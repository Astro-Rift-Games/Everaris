Shader "Grimhold/Collapse/CollapseRiftProcedural"
{
    Properties
    {
        [PerRendererData] _MainTex ("Procedural Rift Atlas", 2D) = "black" {}
        _CoreColor ("Core Color", Color) = (0.098, 0.098, 0.110, 1)
        _EdgeColor ("Edge Color", Color) = (0.161, 0.161, 0.180, 1)
        [HDR] _EmissionColor ("Emission Color", Color) = (0.29, 0.72, 0.56, 1)
        _MaxEmissionStrength ("Max Emission Scale", Range(1, 8)) = 4.0
        _EdgeGlowMode ("Glow Polarity (0=Wall Core, 1=Floor Edge)", Range(0, 1)) = 1.0
        _GrowthFrontWidth ("Spawn Growth Front Width", Range(0.02, 0.35)) = 0.14
        _CellPixelSize ("Atlas Cell Size (px)", Float) = 48.0

        [Header(Eruption Flame Plume Masked by Rift)]
        _FlameHeightPx ("Flame Height (px)", Range(0, 22)) = 15.0
        _FlameSpeed ("Flame Animation FPS", Range(2, 20)) = 12.0
        _FlameIntensity ("Flame Brightness", Range(0, 4)) = 1.65
        _FlameColorHot ("Flame Hot Core Color", Color) = (0.48, 0.96, 0.78, 1)
        _FlameColorMid ("Flame Mid Color", Color) = (0.29, 0.65, 0.51, 1)
        _FlameColorTip ("Flame Outer Tip Color", Color) = (0.14, 0.28, 0.25, 1)

        [MaterialToggle] _ZWrite ("ZWrite", Float) = 0

        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

        float RiftHash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        // Crisp block-sampled pixel-art noise (no bilinear blur).
        float RiftPixelNoise(float2 cell)
        {
            float2 i = floor(cell);
            float2 f = frac(cell);
            float a = RiftHash21(i);
            float b = RiftHash21(i + float2(1.0, 0.0));
            float c = RiftHash21(i + float2(0.0, 1.0));
            float d = RiftHash21(i + float2(1.0, 1.0));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        // Evaluates a pixel-art cyan flame/wisp plume rising directly out of THIS rift's baked atlas silhouette.
        // Strictly clamped inside the current _CellPixelSize tile so adjacent rifts in the atlas never bleed.
        // Returns (flameMask [0..1], heat [0=tip .. 1=hot core]).
        half2 EvaluateMaskedEruptionFlame(
            TEXTURE2D_PARAM(atlasTex, atlasSampler),
            float2 uv,
            half progress,
            half flameAmount,
            half edgeGlowMode,
            float cellPixelSize,
            float flameHeightPx,
            float flameSpeed)
        {
            if (flameAmount <= 0.01h || flameHeightPx <= 0.5)
            {
                return half2(0.0h, 0.0h);
            }

            uint texW = 1, texH = 1;
            atlasTex.GetDimensions(texW, texH);
            float2 texSize = float2((float)texW, (float)texH);
            float2 invTexSize = rcp(max(float2(1.0, 1.0), texSize));
            float2 px = floor(uv * texSize);
            float cellSz = max(16.0, cellPixelSize);
            float2 tileOrigin = floor(px / cellSz) * cellSz;
            float2 tileMax = tileOrigin + cellSz - 1.0;
            float2 localPx = px - tileOrigin;

            // Quantized ~12fps time step so flames animate like authentic pixel art
            float frameStep = floor(_Time.y * flameSpeed);
            float tileSeed = RiftHash21(tileOrigin * 0.07) * 31.7;

            // Subtle pixel-snapped horizontal flicker as flames rise
            float sway = round(sin(localPx.y * 0.42 - frameStep * 0.85 + tileSeed) * 0.85);
            float activeHeight = max(1.0, flameHeightPx * (float)flameAmount);

            // Scan downward inside the same atlas tile to find if this pixel sits directly above THIS rift's mask
            float bestStrength = 0.0;
            float bestHeat = 0.0;

            [unroll]
            for (int stepIdx = 0; stepIdx <= 12; stepIdx++)
            {
                float dy = round(((float)stepIdx / 12.0) * activeHeight);
                float2 samplePx = float2(px.x + sway * step(1.0, dy), px.y - dy);

                if (samplePx.x < tileOrigin.x || samplePx.x > tileMax.x ||
                    samplePx.y < tileOrigin.y || samplePx.y > tileMax.y)
                {
                    continue;
                }

                float2 sampleUv = (samplePx + 0.5) * invTexSize;
                half4 s = SAMPLE_TEXTURE2D(atlasTex, atlasSampler, sampleUv);
                if (s.r < 0.5h || progress < s.g)
                {
                    continue;
                }

                // Core pixels (s.b=1, low BFS order s.g) launch the tallest roaring fire jet;
                // outer branches launch shorter licking tongues following the exact procedural shape.
                float coreWeight = lerp(0.42, 1.15, (float)s.b);
                float centerWeight = lerp(1.0, 0.45, (float)s.g);
                float localMaxH = max(2.0, activeHeight * coreWeight * centerWeight);

                float hNorm = dy / localMaxH;
                if (hNorm > 1.0)
                {
                    continue;
                }

                // Rising pixel-art tongue noise (coarse tongues + fine ember breakup)
                float2 tongueCoord = float2((localPx.x + sway) * 0.34 + tileSeed, (localPx.y - frameStep * 1.45) * 0.24);
                float2 sparkCoord = float2(localPx.x * 0.68 - tileSeed, localPx.y * 0.58 - frameStep * 2.2);
                float nCoarse = RiftPixelNoise(tongueCoord);
                float nFine = RiftPixelNoise(sparkCoord);
                float flameShape = nCoarse * 0.68 + nFine * 0.32;

                // Floor fire is dense near the pit and shoots tall jagged tongues upward; Wall wisps stay thin & wispy
                float threshold = lerp(lerp(0.36, 0.88, hNorm), lerp(0.10, 0.74, hNorm), (float)edgeGlowMode);
                float strength = step(threshold, flameShape);

                if (edgeGlowMode > 0.5h && dy <= 2.0 && s.b > 0.5h)
                {
                    strength = max(strength, step(0.16, flameShape));
                }

                if (strength > 0.5)
                {
                    float heat = saturate((1.0 - hNorm) * 0.82 + flameShape * 0.30 - 0.12);
                    if (heat > bestHeat || bestStrength < 0.5)
                    {
                        bestStrength = 1.0;
                        bestHeat = max(bestHeat, heat);
                    }
                }
            }

            return half2((half)bestStrength, (half)bestHeat);
        }

        half3 QuantizeFlamePalette(half heat, half3 colHot, half3 colMid, half3 colTip)
        {
            // Strict 3-band Catacombs pixel-art fire ramp (no muddy gradients)
            half3 col = colTip;
            col = lerp(col, colMid, step(0.32h, heat));
            col = lerp(col, colHot, step(0.68h, heat));
            return col;
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex LitVertex
            #pragma fragment LitFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _EdgeColor;
                half4 _EmissionColor;
                half _MaxEmissionStrength;
                half _EdgeGlowMode;
                half _GrowthFrontWidth;
                float _CellPixelSize;
                float _FlameHeightPx;
                float _FlameSpeed;
                half _FlameIntensity;
                half4 _FlameColorHot;
                half4 _FlameColorMid;
                half4 _FlameColorTip;
            CBUFFER_END

            Varyings LitVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonLitVertex(input);
                // Keep SpriteRenderer.color intact as continuous shader parameters (no MPB needed -> batching preserved).
                o.color = input.color * unity_SpriteColor;
                return o;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half progress = input.color.r;
                half width    = input.color.g;
                half emission = input.color.b * _MaxEmissionStrength;
                half visible  = input.color.a;

                clip(visible - 0.5h);

                // Atlas channels: R = mask, G = BFS growth order [0..1], B = core [0=edge, 1=core]
                half4 d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half crackVisible = step(0.5h, d.r) * step(d.g, progress);

                // Eruption flames/wisps smoothly driven by width envelope (pre-ignition -> peak -> cooldown)
                half flameAmount = width * saturate(emission * 1.15h);
                half2 flame = EvaluateMaskedEruptionFlame(
                    TEXTURE2D_ARGS(_MainTex, sampler_MainTex),
                    input.uv,
                    progress,
                    flameAmount,
                    _EdgeGlowMode,
                    _CellPixelSize,
                    _FlameHeightPx,
                    _FlameSpeed);

                clip(max(crackVisible, flame.x) - 0.5h);

                half3 flameRgb = QuantizeFlamePalette(flame.y, _FlameColorHot.rgb, _FlameColorMid.rgb, _FlameColorTip.rgb)
                    * (1.0h + _FlameIntensity * flameAmount * (0.15h + 0.35h * flame.y));

                if (crackVisible < 0.5h)
                {
                    // Pure flame pixel rising directly out of the procedural crack below
                    return half4(flameRgb, 1.0h);
                }

                half3 baseCol = lerp(_EdgeColor.rgb, _CoreColor.rgb, d.b);

                // Evaluate URP 2D lighting on baseCol WITHOUT multiplying by input.color (parameter channel!).
                half4 lit = half4(baseCol, 1.0h);
                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(lit.rgb, lit.a, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
                lit = CombinedShapeLightShared(surfaceData, inputData);

                // Floor glows along edges (_EdgeGlowMode = 1); Wall glows inside core with dark outer rim (_EdgeGlowMode = 0).
                half stateGlowMask = lerp(d.b, 1.0h - d.b, _EdgeGlowMode);
                half isSpawning = step(progress, 0.99h);
                half frontDist = saturate(1.0h - (progress - d.g) / max(0.01h, _GrowthFrontWidth));
                half glowWeight = max(stateGlowMask, isSpawning * frontDist);

                lit.rgb += _EmissionColor.rgb * (emission * glowWeight);

                // Blend roaring fire boiling up INSIDE the crack so the plume visibly emerges from the pit itself.
                // On Wall (_EdgeGlowMode = 0), keep the dark outer rim crisp by masking inner fire to d.b.
                half crackFireMask = lerp(d.b * 0.85h, lerp(0.45h, 0.92h, d.b), _EdgeGlowMode);
                half innerFireBlend = flame.x * saturate(flameAmount * crackFireMask);
                lit.rgb = lerp(lit.rgb, flameRgb, innerFireBlend);
                lit.a = 1.0h;
                return lit;
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _EdgeColor;
                half4 _EmissionColor;
                half _MaxEmissionStrength;
                half _EdgeGlowMode;
                half _GrowthFrontWidth;
                float _CellPixelSize;
                float _FlameHeightPx;
                float _FlameSpeed;
                half _FlameIntensity;
                half4 _FlameColorHot;
                half4 _FlameColorMid;
                half4 _FlameColorTip;
            CBUFFER_END

            Varyings UnlitVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color * unity_SpriteColor;
                return o;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half progress = input.color.r;
                half width    = input.color.g;
                half emission = input.color.b * _MaxEmissionStrength;
                half visible  = input.color.a;

                clip(visible - 0.5h);

                half4 d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half crackVisible = step(0.5h, d.r) * step(d.g, progress);

                half flameAmount = width * saturate(emission * 1.15h);
                half2 flame = EvaluateMaskedEruptionFlame(
                    TEXTURE2D_ARGS(_MainTex, sampler_MainTex),
                    input.uv,
                    progress,
                    flameAmount,
                    _EdgeGlowMode,
                    _CellPixelSize,
                    _FlameHeightPx,
                    _FlameSpeed);

                clip(max(crackVisible, flame.x) - 0.5h);

                half3 flameRgb = QuantizeFlamePalette(flame.y, _FlameColorHot.rgb, _FlameColorMid.rgb, _FlameColorTip.rgb)
                    * (1.0h + _FlameIntensity * flameAmount * (0.15h + 0.35h * flame.y));

                if (crackVisible < 0.5h)
                {
                    return half4(flameRgb, 1.0h);
                }

                half3 baseCol = lerp(_EdgeColor.rgb, _CoreColor.rgb, d.b);
                half stateGlowMask = lerp(d.b, 1.0h - d.b, _EdgeGlowMode);
                half isSpawning = step(progress, 0.99h);
                half frontDist = saturate(1.0h - (progress - d.g) / max(0.01h, _GrowthFrontWidth));
                half glowWeight = max(stateGlowMask, isSpawning * frontDist);

                half3 finalRgb = baseCol + _EmissionColor.rgb * (emission * glowWeight);
                half crackFireMask = lerp(d.b * 0.85h, lerp(0.45h, 0.92h, d.b), _EdgeGlowMode);
                half innerFireBlend = flame.x * saturate(flameAmount * crackFireMask);
                finalRgb = lerp(finalRgb, flameRgb, innerFireBlend);
                return half4(finalRgb, 1.0h);
            }
            ENDHLSL
        }
    }
}

using UnityEngine;

/// <summary>
/// Maps continuous collapse rift visual state into vertex color channels
/// (R = progress [0..1], G = flame/eruption envelope [0..1], B = emission / 4 [0..1], A = visibility [0|1])
/// for batch-friendly rendering with <c>CollapseRiftProcedural.shader</c> (TASK-341).
/// Replicates the progressive glow-up (Telegraph), rising/peaking/receding fire growth (Erupting),
/// and smooth ember fade (Cooldown) of the original spritesheet animations.
/// </summary>
public static class CollapseRiftShaderParameterMapper
{
    public const float MaxShaderEmissionScale = 4.0f;
    private const float PreIgnitionWidth = 0.18f;
    private const float EruptionEndWidth = 0.28f;

    public static Color Evaluate(
        CollapseRiftVisualState state,
        float normalizedStateProgress,
        float pulseFactor,
        float telegraphEmission,
        float eruptionEmission,
        float cooldownEmission,
        float spawnEmission = 0.65f)
    {
        float t = Mathf.Clamp01(normalizedStateProgress);
        float pulse = Mathf.Clamp01(pulseFactor);

        switch (state)
        {
            case CollapseRiftVisualState.Hidden:
                return new Color(0f, 0f, 0f, 0f);

            case CollapseRiftVisualState.Spawning:
                return new Color(
                    t,
                    0f,
                    NormalizeEmission(spawnEmission),
                    1f);

            case CollapseRiftVisualState.Dormant:
                return new Color(1f, 0f, 0f, 1f);

            case CollapseRiftVisualState.Telegraph:
            {
                // Progressive illumination across Telegraph (mirrors Stage 2 activation buildup)
                float chargeRamp = Mathf.Lerp(0.10f, 1.0f, SmoothStep(0f, 0.85f, t));
                float emission = telegraphEmission * chargeRamp * pulse;
                // Subtle pre-ignition licks in the final 20% of Telegraph before Eruption begins
                float preIgnition = Mathf.Lerp(0f, PreIgnitionWidth, SmoothStep(0.80f, 1.0f, t));
                return new Color(
                    1f,
                    preIgnition,
                    NormalizeEmission(emission),
                    1f);
            }

            case CollapseRiftVisualState.Erupting:
            {
                // Smooth attack (0..0.34) -> roaring peak (0.34..0.68) -> recede (0.68..1.0)
                float flameEnvelope;
                if (t < 0.34f)
                {
                    flameEnvelope = Mathf.Lerp(PreIgnitionWidth, 1.0f, SmoothStep(0f, 0.34f, t));
                }
                else if (t <= 0.68f)
                {
                    flameEnvelope = 1.0f;
                }
                else
                {
                    flameEnvelope = Mathf.Lerp(1.0f, EruptionEndWidth, SmoothStep(0.68f, 1.0f, t));
                }

                float emission;
                if (t < 0.28f)
                {
                    emission = Mathf.Lerp(telegraphEmission, eruptionEmission, SmoothStep(0f, 0.28f, t));
                }
                else if (t <= 0.72f)
                {
                    emission = eruptionEmission;
                }
                else
                {
                    emission = Mathf.Lerp(eruptionEmission, cooldownEmission, SmoothStep(0.72f, 1.0f, t));
                }

                return new Color(
                    1f,
                    flameEnvelope,
                    NormalizeEmission(emission),
                    1f);
            }

            case CollapseRiftVisualState.Cooldown:
            {
                float remain = 1f - SmoothStep(0f, 1f, t);
                return new Color(
                    1f,
                    EruptionEndWidth * remain,
                    NormalizeEmission(cooldownEmission * remain),
                    1f);
            }

            default:
                return new Color(0f, 0f, 0f, 0f);
        }
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float u = Mathf.Clamp01((x - edge0) / Mathf.Max(0.0001f, edge1 - edge0));
        return u * u * (3f - 2f * u);
    }

    private static float NormalizeEmission(float emissionStrength)
    {
        return Mathf.Clamp01(Mathf.Max(0f, emissionStrength) / MaxShaderEmissionScale);
    }
}

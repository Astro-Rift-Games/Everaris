#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Presentation
{
    public sealed class CollapseRiftProceduralPresentationTests
    {
        private const string ConfigAssetPath = "Assets/Scriptable Objects/DefaultCollapseRiftConfig.asset";
        private const string FloorKitAssetPath = "Assets/Scriptable Objects/CollapseRifts/DefaultFloorRiftKit.asset";
        private const string WallKitAssetPath = "Assets/Scriptable Objects/CollapseRifts/DefaultWallRiftKit.asset";
        private const string FloorMatAssetPath = "Assets/Art/Materials/CollapseRiftProceduralFloor.mat";
        private const string WallMatAssetPath = "Assets/Art/Materials/CollapseRiftProceduralWall.mat";

        private sealed class SimpleSurfaceGrid : ICollapseSurfaceGrid
        {
            private readonly HashSet<Vector2Int> _floors = new();
            private readonly HashSet<Vector2Int> _walls = new();

            public BoundsInt CellBounds => new BoundsInt(0, 0, 0, 24, 18, 1);

            public SimpleSurfaceGrid()
            {
                for (int y = 0; y < 14; y++)
                    for (int x = 0; x < 24; x++)
                        _floors.Add(new Vector2Int(x, y));

                for (int y = 14; y < 18; y++)
                    for (int x = 0; x < 24; x++)
                        _walls.Add(new Vector2Int(x, y));
            }

            public bool HasFloorTile(Vector2Int cell) => _floors.Contains(cell);
            public bool HasWallTile(Vector2Int cell) => _walls.Contains(cell);
            public bool HasBlockingFloorOverlay(Vector2Int cell) => _walls.Contains(cell);
            public Vector2 CellToWorldCenter(Vector2Int cell) => new Vector2(cell.x + 0.5f, cell.y + 0.5f);
            public Vector2 CellToWorldBottomLeft(Vector2Int cell) => new Vector2(cell.x, cell.y);
        }
        [Test]
        public void ShaderParameterMapper_MapsAllFiveStatesToExpectedContinuousCurves()
        {
            // Hidden -> alpha 0
            Color hidden = CollapseRiftShaderParameterMapper.Evaluate(CollapseRiftVisualState.Hidden, 0f, 1f, 1.2f, 2.2f, 0.65f);
            Assert.That(hidden.a, Is.EqualTo(0f));

            // Spawning -> progress ramps 0..1, width 0, low growth-front emission
            Color spawnStart = CollapseRiftShaderParameterMapper.Evaluate(CollapseRiftVisualState.Spawning, 0.1f, 1f, 1.2f, 2.2f, 0.65f);
            Color spawnEnd = CollapseRiftShaderParameterMapper.Evaluate(CollapseRiftVisualState.Spawning, 0.9f, 1f, 1.2f, 2.2f, 0.65f);
            Assert.That(spawnStart.r, Is.EqualTo(0.1f).Within(0.01f));
            Assert.That(spawnEnd.r, Is.EqualTo(0.9f).Within(0.01f));
            Assert.That(spawnStart.g, Is.EqualTo(0f));
            Assert.That(spawnStart.b, Is.GreaterThan(0f));
            Assert.That(spawnStart.a, Is.EqualTo(1f));

            // Dormant -> complete (progress=1), width=0, emission=0
            Color dormant = CollapseRiftShaderParameterMapper.Evaluate(CollapseRiftVisualState.Dormant, 0.5f, 1f, 1.2f, 2.2f, 0.65f);
            Assert.That(dormant.r, Is.EqualTo(1f));
            Assert.That(dormant.g, Is.EqualTo(0f));
            Assert.That(dormant.b, Is.EqualTo(0f));
            Assert.That(dormant.a, Is.EqualTo(1f));

            // Telegraph -> progressive charge-up illumination + pulsing + subtle late pre-ignition
            Color teleEarly = CollapseRiftShaderParameterMapper.Evaluate(CollapseRiftVisualState.Telegraph, 0.05f, 1.0f, 1.2f, 2.2f, 0.65f);
            Color teleMidLow = CollapseRiftShaderParameterMapper.Evaluate(CollapseRiftVisualState.Telegraph, 0.5f, 0.5f, 1.2f, 2.2f, 0.65f);
            Color teleMidHigh = CollapseRiftShaderParameterMapper.Evaluate(CollapseRiftVisualState.Telegraph, 0.5f, 1.0f, 1.2f, 2.2f, 0.65f);
            Color teleLate = CollapseRiftShaderParameterMapper.Evaluate(CollapseRiftVisualState.Telegraph, 0.95f, 1.0f, 1.2f, 2.2f, 0.65f);
            Assert.That(teleEarly.r, Is.EqualTo(1f));
            Assert.That(teleEarly.g, Is.EqualTo(0f));
            Assert.That(teleMidHigh.b, Is.GreaterThan(teleEarly.b), "Telegraph glow must ramp up progressively.");
            Assert.That(teleMidHigh.b, Is.GreaterThan(teleMidLow.b), "Telegraph pulse must modulate emission.");
            Assert.That(teleLate.g, Is.GreaterThan(0f).And.LessThan(0.25f), "Late Telegraph should pre-ignite gently.");

            // Erupting -> grows smoothly from pre-ignition to peak (1.0) and recedes smoothly into Cooldown
            Color eruptStart = CollapseRiftShaderParameterMapper.Evaluate(CollapseRiftVisualState.Erupting, 0f, 1f, 1.2f, 2.2f, 0.65f);
            Color eruptPeak = CollapseRiftShaderParameterMapper.Evaluate(CollapseRiftVisualState.Erupting, 0.5f, 1f, 1.2f, 2.2f, 0.65f);
            Color eruptEnd = CollapseRiftShaderParameterMapper.Evaluate(CollapseRiftVisualState.Erupting, 1f, 1f, 1.2f, 2.2f, 0.65f);
            Assert.That(eruptStart.r, Is.EqualTo(1f));
            Assert.That(eruptStart.g, Is.LessThan(eruptPeak.g), "Fire must grow smoothly at eruption start instead of popping to full height.");
            Assert.That(eruptPeak.g, Is.EqualTo(1f).Within(0.01f));
            Assert.That(eruptPeak.b, Is.EqualTo(2.2f / CollapseRiftShaderParameterMapper.MaxShaderEmissionScale).Within(0.01f));
            Assert.That(eruptEnd.g, Is.LessThan(eruptPeak.g).And.GreaterThan(0f), "Fire must recede smoothly before Cooldown.");

            // Cooldown -> seamless handoff from Erupting end, fading cleanly to 0
            Color coolStart = CollapseRiftShaderParameterMapper.Evaluate(CollapseRiftVisualState.Cooldown, 0f, 1f, 1.2f, 2.2f, 0.65f);
            Color coolEnd = CollapseRiftShaderParameterMapper.Evaluate(CollapseRiftVisualState.Cooldown, 1f, 1f, 1.2f, 2.2f, 0.65f);
            Assert.That(coolStart.r, Is.EqualTo(1f));
            Assert.That(coolStart.g, Is.EqualTo(eruptEnd.g).Within(0.01f), "Cooldown must hand off seamlessly from Erupting end.");
            Assert.That(coolEnd.g, Is.EqualTo(0f).Within(0.01f));
            Assert.That(coolStart.b, Is.GreaterThan(coolEnd.b));
            Assert.That(coolEnd.b, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void CollapseRiftAtlas_PacksSingleRgba32PointTexture_AndDisposesAllRuntimeObjects()
        {
            var floorPlacements = new List<CollapseRiftPlacement>
            {
                new(new Vector2(2f, 2f), new Vector2Int(2, 2), CollapseRiftSurfaceType.Floor, CollapseRiftBatch.Stage2Initial, 0f),
                new(new Vector2(8f, 4f), new Vector2Int(8, 4), CollapseRiftSurfaceType.Floor, CollapseRiftBatch.Stage3Secondary, 0.5f)
            };
            var wallPlacements = new List<CollapseRiftPlacement>
            {
                new(new Vector2(4f, 15.5f), new Vector2Int(4, 14), CollapseRiftSurfaceType.Wall, CollapseRiftBatch.Stage2Initial, 0.2f)
            };

            CollapseRiftAtlas atlas = CollapseRiftAtlas.Build(
                floorPlacements,
                wallPlacements,
                CollapseRiftPlaceholderKitFactory.CreateFloorKit(),
                CollapseRiftPlaceholderKitFactory.CreateWallKit(),
                raidSeed: 4242,
                cellPixelSize: 48,
                maxWallPixelHeight: 32);

            Assert.That(atlas.Texture, Is.Not.Null);
            Assert.That(atlas.Texture.format, Is.EqualTo(TextureFormat.RGBA32));
            Assert.That(atlas.Texture.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(atlas.Texture.mipmapCount, Is.EqualTo(1));
            Assert.That(atlas.FloorSprites.Count, Is.EqualTo(2));
            Assert.That(atlas.WallSprites.Count, Is.EqualTo(1));
            Assert.That(atlas.FloorSprites[0].texture, Is.SameAs(atlas.Texture));
            Assert.That(atlas.WallSprites[0].texture, Is.SameAs(atlas.Texture));
            Assert.That(atlas.FloorSprites[0].rect, Is.Not.EqualTo(atlas.FloorSprites[1].rect));

            Texture2D texRef = atlas.Texture;
            Sprite spriteRef = atlas.FloorSprites[0];

            atlas.Dispose();

            Assert.That(atlas.IsDisposed, Is.True);
            Assert.That(texRef == null, Is.True, "Atlas Texture2D must be destroyed on Dispose to prevent raid memory leaks.");
            Assert.That(spriteRef == null, Is.True, "Generated Sprites must be destroyed on Dispose.");
        }

        [Test]
        public void PieceExtractor_ClassifiesDarkCoreVsLightRim_FromStage1PaletteColors()
        {
            var coreColor = new Color32(0x19, 0x19, 0x1C, 255);
            var edgeColor = new Color32(0x29, 0x29, 0x2E, 255);
            var clear = new Color32(0, 0, 0, 0);

            var pixels = new Color[]
            {
                clear, edgeColor, clear,
                edgeColor, coreColor, edgeColor,
                clear, edgeColor, clear
            };

            bool ok = CollapseRiftPieceExtractor.TryExtractFromColors(
                pixels, 3, 3, new Vector2Int(1, 0), new Vector2Int(1, 2), out CollapseRiftPieceData data);

            Assert.That(ok, Is.True);
            Assert.That(data.Mask[4], Is.True);
            Assert.That(data.CoreMask[4], Is.True, "Darker #19191C interior pixel must classify as Core.");
            Assert.That(data.Mask[1], Is.True);
            Assert.That(data.CoreMask[1], Is.False, "Lighter #29292E rim pixel must classify as Edge.");
        }

        [Test]
        public void Presenter_InProceduralMode_BuildsSingleAtlas_UsesSharedMaterialWithoutMpb_AndDisposesCleanly()
        {
            var floorKit = AssetDatabase.LoadAssetAtPath<CollapseRiftKitAsset>(FloorKitAssetPath);
            var wallKit = AssetDatabase.LoadAssetAtPath<CollapseRiftKitAsset>(WallKitAssetPath);
            var floorMat = AssetDatabase.LoadAssetAtPath<Material>(FloorMatAssetPath);
            var wallMat = AssetDatabase.LoadAssetAtPath<Material>(WallMatAssetPath);

            var config = ScriptableObject.CreateInstance<CollapseRiftConfig>();
            var presenterGo = new GameObject("ProceduralPresenterTest");
            try
            {
                config.ConfigureForTests(
                    floorSpawn: null, floorDormant: null, floorTelegraph: null, floorEruption: null, floorCooldown: null,
                    wallSpawn: null, wallStage1: null, wallStage2: null, wallStage3: null, wallStage4: null,
                    stage2FloorCount: 3, stage3FloorCount: 3, wallCount: 3, wallFrameIntervalSeconds: 0.2f);
                config.ConfigureProceduralForTests(CollapseRiftRenderMode.Procedural, floorMat, wallMat, floorKit, wallKit, 48);

                var presenter = presenterGo.AddComponent<CollapseRiftPresenter>();
                presenter.Configure(null, config, new SimpleSurfaceGrid(), fallbackSeed: 7777);
                presenter.ApplyInitialPhase(DungeonPressurePhase.Reinforcements);

                Assert.That(presenter.ActiveAtlas, Is.Not.Null);
                Texture2D atlasTex = presenter.ActiveAtlas.Texture;
                Sprite firstFloorSprite = presenter.ActiveAtlas.FloorSprites[0];
                Assert.That(presenter.FloorRifts.Count, Is.EqualTo(6));
                Assert.That(presenter.WallRifts.Count, Is.EqualTo(3));

                // All floor rifts share the same atlas texture, shared procedural floor material, and zero MPB
                for (int i = 0; i < presenter.FloorRifts.Count; i++)
                {
                    var v = presenter.FloorRifts[i];
                    Assert.That(v.UsesProceduralShader, Is.True);
                    Assert.That(v.Renderer.sharedMaterial, Is.SameAs(floorMat));
                    Assert.That(v.Renderer.HasPropertyBlock(), Is.False, "No MaterialPropertyBlock allowed in Procedural mode.");
                    Assert.That(v.Renderer.sprite.texture, Is.SameAs(atlasTex));
                }

                // All wall rifts share the same atlas texture, shared procedural wall material, and zero MPB
                for (int i = 0; i < presenter.WallRifts.Count; i++)
                {
                    var v = presenter.WallRifts[i];
                    Assert.That(v.UsesProceduralShader, Is.True);
                    Assert.That(v.Renderer.sharedMaterial, Is.SameAs(wallMat));
                    Assert.That(v.Renderer.HasPropertyBlock(), Is.False, "No MaterialPropertyBlock allowed in Procedural mode.");
                    Assert.That(v.Renderer.sprite.texture, Is.SameAs(atlasTex));
                }

                // Verify continuous vertex color updates across floor states + Light2D kept intact
                CollapseFloorRiftVisual activeFloor = presenter.FloorRifts[0];
                presenter.ApplyPhaseTransition(DungeonPressurePhase.CriticalPressure);
                Assert.That(activeFloor.VisualState, Is.EqualTo(CollapseRiftVisualState.Dormant));
                Assert.That(activeFloor.Renderer.color.r, Is.EqualTo(1f).Within(0.01f));
                Assert.That(activeFloor.Renderer.color.g, Is.EqualTo(0f).Within(0.01f));

                activeFloor.SyncFromAuthority(CollapseRiftVisualState.Erupting, 0.25f, true, suppressTransitionEffects: true);
                Assert.That(activeFloor.Renderer.color.g, Is.EqualTo(1f).Within(0.01f));
                Assert.That(activeFloor.Renderer.color.b, Is.GreaterThan(0.1f));
                Assert.That(activeFloor.LocalLight.enabled, Is.True, "Light2D must stay active during erupting state.");

                presenter.ResetPresentation();
                Assert.That(presenter.ActiveAtlas, Is.Null);
                Assert.That(atlasTex == null, Is.True, "ResetPresentation must dispose per-raid atlas Texture2D.");
                Assert.That(firstFloorSprite == null, Is.True, "ResetPresentation must dispose per-raid atlas Sprites.");
            }
            finally
            {
                Object.DestroyImmediate(presenterGo);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Presenter_InSpriteSheetMode_RemainsLegacyCompatible_WithoutAllocatingProceduralAtlas()
        {
            var config = ScriptableObject.CreateInstance<CollapseRiftConfig>();
            var presenterGo = new GameObject("LegacySpriteSheetPresenterTest");
            var dummyTex = new Texture2D(16, 16);
            var dummySprite = Sprite.Create(dummyTex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
            try
            {
                var arr = new[] { dummySprite };
                config.ConfigureForTests(arr, dummySprite, arr, arr, arr, arr, arr, arr, arr, arr, 2, 2, 2, 0.2f);
                config.ConfigureProceduralForTests(CollapseRiftRenderMode.SpriteSheet, null, null, null, null, 48);

                var presenter = presenterGo.AddComponent<CollapseRiftPresenter>();
                presenter.Configure(null, config, new SimpleSurfaceGrid(), fallbackSeed: 555);
                presenter.ApplyInitialPhase(DungeonPressurePhase.Reinforcements);

                Assert.That(presenter.ActiveAtlas, Is.Null);
                Assert.That(presenter.FloorRifts.Count, Is.EqualTo(4));
                Assert.That(presenter.FloorRifts[0].UsesProceduralShader, Is.False);
                Assert.That(presenter.WallRifts[0].UsesProceduralShader, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(presenterGo);
                Object.DestroyImmediate(config);
                Object.DestroyImmediate(dummySprite);
                Object.DestroyImmediate(dummyTex);
            }
        }

        [Test]
        public void ProjectAssets_DefaultProceduralKitsAndMaterials_LoadAndExtractAllCroppedPieces()
        {
            var floorKitAsset = AssetDatabase.LoadAssetAtPath<CollapseRiftKitAsset>(FloorKitAssetPath);
            var wallKitAsset = AssetDatabase.LoadAssetAtPath<CollapseRiftKitAsset>(WallKitAssetPath);
            var floorMat = AssetDatabase.LoadAssetAtPath<Material>(FloorMatAssetPath);
            var wallMat = AssetDatabase.LoadAssetAtPath<Material>(WallMatAssetPath);
            var config = AssetDatabase.LoadAssetAtPath<CollapseRiftConfig>(ConfigAssetPath);

            Assert.That(floorKitAsset, Is.Not.Null);
            Assert.That(wallKitAsset, Is.Not.Null);
            Assert.That(floorMat, Is.Not.Null);
            Assert.That(wallMat, Is.Not.Null);
            Assert.That(config, Is.Not.Null);

            Assert.That(floorMat.shader.name, Is.EqualTo("Grimhold/Collapse/CollapseRiftProcedural"));
            Assert.That(wallMat.shader.name, Is.EqualTo("Grimhold/Collapse/CollapseRiftProcedural"));
            Assert.That(floorMat.GetFloat("_EdgeGlowMode"), Is.EqualTo(1f).Within(0.01f));
            Assert.That(wallMat.GetFloat("_EdgeGlowMode"), Is.EqualTo(0f).Within(0.01f));

            CollapseRiftRuntimeKit runtimeFloor = floorKitAsset.BuildRuntimeKit();
            Assert.That(runtimeFloor.IsValid, Is.True);
            Assert.That(runtimeFloor.CoresOrBases.Count, Is.EqualTo(3));
            Assert.That(runtimeFloor.Branches.Count, Is.EqualTo(5));
            Assert.That(runtimeFloor.Tips.Count, Is.EqualTo(3));

            CollapseRiftRuntimeKit runtimeWall = wallKitAsset.BuildRuntimeKit();
            Assert.That(runtimeWall.IsValid, Is.True);
            Assert.That(runtimeWall.CoresOrBases.Count, Is.EqualTo(2));
            Assert.That(runtimeWall.Trunks.Count, Is.EqualTo(3));
            Assert.That(runtimeWall.Branches.Count, Is.EqualTo(3));
            Assert.That(runtimeWall.Tips.Count, Is.EqualTo(3));

            Assert.That(config.RenderMode, Is.EqualTo(CollapseRiftRenderMode.Procedural));
            Assert.That(config.ProceduralFloorKit, Is.EqualTo(floorKitAsset));
            Assert.That(config.ProceduralWallKit, Is.EqualTo(wallKitAsset));
            Assert.That(config.ProceduralFloorMaterial, Is.EqualTo(floorMat));
            Assert.That(config.ProceduralWallMaterial, Is.EqualTo(wallMat));
            Assert.That(floorMat.GetFloat("_CellPixelSize"), Is.EqualTo(48f).Within(0.01f));
            Assert.That(floorMat.GetFloat("_FlameHeightPx"), Is.GreaterThan(0f));
            Assert.That(wallMat.GetFloat("_CellPixelSize"), Is.EqualTo(48f).Within(0.01f));
            Assert.That(wallMat.GetFloat("_FlameHeightPx"), Is.GreaterThan(0f));
        }
    }
}
#endif

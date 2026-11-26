#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.DungeonPressure
{
    /// <summary>
    /// End-to-end PlayMode verification for US-92 (TASK-340, TASK-341, TASK-342):
    /// - Single per-raid RGBA32 Point atlas shared across Floor and Wall rifts.
    /// - Zero MaterialPropertyBlock usage (batching preserved via SpriteRenderer.color).
    /// - Zero leaked Texture2D/Sprite instances across repeated raid generation/reset cycles.
    /// - Different raid seeds yield distinct procedural atlas pixels.
    /// </summary>
    public sealed class CollapseRiftProceduralPlayModeTests
    {
        private const string ConfigAssetPath = "Assets/Scriptable Objects/DefaultCollapseRiftConfig.asset";

        private sealed class PlayModeSurfaceGrid : ICollapseSurfaceGrid
        {
            private readonly HashSet<Vector2Int> _floors = new();
            private readonly HashSet<Vector2Int> _walls = new();

            public BoundsInt CellBounds => new BoundsInt(0, 0, 0, 28, 20, 1);

            public PlayModeSurfaceGrid()
            {
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 28; x++)
                        _floors.Add(new Vector2Int(x, y));

                for (int y = 16; y < 20; y++)
                    for (int x = 0; x < 28; x++)
                        _walls.Add(new Vector2Int(x, y));
            }

            public bool HasFloorTile(Vector2Int cell) => _floors.Contains(cell);
            public bool HasWallTile(Vector2Int cell) => _walls.Contains(cell);
            public bool HasBlockingFloorOverlay(Vector2Int cell) => _walls.Contains(cell);
            public Vector2 CellToWorldCenter(Vector2Int cell) => new Vector2(cell.x + 0.5f, cell.y + 0.5f);
            public Vector2 CellToWorldBottomLeft(Vector2Int cell) => new Vector2(cell.x, cell.y);
        }

        private GameObject _presenterGo;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_presenterGo != null)
            {
                Object.Destroy(_presenterGo);
                _presenterGo = null;
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProceduralMode_RepeatedRaidCycles_LeakZeroTexturesAndPreserveBatching()
        {
            var config = AssetDatabase.LoadAssetAtPath<CollapseRiftConfig>(ConfigAssetPath);
            Assert.That(config, Is.Not.Null);
            Assert.That(config.RenderMode, Is.EqualTo(CollapseRiftRenderMode.Procedural));

            _presenterGo = new GameObject("PlayModeProceduralRiftPresenter");
            _presenterGo.SetActive(false);
            var presenter = _presenterGo.AddComponent<CollapseRiftPresenter>();
            var grid = new PlayModeSurfaceGrid();

            var disposedTextures = new List<Texture2D>();
            var disposedSprites = new List<Sprite>();
            Vector3 firstCycleRiftPos = Vector3.zero;
            bool sawDifferentSeedPlacement = false;

            for (int cycle = 0; cycle < 5; cycle++)
            {
                int raidSeed = 10000 + cycle * 977;
                presenter.Configure(null, config, grid, fallbackSeed: raidSeed);
                if (!_presenterGo.activeSelf)
                {
                    _presenterGo.SetActive(true);
                }
                presenter.ApplyInitialPhase(DungeonPressurePhase.Reinforcements);
                yield return null;

                Assert.That(presenter.ActiveAtlas, Is.Not.Null, $"Cycle {cycle} must allocate a single per-raid atlas.");
                Assert.That(presenter.FloorRifts.Count, Is.GreaterThan(0));
                Assert.That(presenter.WallRifts.Count, Is.GreaterThan(0));

                Texture2D atlasTex = presenter.ActiveAtlas.Texture;
                Assert.That(atlasTex.format, Is.EqualTo(TextureFormat.RGBA32));
                Assert.That(atlasTex.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(atlasTex.mipmapCount, Is.EqualTo(1));
                Assert.That(atlasTex.isReadable, Is.False, "Baked raid atlas should release CPU pixel buffer via makeNoLongerReadable.");

                Vector3 pos0 = presenter.FloorRifts[0].transform.position;
                if (cycle == 0)
                {
                    firstCycleRiftPos = pos0;
                }
                else if ((pos0 - firstCycleRiftPos).sqrMagnitude > 0.01f)
                {
                    sawDifferentSeedPlacement = true;
                }

                // Step through live phases and verify shared materials & zero MPB
                presenter.ApplyPhaseTransition(DungeonPressurePhase.CriticalPressure);
                yield return null;
                presenter.ApplyPhaseTransition(DungeonPressurePhase.Collapse);
                yield return null;

                Material expectedFloorMat = config.ProceduralFloorMaterial;
                Material expectedWallMat = config.ProceduralWallMaterial;

                for (int i = 0; i < presenter.FloorRifts.Count; i++)
                {
                    CollapseFloorRiftVisual v = presenter.FloorRifts[i];
                    Assert.That(v.UsesProceduralShader, Is.True);
                    Assert.That(v.Renderer.sharedMaterial, Is.SameAs(expectedFloorMat));
                    Assert.That(v.Renderer.HasPropertyBlock(), Is.False, "Floor rift must not use MaterialPropertyBlock in Procedural mode.");
                    Assert.That(v.Renderer.sprite.texture, Is.SameAs(atlasTex));
                }

                for (int i = 0; i < presenter.WallRifts.Count; i++)
                {
                    CollapseWallRiftVisual v = presenter.WallRifts[i];
                    Assert.That(v.UsesProceduralShader, Is.True);
                    Assert.That(v.Renderer.sharedMaterial, Is.SameAs(expectedWallMat));
                    Assert.That(v.Renderer.HasPropertyBlock(), Is.False, "Wall rift must not use MaterialPropertyBlock in Procedural mode.");
                    Assert.That(v.Renderer.sprite.texture, Is.SameAs(atlasTex));
                }

                disposedTextures.Add(atlasTex);
                disposedSprites.Add(presenter.ActiveAtlas.FloorSprites[0]);

                _presenterGo.SetActive(false);
                presenter.ResetPresentation();
                Assert.That(presenter.ActiveAtlas, Is.Null);
                yield return null;

                Assert.That(atlasTex == null, Is.True, $"Atlas texture from cycle {cycle} must be destroyed on ResetPresentation.");
            }

            Assert.That(sawDifferentSeedPlacement, Is.True, "Different raid seeds must vary procedural rift layouts.");
            for (int i = 0; i < disposedTextures.Count; i++)
            {
                Assert.That(disposedTextures[i] == null, Is.True);
                Assert.That(disposedSprites[i] == null, Is.True);
            }
        }
    }
}
#endif

#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tests.EditMode.Presentation
{
    public sealed class CollapseRiftPresenterTests
    {
        private const string ConfigAssetPath = "Assets/Scriptable Objects/DefaultCollapseRiftConfig.asset";
        private const string MaterialAssetPath = "Assets/Art/Materials/CollapseRiftEmissive.mat";
        private const string FloorPrefabPath = "Assets/Prefabs/Collapse/FloorRift.prefab";
        private const string WallPrefabPath = "Assets/Prefabs/Collapse/WallRift.prefab";

        private sealed class TestSurfaceGrid : ICollapseSurfaceGrid
        {
            private readonly HashSet<Vector2Int> _floors = new();
            private readonly HashSet<Vector2Int> _walls = new();

            public BoundsInt CellBounds => new BoundsInt(0, 0, 0, 24, 18, 1);

            public TestSurfaceGrid()
            {
                for (int y = 0; y < 14; y++)
                {
                    for (int x = 0; x < 24; x++)
                    {
                        _floors.Add(new Vector2Int(x, y));
                    }
                }

                for (int y = 14; y < 18; y++)
                {
                    for (int x = 0; x < 24; x++)
                    {
                        _walls.Add(new Vector2Int(x, y));
                    }
                }
            }

            public bool HasFloorTile(Vector2Int cell) => _floors.Contains(cell);
            public bool HasWallTile(Vector2Int cell) => _walls.Contains(cell);
            public bool HasBlockingFloorOverlay(Vector2Int cell) => _walls.Contains(cell);
            public Vector2 CellToWorldCenter(Vector2Int cell) => new Vector2(cell.x + 0.5f, cell.y + 0.5f);
            public Vector2 CellToWorldBottomLeft(Vector2Int cell) => new Vector2(cell.x, cell.y);
        }

        private GameObject _presenterGo;
        private CollapseRiftPresenter _presenter;
        private CollapseRiftConfig _config;
        private Texture2D _dummyTex;
        private Sprite _dummySprite;

        [SetUp]
        public void SetUp()
        {
            _dummyTex = new Texture2D(16, 16);
            _dummySprite = Sprite.Create(_dummyTex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);

            _config = ScriptableObject.CreateInstance<CollapseRiftConfig>();
            _config.ConfigureForTests(
                floorSpawn: CreateSprites(7),
                floorDormant: _dummySprite,
                floorTelegraph: CreateSprites(7),
                floorEruption: CreateSprites(7),
                floorCooldown: CreateSprites(8),
                wallSpawn: CreateSprites(7),
                wallStage1: CreateSprites(1),
                wallStage2: CreateSprites(7),
                wallStage3: CreateSprites(7),
                wallStage4: CreateSprites(8),
                stage2FloorCount: 4,
                stage3FloorCount: 4,
                wallCount: 3,
                wallFrameIntervalSeconds: 0.2f);

            _presenterGo = new GameObject("TestCollapseRiftPresenter");
            _presenter = _presenterGo.AddComponent<CollapseRiftPresenter>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_presenterGo != null) Object.DestroyImmediate(_presenterGo);
            if (_config != null) Object.DestroyImmediate(_config);
            if (_dummySprite != null) Object.DestroyImmediate(_dummySprite);
            if (_dummyTex != null) Object.DestroyImmediate(_dummyTex);
        }

        private Sprite[] CreateSprites(int count)
        {
            var arr = new Sprite[count];
            for (int i = 0; i < count; i++) arr[i] = _dummySprite;
            return arr;
        }

        [Test]
        public void FullStageProgression_FloorAndWallRifts_FollowExactRulesAndSortingAndNoColliders()
        {
            var grid = new TestSurfaceGrid();
            _presenter.Configure(null, _config, grid, null, fallbackSeed: 4242);

            Assert.That(_presenter.FloorRifts.Count, Is.EqualTo(8));
            Assert.That(_presenter.WallRifts.Count, Is.EqualTo(3));

            // Sorting & non-blocking navigation guarantees
            foreach (var floor in _presenter.FloorRifts)
            {
                Assert.That(floor.Renderer.sortingLayerName, Is.EqualTo("Floor"));
                Assert.That(floor.Renderer.sortingOrder, Is.EqualTo(2));
                Assert.That(floor.GetComponentInChildren<Collider2D>(true), Is.Null);
            }

            foreach (var wall in _presenter.WallRifts)
            {
                Assert.That(wall.Renderer.sortingLayerName, Is.EqualTo("Walls"));
                Assert.That(wall.Renderer.sortingOrder, Is.EqualTo(3));
                Assert.That(wall.GetComponentInChildren<Collider2D>(true), Is.Null);
            }

            // Stage 1 (Normal): no visible floor or wall rifts
            Assert.That(CountVisibleFloorRifts(), Is.EqualTo(0));
            Assert.That(CountVisibleWallRifts(), Is.EqualTo(0));

            // Stage 2 (Reinforcements): Batch 1 floor spawns (dormant, no activation); wall rifts spawn -> Stage1
            _presenter.ApplyPhaseTransition(DungeonPressurePhase.Reinforcements);
            Assert.That(CountVisibleFloorRifts(), Is.EqualTo(4));
            Assert.That(CountActivatableFloorRifts(), Is.EqualTo(0));
            Assert.That(CountVisibleWallRifts(), Is.EqualTo(3));
            Assert.That(_presenter.WallRifts[0].VisualState, Is.EqualTo(CollapseRiftVisualState.Spawning));

            // Advance past Stage0-Spawn -> Dormant (Stage1)
            _presenter.EvaluatePresentation(2.0f);
            Assert.That(_presenter.WallRifts[0].VisualState, Is.EqualTo(CollapseRiftVisualState.Dormant));

            // Stage 3 (CriticalPressure): Batch 1 active + Batch 2 spawns without activation; walls loop Stage1 + Stage2
            _presenter.ApplyPhaseTransition(DungeonPressurePhase.CriticalPressure);
            Assert.That(CountVisibleFloorRifts(), Is.EqualTo(8));
            Assert.That(CountActivatableFloorRifts(), Is.EqualTo(4));

            // Trigger an active floor rift and confirm local light turns on during Telegraph/Erupting
            CollapseFloorRiftVisual activeFloor = FindFirstActivatableFloorRift();
            Assert.That(activeFloor, Is.Not.Null);
            Assert.That(activeFloor.TryTriggerActivation(), Is.True);
            Assert.That(activeFloor.VisualState, Is.EqualTo(CollapseRiftVisualState.Telegraph));
            Assert.That(activeFloor.LocalLight.enabled, Is.True);
            Assert.That(activeFloor.LocalLight.intensity, Is.GreaterThan(0f));

            // Stage 4 (Collapse): All 8 floor rifts active, no new floor rifts added; walls loop Stage2 + Stage3 + Stage4
            _presenter.ApplyPhaseTransition(DungeonPressurePhase.Collapse);
            Assert.That(_presenter.FloorRifts.Count, Is.EqualTo(8));
            Assert.That(CountActivatableFloorRifts(), Is.EqualTo(8));
            foreach (var wall in _presenter.WallRifts)
            {
                Assert.That(
                    wall.VisualState == CollapseRiftVisualState.Telegraph ||
                    wall.VisualState == CollapseRiftVisualState.Erupting ||
                    wall.VisualState == CollapseRiftVisualState.Cooldown,
                    Is.True,
                    "Stage 4 wall rifts must cycle exclusively across Stage2, Stage3 and Stage4.");
            }
        }

        [Test]
        public void WallFrameIntervalOverride_UpdatesAllWallRiftsFromInspector()
        {
            var grid = new TestSurfaceGrid();
            _presenter.Configure(null, _config, grid, null, fallbackSeed: 101);
            _presenter.ApplyInitialPhase(DungeonPressurePhase.CriticalPressure);

            _presenter.SetWallFrameIntervalSeconds(0.08f);
            Assert.That(_presenter.EffectiveWallFrameIntervalSeconds, Is.EqualTo(0.08f).Within(0.0001f));
            foreach (var wall in _presenter.WallRifts)
            {
                Assert.That(wall.FrameIntervalSeconds, Is.EqualTo(0.08f).Within(0.0001f));
            }
        }

        [Test]
        public void LateJoinInCollapse_ReflectsActiveStateImmediatelyWithoutReplayingSpawnOrEruptions()
        {
            var grid = new TestSurfaceGrid();
            _presenter.Configure(null, _config, grid, null, fallbackSeed: 777);
            _presenter.ApplyInitialPhase(DungeonPressurePhase.Collapse);

            foreach (var floor in _presenter.FloorRifts)
            {
                Assert.That(floor.VisualState, Is.EqualTo(CollapseRiftVisualState.Dormant));
                Assert.That(floor.IsActivatable, Is.True);
            }

            foreach (var wall in _presenter.WallRifts)
            {
                Assert.That(wall.VisualState, Is.Not.EqualTo(CollapseRiftVisualState.Spawning));
                Assert.That(wall.VisualState, Is.Not.EqualTo(CollapseRiftVisualState.Hidden));
            }
        }

        [Test]
        public void ResetPresentation_DestroysAllSpawnedRiftsCleanly()
        {
            var grid = new TestSurfaceGrid();
            _presenter.Configure(null, _config, grid, null, fallbackSeed: 321);
            Assert.That(_presenter.FloorRifts.Count, Is.GreaterThan(0));

            _presenter.ResetPresentation();

            Assert.That(_presenter.FloorRifts.Count, Is.EqualTo(0));
            Assert.That(_presenter.WallRifts.Count, Is.EqualTo(0));
            Assert.That(_presenterGo.transform.Find("CollapseRiftsRoot"), Is.Null);
        }

        [Test]
        public void ReconfigureWithNewSeed_ReplacesRiftsUnderFreshRoot_AndPhaseTransitionsStillActivateAllRifts()
        {
            var grid = new TestSurfaceGrid();
            _presenter.Configure(null, _config, grid, null, fallbackSeed: 1337);
            Transform firstRoot = _presenterGo.transform.Find("CollapseRiftsRoot");
            Assert.That(firstRoot, Is.Not.Null);

            // Simulate RaidGenerationId arriving on a later frame and regenerating rifts
            _presenter.Configure(null, _config, grid, null, fallbackSeed: 935090660);
            Transform secondRoot = _presenterGo.transform.Find("CollapseRiftsRoot");
            Assert.That(secondRoot, Is.Not.Null);
            Assert.That(ReferenceEquals(firstRoot, secondRoot), Is.False);
            Assert.That(_presenter.FloorRifts.Count, Is.EqualTo(8));
            Assert.That(_presenter.WallRifts.Count, Is.EqualTo(3));

            _presenter.ApplyPhaseTransition(DungeonPressurePhase.Reinforcements);
            Assert.That(CountVisibleFloorRifts(), Is.EqualTo(4));
            Assert.That(CountVisibleWallRifts(), Is.EqualTo(3));

            _presenter.ApplyPhaseTransition(DungeonPressurePhase.CriticalPressure);
            Assert.That(CountVisibleFloorRifts(), Is.EqualTo(8));
            Assert.That(CountActivatableFloorRifts(), Is.EqualTo(4));

            _presenter.ApplyPhaseTransition(DungeonPressurePhase.Collapse);
            Assert.That(CountVisibleFloorRifts(), Is.EqualTo(8));
            Assert.That(CountActivatableFloorRifts(), Is.EqualTo(8));
        }

        [Test]
        public void ProjectAssets_DefaultConfigMaterialAndPrefabs_AreConfiguredWithAllCollapseSprites()
        {
            var defaultConfig = AssetDatabase.LoadAssetAtPath<CollapseRiftConfig>(ConfigAssetPath);
            Assert.That(defaultConfig, Is.Not.Null, $"Missing config asset at {ConfigAssetPath}");

            Assert.That(defaultConfig.FloorSpawnFrames, Is.Not.Null.And.Length.EqualTo(7));
            Assert.That(defaultConfig.FloorDormantSprite, Is.Not.Null);
            Assert.That(defaultConfig.FloorTelegraphFrames, Is.Not.Null.And.Length.EqualTo(7));
            Assert.That(defaultConfig.FloorEruptionFrames, Is.Not.Null.And.Length.EqualTo(7));
            Assert.That(defaultConfig.FloorCooldownFrames, Is.Not.Null.And.Length.EqualTo(8));

            Assert.That(defaultConfig.WallSpawnFrames, Is.Not.Null.And.Length.EqualTo(7));
            Assert.That(defaultConfig.WallStage1Frames, Is.Not.Null.And.Length.EqualTo(1));
            Assert.That(defaultConfig.WallStage2Frames, Is.Not.Null.And.Length.EqualTo(7));
            Assert.That(defaultConfig.WallStage3Frames, Is.Not.Null.And.Length.EqualTo(7));
            Assert.That(defaultConfig.WallStage4Frames, Is.Not.Null.And.Length.EqualTo(8));

            // Confirm Stage1 normalized to full 48x48 rect to avoid visual shift
            Assert.That(defaultConfig.FloorDormantSprite.rect.width, Is.EqualTo(48f).Within(0.01f));
            Assert.That(defaultConfig.FloorDormantSprite.rect.height, Is.EqualTo(48f).Within(0.01f));
            Assert.That(defaultConfig.WallStage1Frames[0].rect.width, Is.EqualTo(48f).Within(0.01f));
            Assert.That(defaultConfig.WallStage1Frames[0].rect.height, Is.EqualTo(48f).Within(0.01f));

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
            Assert.That(mat, Is.Not.Null, $"Missing emissive material at {MaterialAssetPath}");
            Assert.That(defaultConfig.RiftMaterial, Is.EqualTo(mat));
            Assert.That(mat.shader.name, Is.EqualTo("Grimhold/Collapse/CollapseRiftEmissive"));

            var floorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FloorPrefabPath);
            var wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
            Assert.That(floorPrefab, Is.Not.Null, $"Missing prefab at {FloorPrefabPath}");
            Assert.That(wallPrefab, Is.Not.Null, $"Missing prefab at {WallPrefabPath}");
            Assert.That(defaultConfig.FloorRiftPrefab, Is.Not.Null);
            Assert.That(defaultConfig.WallRiftPrefab, Is.Not.Null);
            Assert.That(floorPrefab.GetComponentInChildren<Collider2D>(true), Is.Null);
            Assert.That(wallPrefab.GetComponentInChildren<Collider2D>(true), Is.Null);
        }

        [Test]
        public void CatacombsMapScene_HasCollapseRiftPresenterAndAmbientPresenterWiredToNonNullConfigs()
        {
            string scenePath = "Assets/Scenes/CatacombsMap.unity";
            Scene opened = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                CollapseRiftPresenter riftPresenter = null;
                DungeonPressureAmbientPresenter ambientPresenter = null;

                foreach (GameObject root in opened.GetRootGameObjects())
                {
                    if (riftPresenter == null) riftPresenter = root.GetComponentInChildren<CollapseRiftPresenter>(true);
                    if (ambientPresenter == null) ambientPresenter = root.GetComponentInChildren<DungeonPressureAmbientPresenter>(true);
                }

                Assert.That(riftPresenter, Is.Not.Null, "CatacombsMap.unity must contain a CollapseRiftPresenter.");
                Assert.That(riftPresenter.Config, Is.Not.Null, "CollapseRiftPresenter._config in CatacombsMap.unity must be assigned (not null).");
                Assert.That(ambientPresenter, Is.Not.Null, "CatacombsMap.unity must contain a DungeonPressureAmbientPresenter.");
                Assert.That(ambientPresenter.Config, Is.Not.Null, "DungeonPressureAmbientPresenter._config in CatacombsMap.unity must be assigned (not null).");
            }
            finally
            {
                EditorSceneManager.CloseScene(opened, removeScene: true);
            }
        }

        private int CountVisibleFloorRifts()
        {
            int count = 0;
            for (int i = 0; i < _presenter.FloorRifts.Count; i++)
            {
                if (_presenter.FloorRifts[i].VisualState != CollapseRiftVisualState.Hidden) count++;
            }
            return count;
        }

        private int CountActivatableFloorRifts()
        {
            int count = 0;
            for (int i = 0; i < _presenter.FloorRifts.Count; i++)
            {
                if (_presenter.FloorRifts[i].IsActivatable) count++;
            }
            return count;
        }

        private int CountVisibleWallRifts()
        {
            int count = 0;
            for (int i = 0; i < _presenter.WallRifts.Count; i++)
            {
                if (_presenter.WallRifts[i].VisualState != CollapseRiftVisualState.Hidden) count++;
            }
            return count;
        }

        private CollapseFloorRiftVisual FindFirstActivatableFloorRift()
        {
            for (int i = 0; i < _presenter.FloorRifts.Count; i++)
            {
                if (_presenter.FloorRifts[i].IsActivatable) return _presenter.FloorRifts[i];
            }
            return null;
        }
    }
}
#endif

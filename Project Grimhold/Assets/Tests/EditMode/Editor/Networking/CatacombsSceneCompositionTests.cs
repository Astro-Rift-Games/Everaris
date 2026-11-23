using System.Collections.Generic;
using System.Linq;
using Fusion;
using NUnit.Framework;
using ProjectGrimhold.Gameplay.Visibility;
using Spawning;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Assert = NUnit.Framework.Assert;

namespace Tests.EditMode.Networking
{
    public sealed class CatacombsSceneCompositionTests
    {
        private const string ScenePath = "Assets/Scenes/CatacombsMap.unity";
        private const string SanctuaryPath = "Assets/Prefabs/ExtractionSanctuary.prefab";
        private Scene _scene;
        private Scene _reference;

        [SetUp]
        public void OpenSavedScenesReadOnly()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Assert.That(SceneManager.GetSceneAt(i).isDirty, Is.False,
                    "Composition tests require clean open scenes.");
            _scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            _reference = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Gameplay.unity");
        }

        [TearDown]
        public void CloseWithoutSaving()
        {
            if (_reference.IsValid()) EditorSceneManager.ClosePreviewScene(_reference);
            if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
        }

        [Test]
        public void RequiredConfiguration_HasEightOrderedSanctuaryAreasAndUniquePositions()
        {
            NetworkSpawnSceneConfiguration configuration = One<NetworkSpawnSceneConfiguration>(_scene);
            Assert.That(configuration.SpawnPointPolicy, Is.EqualTo(SceneSpawnPointPolicy.Required));
            Assert.That(configuration.Validate(out string failure), Is.True, failure);
            Assert.That(configuration.PlayerSpawnAreas.Count, Is.EqualTo(8));
            var points = new HashSet<Transform>();
            var positions = new HashSet<Vector3>();
            for (int area = 0; area < 8; area++)
            {
                Transform sanctuary = Named("ExtractionSanctuary_" + (area + 1));
                Assert.That(configuration.PlayerSpawnAreas[area].SpawnPoints.Count, Is.EqualTo(4));
                for (int point = 0; point < 4; point++)
                {
                    Transform spawn = configuration.PlayerSpawnAreas[area].SpawnPoints[point];
                    Assert.That(spawn, Is.SameAs(sanctuary.Find("Spawnpoint " + (point + 1))));
                    Assert.That(points.Add(spawn), Is.True);
                    Assert.That(positions.Add(spawn.position), Is.True);
                }
            }
            Assert.That(points.Count, Is.EqualTo(32).And.GreaterThanOrEqualTo(RaidSessionRules.MaxParticipants));
        }

        [Test]
        public void Sanctuaries_AreUnmodifiedPrefabInstancesAtTheEightAuthoredPoints()
        {
            Assert.That(All<ExtractionSanctuary>(_scene), Has.Length.EqualTo(8));
            Assert.That(All<ExtractionZone>(_scene), Has.Length.EqualTo(8));
            // Fusion's scene baker authors SortKey for deterministic scene-object ordering.
            NetworkObject[] networkObjects = All<ExtractionSanctuary>(_scene)
                .Select(item => item.GetComponent<NetworkObject>()).ToArray();
            Assert.That(networkObjects.Select(item => item.SortKey).Distinct().Count(), Is.EqualTo(8));
            Assert.That(networkObjects.All(item => item.SortKey != 0), Is.True);
            for (int i = 1; i <= 8; i++)
            {
                Transform sanctuary = Named("ExtractionSanctuary_" + i);
                Transform marker = Named("MapMarkers").Find("PlayerSanctuaryPoints/Point_" + i);
                Assert.That(Vector3.Distance(sanctuary.position, marker.position), Is.LessThanOrEqualTo(0.01f));
                Assert.That(sanctuary.GetComponent<NetworkObject>(), Is.Not.Null);
                Assert.That(sanctuary.GetComponent<ExtractionZone>(), Is.Not.Null);
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sanctuary.gameObject),
                    Is.EqualTo(SanctuaryPath));
                Assert.That(PrefabUtility.GetAddedComponents(sanctuary.gameObject), Is.Empty);
                Assert.That(PrefabUtility.GetRemovedComponents(sanctuary.gameObject), Is.Empty);
                Assert.That(PrefabUtility.GetAddedGameObjects(sanctuary.gameObject), Is.Empty);
                foreach (PropertyModification modification in PrefabUtility.GetPropertyModifications(sanctuary.gameObject)
                    ?? System.Array.Empty<PropertyModification>())
                {
                    Assert.That(modification.target is Transform || modification.propertyPath == "m_Name" ||
                        (modification.target is NetworkObject && modification.propertyPath == "SortKey"),
                        Is.True, "Unexpected sanctuary override: " + modification.propertyPath);
                }
            }
        }

        [Test]
        public void WorldGroups_UseScaledAmountsAndOrderedMarkersAndPreserveOtherGameplaySettings()
        {
            NetworkSpawnSceneConfiguration configuration = One<NetworkSpawnSceneConfiguration>(_scene);
            NetworkSpawnSceneConfiguration reference = One<NetworkSpawnSceneConfiguration>(_reference);
            Assert.That(configuration.SpawnGroups.Select(group => (int)group.Group), Is.EquivalentTo(new[] { 1, 2, 6, 7 }));
            string[] markerGroups = { "EnemySpawnMarkers", "LootSpawnMarkers", "BreakableSpawnMarkers", "ReinforcementSpawnMarkers" };
            int[] types = { 1, 2, 6, 7 };
            int[] counts = { 64, 96, 96, 64 };
            string[] prefixes = { "Enemy_", "Loot_", "Breakable_", "Reinforcement_" };
            for (int i = 0; i < types.Length; i++)
            {
                SpawnGroupDefinition group = configuration.SpawnGroups.Single(item => (int)item.Group == types[i]);
                SpawnGroupDefinition original = reference.SpawnGroups.Single(item => item.Group == group.Group);
                Transform markers = Named("MapMarkers").Find(markerGroups[i]);
                Assert.That(group.SpawnPoints, Has.Length.EqualTo(counts[i]));
                Assert.That(markers.childCount, Is.EqualTo(counts[i]));
                Transform[] orderedMarkers = Enumerable.Range(1, counts[i])
                    .Select(number => markers.Find(prefixes[i] + number)).ToArray();
                Assert.That(orderedMarkers, Has.None.Null);
                Assert.That(group.SpawnPoints, Is.EqualTo(orderedMarkers));
                Assert.That(group.SpawnPoints.Distinct().Count(), Is.EqualTo(counts[i]));
                Assert.That(group.Amount, Is.EqualTo(64));
                Assert.That(group.ThreatBudget, Is.EqualTo(original.ThreatBudget));
            }
        }

        [Test]
        public void SpawnManager_PreservesGameplayPrefabCatalogAndConfigurationReferences()
        {
            NetworkSpawnManager manager = One<NetworkSpawnManager>(_scene);
            NetworkSpawnManager original = One<NetworkSpawnManager>(_reference);
            Assert.That(EditorJsonUtility.ToJson(manager), Is.EqualTo(EditorJsonUtility.ToJson(original)));
            Assert.That(manager.GetComponent<NetworkSpawnSceneConfiguration>(),
                Is.SameAs(One<NetworkSpawnSceneConfiguration>(_scene)));
        }

        [Test]
        public void SceneSystems_AreUniqueAndExplicitlyWiredLocally()
        {
            PathfindingGrid grid = One<PathfindingGrid>(_scene);
            Assert.That(Reference(grid, "_config"), Is.SameAs(Reference(One<PathfindingGrid>(_reference), "_config")));
            Camera camera = One<Camera>(_scene);
            Assert.That(camera.GetComponent<LocalCameraController>(), Is.Not.Null);
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(camera),
                Is.EqualTo(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(One<Camera>(_reference))));
            Assert.That(camera.CompareTag("MainCamera"), Is.True);
            EntityVisibilitySystem visibility = One<EntityVisibilitySystem>(_scene);
            VisionFogPresenter fog = One<VisionFogPresenter>(_scene);
            Assert.That(Reference(fog, "_visibilitySystem"), Is.SameAs(visibility));
            Assert.That(Reference(fog, "_camera"), Is.SameAs(camera));
            Assert.That(Reference(fog, "_renderer"), Is.SameAs(fog.GetComponent<MeshRenderer>()));
            foreach (string field in new[] { "_settings", "_maskMaterial" })
                Assert.That(Reference(fog, field), Is.SameAs(Reference(One<VisionFogPresenter>(_reference), field)));
            Light2D[] lights = All<Light2D>(_scene);
            Assert.That(lights.Where(item => item.GetComponentInParent<DungeonLightProp>(true) == null).ToArray(), Has.Length.EqualTo(1),
                "Besides painted light props (torches, braziers, candles) the scene owns only its global light.");
            Light2D light = lights.Single(item => item.GetComponentInParent<DungeonLightProp>(true) == null);
            Assert.That(light.lightType, Is.EqualTo(Light2D.LightType.Global));
            Assert.That(light.intensity, Is.EqualTo(All<Light2D>(_reference)
                .Single(item => item.lightType == Light2D.LightType.Global).intensity));
            One<EventSystem>(_scene);
            PlayerInputReader reader = One<PlayerInputReader>(_scene);
            Assert.That(Reference(reader, "_worldCamera"), Is.SameAs(camera));
            Assert.That(Reference(One<FusionInputProvider>(_scene), "_inputReader"), Is.SameAs(reader));
        }

        [Test]
        public void WallsAndObstacles_HaveWorldCollisionWithoutChangingTileArt()
        {
            Tilemap[] tilemaps = All<Tilemap>(_scene);
            foreach (string name in new[] { "Walls", "Obstacles" })
            {
                Tilemap tilemap = tilemaps.Single(item => item.name == name);
                Assert.That(tilemap.GetComponent<TilemapCollider2D>(), Is.Not.Null);
                Assert.That(tilemap.gameObject.activeInHierarchy, Is.True);
                Assert.That(tilemap.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("WorldCollision")));
            }
            foreach (TilemapCollider2D collider in All<TilemapCollider2D>(_scene))
                Assert.That(collider.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("WorldCollision")));
        }

        [Test]
        public void MinimalRaid_ContainsNoDeveloperSpawnersPickupsOrExcludedSystems()
        {
            Assert.That(All<RaidLootTestSpawner>(_scene), Is.Empty);
            Assert.That(All<MissionPlayModeDebugger>(_scene), Is.Empty);
            Assert.That(All<NetworkLootPickup>(_scene), Is.Empty);
            Assert.That(All<NetworkDoor>(_scene), Is.Empty);
            Assert.That(All<EnemyPatrolRoute>(_scene), Is.Empty);
            Assert.That(All<ParticleSystem>(_scene).Where(item => item.GetComponentInParent<DungeonLightProp>(true) == null),
                Is.Empty, "Only light props may carry particles (their extinguish smoke).");
            Assert.That(All<Transform>(_scene).Any(item => item.name.StartsWith("LootPickup_") ||
                item.name.StartsWith("TestPickup_") || item.name == "[LootTestSpawner]" || item.name == "[MissionDebugger]"), Is.False);
        }

        [Test]
        public void RaidScene_PlaysTheRaidMusicTrack()
        {
            SceneMusicPresenter[] presenters = All<SceneMusicPresenter>(_scene);
            Assert.That(presenters, Has.Length.EqualTo(1), "The Raid scene owns exactly one scene music track.");
            Assert.That(new SerializedObject(presenters[0]).FindProperty("_trackKey").stringValue, Is.EqualTo(AudioKeys.Raid));
        }

        [Test]
        public void SceneName_ResolvesCatacombsEnvironment()
        {
            Assert.That(DungeonEnvironmentResolver.ResolveEnvironment(_scene.name), Is.EqualTo("catacombs"));
        }

        private Transform Named(string name) => All<Transform>(_scene).Single(item => item.name == name);

        private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        private static T One<T>(Scene scene) where T : Component
        {
            T[] components = All<T>(scene);
            Assert.That(components, Has.Length.EqualTo(1), typeof(T).Name);
            return components[0];
        }

        private static Object Reference(Object component, string field) =>
            new SerializedObject(component).FindProperty(field).objectReferenceValue;
    }
}

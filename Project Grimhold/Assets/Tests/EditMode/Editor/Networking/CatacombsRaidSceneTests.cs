using System.IO;
using Fusion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Assert = NUnit.Framework.Assert;

namespace Tests.EditMode.Networking
{
    public sealed class CatacombsRaidSceneTests
    {
        private const string RaidPath = "Assets/Scenes/CatacombsMap.unity";

        [Test]
        public void LaunchComposition_SelectsCatacombsMap()
        {
            GameObject systems = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Systems.prefab");
            var coordinator = systems.GetComponentInChildren<SessionConnectionCoordinator>(true);
            Assert.That(coordinator, Is.Not.Null);
            Assert.That(new SerializedObject(coordinator).FindProperty("_gameplaySceneName").stringValue,
                Is.EqualTo("CatacombsMap"));
        }

        [Test]
        public void NewCoordinator_DefaultAgreesWithLaunchComposition()
        {
            var owner = new GameObject("Coordinator default test");
            owner.SetActive(false);
            try
            {
                var coordinator = owner.AddComponent<SessionConnectionCoordinator>();
                Assert.That(new SerializedObject(coordinator).FindProperty("_gameplaySceneName").stringValue,
                    Is.EqualTo("CatacombsMap"));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void RaidScene_IsEnabledAndResolvesByNameAndPath()
        {
            int index = NetworkSceneBuildIndexResolver.Resolve("CatacombsMap");
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            Assert.That(SceneUtility.GetScenePathByBuildIndex(index), Is.EqualTo(RaidPath));
            Assert.That(NetworkSceneBuildIndexResolver.Resolve(RaidPath), Is.EqualTo(index));
        }

        [Test]
        public void HostMigration_PreservesCatacombsBuildIndexInReplacementSceneInfo()
        {
            int sourceIndex = NetworkSceneBuildIndexResolver.Resolve("CatacombsMap");
            Assert.That(sourceIndex, Is.GreaterThanOrEqualTo(0));
            var replacement = new NetworkSceneInfo();
            Assert.That(replacement.AddSceneRef(SceneRef.FromIndex(sourceIndex), LoadSceneMode.Single),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(replacement.Scenes[0].AsIndex, Is.EqualTo(sourceIndex));
            Assert.That(SceneUtility.GetScenePathByBuildIndex(replacement.Scenes[0].AsIndex),
                Is.EqualTo(RaidPath));
            // Structural guard links the scene-info roundtrip above to the actual migration path.
            // This is not evidence of a live multi-process migration.
            string migration = File.ReadAllText("Assets/Scripts/Networking/HostMigrationLifecycleController.cs");
            Assert.That(migration, Does.Contain("int oldSceneBuildIndex = oldScene.buildIndex;"));
            Assert.That(migration, Does.Contain("SceneRef.FromIndex(oldSceneBuildIndex)"));
        }

        [Test]
        public void LegacyGameplayAndTownResolution_RemainsAvailableAtExistingIndices()
        {
            Assert.That(NetworkSceneBuildIndexResolver.Resolve("Gameplay"), Is.EqualTo(1));
            Assert.That(NetworkSceneBuildIndexResolver.Resolve("Assets/Scenes/Gameplay.unity"), Is.EqualTo(1));
            Assert.That(NetworkSceneBuildIndexResolver.Resolve("Lobby-Town"), Is.EqualTo(2));
            Assert.That(NetworkSceneBuildIndexResolver.Resolve("MainMenu"), Is.EqualTo(0));
            Assert.That(NetworkSceneBuildIndexResolver.Resolve("missing-scene"), Is.EqualTo(-1));
        }
    }
}

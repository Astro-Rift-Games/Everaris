#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using Spawning;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using NetworkObject = Fusion.NetworkObject;

namespace Tests.EditMode.Loot
{
    /// <summary>
    /// The jar is a visual variant of the crate breakable: both share the Raid's breakable points, the jar
    /// chosen from each point's seed, with the same drop rules.
    /// </summary>
    public sealed class JarBreakableTests
    {
        private const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";
        private const string CatacombsScenePath = "Assets/Scenes/CatacombsMap.unity";
        private const string CratePath = "Assets/Prefabs/BreakableObject.prefab";
        private const string JarPath = "Assets/Prefabs/JarBreakable.prefab";

        [Test]
        public void Rule_IsOffAtZeroAndCertainAtAHundred()
        {
            for (ulong seed = 0; seed < 64; seed++)
            {
                Assert.That(JarBreakableSpawnRules.ShouldSpawnJar(seed, 0), Is.False);
                Assert.That(JarBreakableSpawnRules.ShouldSpawnJar(seed, -5), Is.False);
                Assert.That(JarBreakableSpawnRules.ShouldSpawnJar(seed, 100), Is.True);
                Assert.That(JarBreakableSpawnRules.ShouldSpawnJar(seed, 250), Is.True);
            }
        }

        [Test]
        public void Rule_AtFiftyPercent_TurnsAboutHalfOfTheBreakablesIntoJars()
        {
            const int samples = 20_000;
            int jars = 0;
            for (int index = 0; index < samples; index++)
            {
                ulong seed = LootContainerSeedRules.Derive(987_654_321UL, 1, (int)SpawnGroupType.Breakables, index);
                if (JarBreakableSpawnRules.ShouldSpawnJar(seed, 50))
                {
                    jars++;
                }
            }

            Assert.That(jars, Is.InRange(samples * 47 / 100, samples * 53 / 100));
        }

        [Test]
        public void Rule_DoesNotFollowTheCoffinRoll()
        {
            int differences = 0;
            for (int index = 0; index < 2_000; index++)
            {
                ulong seed = LootContainerSeedRules.Derive(42UL, 1, (int)SpawnGroupType.Breakables, index);
                if (JarBreakableSpawnRules.ShouldSpawnJar(seed, 50) !=
                    CoffinContainerSpawnRules.ShouldSpawnCoffin(seed, 50))
                {
                    differences++;
                }
            }

            // Independent rolls disagree about half of the time; a shared roll would never disagree.
            Assert.That(differences, Is.InRange(800, 1_200));
        }

        [Test]
        public void JarPrefab_IsItsOwnNetworkPrefab()
        {
            GameObject jar = Load(JarPath);

            Assert.That(jar.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(jar.GetComponent<BreakableObject>(), Is.Not.Null);
            Assert.That(AssetDatabase.GetLabels(jar), Does.Contain("FusionPrefab"));
            Assert.That(AssetDatabase.AssetPathToGUID(JarPath), Is.Not.EqualTo(AssetDatabase.AssetPathToGUID(CratePath)));
        }

        [TestCase(GameplayScenePath)]
        [TestCase(CatacombsScenePath)]
        public void RaidScene_SpawnsHalfOfItsBreakablesAsJars(string scenePath)
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(scenePath);
            try
            {
                NetworkSpawnManager manager = FindManager(scene);
                Assert.That(manager, Is.Not.Null, $"{scenePath} spawn manager");

                Assert.That(manager.BreakablePrefab.IsValid, Is.True);
                Assert.That(manager.JarBreakablePrefab.IsValid, Is.True);
                Assert.That(manager.JarBreakablePrefab, Is.Not.EqualTo(manager.BreakablePrefab));
                Assert.That(manager.JarBreakableChancePercent, Is.EqualTo(50));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void CopyReferencesFrom_CopiesTheJarVariantAndItsChance()
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(GameplayScenePath);
            var targetObject = new GameObject("Persistent jar loot manager");
            NetworkSpawnManager target = targetObject.AddComponent<NetworkSpawnManager>();
            try
            {
                NetworkSpawnManager source = FindManager(scene);
                SetPrivateField(source, "_jarBreakablePrefab", source.LootContainerPrefab);
                SetPrivateField(source, "_jarBreakableChancePercent", 35);

                Assert.That(target.CopyReferencesFrom(source), Is.True);
                Assert.That(target.JarBreakablePrefab, Is.EqualTo(source.LootContainerPrefab));
                Assert.That(target.JarBreakableChancePercent, Is.EqualTo(35));
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [TestCase(0, true)]
        [TestCase(50, false)]
        public void MissingJarVariant_IsAllowedOnlyWhenDisabled(int chancePercent, bool expected)
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(GameplayScenePath);
            var targetObject = new GameObject("Persistent jar loot manager");
            NetworkSpawnManager target = targetObject.AddComponent<NetworkSpawnManager>();
            try
            {
                NetworkSpawnManager source = FindManager(scene);
                SetPrivateField(source, "_jarBreakablePrefab", default(Fusion.NetworkPrefabRef));
                SetPrivateField(source, "_jarBreakableChancePercent", chancePercent);
                if (!expected)
                {
                    LogAssert.Expect(LogType.Error,
                        $"[NetworkSpawnManager] Scene manager '{source.name}' enables jar breakables but has no valid jar prefab. Breakable groups will be skipped.");
                }

                Assert.That(target.CopyReferencesFrom(source), Is.EqualTo(expected));
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void JarVariant_CannotBypassInitialContentViaExplicitSpawn()
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(GameplayScenePath);
            var targetObject = new GameObject("Persistent jar loot manager");
            NetworkSpawnManager target = targetObject.AddComponent<NetworkSpawnManager>();
            try
            {
                NetworkSpawnManager source = FindManager(scene);
                SetPrivateField(target, "_jarBreakablePrefab", source.JarBreakablePrefab);
                LogAssert.Expect(LogType.Error,
                    "[NetworkSpawnManager] Randomized scene entities cannot bypass their dedicated initial-content pipeline.");

                Assert.That(target.Spawn(source.JarBreakablePrefab, Vector3.zero, Quaternion.identity), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static NetworkSpawnManager FindManager(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (NetworkSpawnManager manager in root.GetComponentsInChildren<NetworkSpawnManager>(true))
                {
                    if (manager.LootContainerPrefab.IsValid)
                    {
                        return manager;
                    }
                }
            }

            return null;
        }

        private static void SetPrivateField(NetworkSpawnManager manager, string name, object value)
        {
            FieldInfo field = typeof(NetworkSpawnManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(manager, value);
        }

        private static GameObject Load(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            return prefab;
        }
    }
}
#endif

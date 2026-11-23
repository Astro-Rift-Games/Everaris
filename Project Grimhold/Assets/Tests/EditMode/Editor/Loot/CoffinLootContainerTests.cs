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
    /// The coffin is a visual variant of the standard loot container: half of the standard container points
    /// spawn as coffins, chosen from the container seed, with the same content rules.
    /// </summary>
    public sealed class CoffinLootContainerTests
    {
        private const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";
        private const string CatacombsScenePath = "Assets/Scenes/CatacombsMap.unity";
        private const string LootContainerPath = "Assets/Prefabs/LootContainer.prefab";
        private const string CoffinPath = "Assets/Prefabs/CoffinLootContainer.prefab";
        private const string RaidPlayerPath = "Assets/Prefabs/NetworkPlayer.prefab";

        [Test]
        public void Rule_IsOffAtZeroAndCertainAtAHundred()
        {
            for (ulong seed = 0; seed < 64; seed++)
            {
                Assert.That(CoffinContainerSpawnRules.ShouldSpawnCoffin(seed, 0), Is.False);
                Assert.That(CoffinContainerSpawnRules.ShouldSpawnCoffin(seed, -5), Is.False);
                Assert.That(CoffinContainerSpawnRules.ShouldSpawnCoffin(seed, 100), Is.True);
                Assert.That(CoffinContainerSpawnRules.ShouldSpawnCoffin(seed, 250), Is.True);
            }
        }

        [Test]
        public void Rule_IsStableForTheSameSeed()
        {
            for (ulong seed = 1; seed < 200; seed++)
            {
                Assert.That(
                    CoffinContainerSpawnRules.ShouldSpawnCoffin(seed * 7919UL, 50),
                    Is.EqualTo(CoffinContainerSpawnRules.ShouldSpawnCoffin(seed * 7919UL, 50)));
            }
        }

        [Test]
        public void Rule_AtFiftyPercent_TurnsAboutHalfOfTheContainersIntoCoffins()
        {
            const int samples = 20_000;
            int coffins = 0;
            for (int index = 0; index < samples; index++)
            {
                ulong seed = LootContainerSeedRules.Derive(987_654_321UL, 1, (int)SpawnGroupType.Loot, index);
                if (CoffinContainerSpawnRules.ShouldSpawnCoffin(seed, 50))
                {
                    coffins++;
                }
            }

            Assert.That(coffins, Is.InRange(samples * 47 / 100, samples * 53 / 100));
        }

        [Test]
        public void Rule_DoesNotFollowTheLockedChestRoll()
        {
            int differences = 0;
            for (int index = 0; index < 2_000; index++)
            {
                ulong seed = LootContainerSeedRules.Derive(42UL, 1, (int)SpawnGroupType.Loot, index);
                if (CoffinContainerSpawnRules.ShouldSpawnCoffin(seed, 50) !=
                    LockedChestSpawnRules.ShouldSpawnLocked(seed, 50))
                {
                    differences++;
                }
            }

            // Independent rolls disagree about half of the time; a shared roll would never disagree.
            Assert.That(differences, Is.InRange(800, 1_200));
        }

        [Test]
        public void CoffinPrefab_IsAVariantOfTheStandardContainerWithItsOwnLook()
        {
            GameObject standard = Load(LootContainerPath);
            GameObject coffin = Load(CoffinPath);

            Assert.That(PrefabUtility.GetPrefabAssetType(coffin), Is.EqualTo(PrefabAssetType.Variant));
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(coffin), Is.SameAs(standard));
            Assert.That(coffin.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(coffin.GetComponent<NetworkLootContainer>(), Is.Not.Null);

            SpriteRenderer renderer = coffin.GetComponentInChildren<SpriteRenderer>(true);
            Assert.That(renderer.sprite.texture.name, Is.EqualTo("Coffin-Close"));

            var presenter = new SerializedObject(coffin.GetComponent<ChestOpeningPresenter>());
            SerializedProperty frames = presenter.FindProperty("_openingFrames");
            Assert.That(frames.arraySize, Is.GreaterThanOrEqualTo(2));
            Assert.That(
                (frames.GetArrayElementAtIndex(0).objectReferenceValue as Sprite).texture.name,
                Is.EqualTo("Coffin-Close"));
            Assert.That(
                (frames.GetArrayElementAtIndex(frames.arraySize - 1).objectReferenceValue as Sprite).texture.name,
                Is.EqualTo("Coffin-Open"));
        }

        [Test]
        public void CoffinArt_ExcludesTheStrayPixelsOfItsSourceImage()
        {
            SpriteRenderer renderer = Load(CoffinPath).GetComponentInChildren<SpriteRenderer>(true);
            Rect rect = renderer.sprite.rect;

            // The source images carry a stray blob at x 2..5; the coffin itself starts at x 21.
            Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(16f));
            Assert.That(renderer.sprite.pixelsPerUnit, Is.EqualTo(16f));
        }

        [Test]
        public void CoffinPrefab_BlocksMovementAndIsInteractableWithinItsArtwork()
        {
            GameObject coffin = Load(CoffinPath);
            int avatarCollisionMask = new SerializedObject(Load(RaidPlayerPath).GetComponent<Kinematic2DMovementMotor>())
                .FindProperty("_collisionMask").FindPropertyRelative("m_Bits").intValue;
            SpriteRenderer renderer = coffin.GetComponentInChildren<SpriteRenderer>(true);
            Bounds art = renderer.sprite.bounds;
            Vector2 artCentre = (Vector2)renderer.transform.localPosition + (Vector2)art.center;
            Rect artwork = new(artCentre - ((Vector2)art.size * 0.5f), art.size);

            BoxCollider2D trigger = coffin.GetComponent<BoxCollider2D>();
            BoxCollider2D blocker = coffin.transform.Find("WorldBlocker").GetComponent<BoxCollider2D>();

            Assert.That(trigger.isTrigger, Is.True);
            Assert.That(blocker.isTrigger, Is.False);
            Assert.That(avatarCollisionMask & (1 << blocker.gameObject.layer), Is.Not.Zero);
            foreach (BoxCollider2D box in new[] { trigger, blocker })
            {
                Vector2 centre = (Vector2)box.transform.localPosition + box.offset;
                if (box.transform == coffin.transform)
                {
                    centre = box.offset;
                }

                Assert.That(centre.x - (box.size.x * 0.5f), Is.GreaterThanOrEqualTo(artwork.xMin - 0.07f), box.name);
                Assert.That(centre.x + (box.size.x * 0.5f), Is.LessThanOrEqualTo(artwork.xMax + 0.07f), box.name);
                Assert.That(centre.y - (box.size.y * 0.5f), Is.GreaterThanOrEqualTo(artwork.yMin - 0.07f), box.name);
                Assert.That(centre.y + (box.size.y * 0.5f), Is.LessThanOrEqualTo(artwork.yMax + 0.07f), box.name);
            }
        }

        [TestCase(GameplayScenePath)]
        [TestCase(CatacombsScenePath)]
        public void RaidScene_SpawnsHalfOfItsStandardContainersAsCoffins(string scenePath)
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(scenePath);
            try
            {
                NetworkSpawnManager manager = FindManager(scene);
                Assert.That(manager, Is.Not.Null, $"{scenePath} spawn manager");

                Assert.That(manager.CoffinContainerPrefab.IsValid, Is.True);
                Assert.That(manager.CoffinContainerPrefab, Is.Not.EqualTo(manager.LootContainerPrefab));
                Assert.That(manager.CoffinContainerChancePercent, Is.EqualTo(50));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void CopyReferencesFrom_CopiesTheCoffinVariantAndItsChance()
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(GameplayScenePath);
            var targetObject = new GameObject("Persistent coffin loot manager");
            NetworkSpawnManager target = targetObject.AddComponent<NetworkSpawnManager>();
            try
            {
                NetworkSpawnManager source = FindManager(scene);
                SetPrivateField(source, "_coffinContainerPrefab", source.LootContainerPrefab);
                SetPrivateField(source, "_coffinContainerChancePercent", 35);

                Assert.That(target.CopyReferencesFrom(source), Is.True);
                Assert.That(target.CoffinContainerPrefab, Is.EqualTo(source.LootContainerPrefab));
                Assert.That(target.CoffinContainerChancePercent, Is.EqualTo(35));
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [TestCase(0, true)]
        [TestCase(50, false)]
        public void MissingCoffinVariant_IsAllowedOnlyWhenDisabled(int chancePercent, bool expected)
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(GameplayScenePath);
            var targetObject = new GameObject("Persistent coffin loot manager");
            NetworkSpawnManager target = targetObject.AddComponent<NetworkSpawnManager>();
            try
            {
                NetworkSpawnManager source = FindManager(scene);
                SetPrivateField(source, "_coffinContainerPrefab", default(Fusion.NetworkPrefabRef));
                SetPrivateField(source, "_coffinContainerChancePercent", chancePercent);
                if (!expected)
                {
                    LogAssert.Expect(LogType.Error,
                        $"[NetworkSpawnManager] Scene manager '{source.name}' enables coffin containers but has no valid coffin prefab. Loot groups will be skipped.");
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
        public void CoffinVariant_CannotBypassInitialContentViaExplicitSpawn()
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(GameplayScenePath);
            var targetObject = new GameObject("Persistent coffin loot manager");
            NetworkSpawnManager target = targetObject.AddComponent<NetworkSpawnManager>();
            try
            {
                NetworkSpawnManager source = FindManager(scene);
                SetPrivateField(target, "_coffinContainerPrefab", source.LootContainerPrefab);
                LogAssert.Expect(LogType.Error,
                    "[NetworkSpawnManager] Randomized scene entities cannot bypass their dedicated initial-content pipeline.");

                Assert.That(target.Spawn(source.LootContainerPrefab, Vector3.zero, Quaternion.identity), Is.Null);
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

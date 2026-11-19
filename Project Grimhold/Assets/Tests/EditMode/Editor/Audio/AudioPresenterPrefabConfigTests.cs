#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Audio
{
    /// <summary>
    /// Every prefab that carries an audio presenter must reference an audio config; a null config
    /// silently disables all of that entity's sounds.
    /// </summary>
    public sealed class AudioPresenterPrefabConfigTests
    {
        private const string PrefabsRoot = "Assets/Prefabs";
        private const string ConfigProperty = "_audioConfig";

        [Test]
        public void EnemyAudioPresenterPrefabs_ReferenceAnAudioConfig()
        {
            AssertAllPresentersHaveConfig<EnemyAudioPresenter>();
        }

        [Test]
        public void PlayerAudioPresenterPrefabs_ReferenceAnAudioConfig()
        {
            AssertAllPresentersHaveConfig<PlayerAudioPresenter>();
        }

        [Test]
        public void SpecterAudioPresenterPrefabs_ReferenceAnAudioConfig()
        {
            AssertAllPresentersHaveConfig<SpecterAudioPresenter>();
        }

        private static void AssertAllPresentersHaveConfig<TPresenter>() where TPresenter : Component
        {
            int presenterCount = 0;
            var offenders = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;

                foreach (TPresenter presenter in root.GetComponentsInChildren<TPresenter>(true))
                {
                    presenterCount++;
                    var serialized = new SerializedObject(presenter);
                    SerializedProperty config = serialized.FindProperty(ConfigProperty);
                    Assert.That(config, Is.Not.Null,
                        $"{typeof(TPresenter).Name} on '{path}' has no serialized '{ConfigProperty}' field.");

                    if (config.objectReferenceValue == null)
                    {
                        offenders.Add(path);
                    }
                }
            }

            Assert.That(presenterCount, Is.GreaterThan(0),
                $"No prefab under '{PrefabsRoot}' carries {typeof(TPresenter).Name}; the check would be vacuous.");
            Assert.That(offenders, Is.Empty,
                $"{typeof(TPresenter).Name} without '{ConfigProperty}' on prefabs:\n  " + string.Join("\n  ", offenders));
        }
    }
}
#endif

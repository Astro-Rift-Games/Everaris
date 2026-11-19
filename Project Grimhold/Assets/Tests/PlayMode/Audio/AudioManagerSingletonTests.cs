#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Audio
{
    public sealed class AudioManagerSingletonTests
    {
        private const string AudioManagerPrefabPath = "Assets/Prefabs/Audio/AudioManager.prefab";

        private GameObject _first;
        private GameObject _second;

        [TearDown]
        public void Cleanup()
        {
            // Destroying the surviving manager resets AudioManager.Instance (see OnDestroy).
            if (_first != null) Object.DestroyImmediate(_first);
            if (_second != null) Object.DestroyImmediate(_second);
            Assert.That(AudioManager.Instance, Is.Null, "Cleanup must leave no published AudioManager.");
        }

        [UnityTest]
        public IEnumerator TwoAudioManagerPrefabs_KeepFirstAndDestroyDuplicate()
        {
            Assume.That(AudioManager.Instance, Is.Null, "Another AudioManager is already published.");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AudioManagerPrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Missing prefab at {AudioManagerPrefabPath}.");

            _first = Object.Instantiate(prefab);
            _second = Object.Instantiate(prefab);
            AudioManager firstManager = _first.GetComponent<AudioManager>();

            yield return null;

            Assert.That(AudioManager.Instance, Is.SameAs(firstManager), "The first instance must stay published.");
            Assert.That(_first == null, Is.False, "The first instance must survive.");
            Assert.That(_second == null, Is.True, "The duplicate must be destroyed.");
            Assert.That(Object.FindObjectsByType<AudioManager>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
        }
    }
}
#endif

#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.Audio
{
    /// <summary>
    /// Scene music belongs to the scene that requested it: the persistent AudioManager must not keep
    /// Town music through the loading screen into a Raid scene that has no music of its own.
    /// </summary>
    public sealed class SceneMusicPresenterTests
    {
        private const string AudioManagerPrefabPath = "Assets/Prefabs/Audio/AudioManager.prefab";

        private GameObject _manager;
        private GameObject _presenter;

        [TearDown]
        public void Cleanup()
        {
            if (_presenter != null) Object.DestroyImmediate(_presenter);
            if (_manager != null) Object.DestroyImmediate(_manager);
        }

        [UnityTest]
        public IEnumerator LeavingTheScene_StopsTheMusicItStarted()
        {
            AudioManager manager = CreateManager();
            AudioClip town = AudioTestKit.Clip("town");
            CreatePresenter(AudioTestKit.Config<MusicAudioConfig>((AudioKeys.Town, town)), AudioKeys.Town);
            yield return null;
            Assert.That(manager.CurrentMusicClip, Is.SameAs(town), "Precondition: the scene started its track.");

            Object.Destroy(_presenter);
            yield return null;

            Assert.That(manager.CurrentMusicClip, Is.Null, "Town music must stop when its scene unloads.");
        }

        [UnityTest]
        public IEnumerator LeavingTheScene_KeepsMusicThatSomethingElseStartedLater()
        {
            AudioManager manager = CreateManager();
            AudioClip town = AudioTestKit.Clip("town");
            AudioClip collapse = AudioTestKit.Clip("collapse");
            CreatePresenter(AudioTestKit.Config<MusicAudioConfig>((AudioKeys.Town, town)), AudioKeys.Town);
            yield return null;

            var other = AudioTestKit.Config<MusicAudioConfig>((AudioKeys.Raid, collapse));
            Assert.That(other.TryGetClip(AudioKeys.Raid, out CustomClip collapseTrack), Is.True);
            manager.PlayMusic(collapseTrack);
            Object.Destroy(_presenter);
            yield return null;

            Assert.That(manager.CurrentMusicClip, Is.SameAs(collapse), "A scene only stops its own track.");
        }

        private AudioManager CreateManager()
        {
            Assume.That(AudioManager.Instance, Is.Null, "Another AudioManager is already published.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AudioManagerPrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Missing prefab at {AudioManagerPrefabPath}.");
            _manager = Object.Instantiate(prefab);
            return _manager.GetComponent<AudioManager>();
        }

        private void CreatePresenter(MusicAudioConfig config, string trackKey)
        {
            _presenter = new GameObject("SceneMusic");
            _presenter.SetActive(false);
            var presenter = _presenter.AddComponent<SceneMusicPresenter>();
            AudioTestKit.SetReference(presenter, "_musicConfig", config);
            AudioTestKit.SetString(presenter, "_trackKey", trackKey);
            _presenter.SetActive(true);
        }
    }
}
#endif

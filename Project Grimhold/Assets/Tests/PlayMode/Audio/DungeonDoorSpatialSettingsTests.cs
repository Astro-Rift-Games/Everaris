#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Assert = NUnit.Framework.Assert;

namespace Tests.PlayMode.Audio
{
    /// <summary>
    /// The door owns its AudioSource but must follow the central 3D distance policy of AudioManager. The real door
    /// prefab is spawned through a real runner, so <see cref="DungeonDoorPresenter.Spawned"/> runs with the setup it
    /// requires (its NetworkDoor, Animator and clips) instead of being called on a detached instance.
    /// </summary>
    public sealed class DungeonDoorSpatialSettingsTests
    {
        private const string DoorPrefabGuid = "e6dd992b08015f64db6dc289448875ce";

        private readonly AudioRunnerFixture _fixture = new();
        private GameObject _managerObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return _fixture.Shutdown();
            if (_managerObject != null) Object.DestroyImmediate(_managerObject);
        }

        private IEnumerator Begin(bool withAudioManager)
        {
            Assume.That(AudioManager.Instance, Is.Null, "Another AudioManager is already published.");
            if (withAudioManager)
            {
                _managerObject = new GameObject("Audio test manager");
                _managerObject.AddComponent<AudioManager>();
            }

            yield return _fixture.Start("DungeonDoorSpatialRunner");
        }

        private AudioSource SpawnDoorAndGetSource()
        {
            NetworkObject door = _fixture.Runner.Spawn(_fixture.Load(DoorPrefabGuid), Vector3.zero, Quaternion.identity);
            Assert.That(door, Is.Not.Null);
            Assert.That(door.GetComponent<DungeonDoorPresenter>(), Is.Not.Null);
            AudioSource source = door.GetComponentInChildren<AudioSource>(true);
            Assert.That(source, Is.Not.Null);
            return source;
        }

        [UnityTest]
        public IEnumerator Spawned_AppliesTheCentralSpatialSettingsToTheDoorSource()
        {
            yield return Begin(withAudioManager: true);

            AudioSource source = SpawnDoorAndGetSource();

            Assert.That(source.rolloffMode, Is.EqualTo(AudioRolloffMode.Linear));
            Assert.That(source.minDistance, Is.EqualTo(2f));
            Assert.That(source.maxDistance, Is.EqualTo(20f), "Spawned applies the central max distance.");
            Assert.That(source.spatialBlend, Is.EqualTo(1f), "The door stays positional.");
        }

        [UnityTest]
        public IEnumerator Spawned_WithoutAnAudioManager_LeavesTheDoorSourceUntouched()
        {
            yield return Begin(withAudioManager: false);

            AudioSource source = SpawnDoorAndGetSource();

            Assert.That(source.maxDistance, Is.EqualTo(500f), "Without an AudioManager the prefab values stay as authored.");
            Assert.That(source.spatialBlend, Is.EqualTo(1f));
        }
    }
}
#endif

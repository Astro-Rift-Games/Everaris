#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Assert = NUnit.Framework.Assert;

namespace Tests.PlayMode.Audio
{
    /// <summary>
    /// <see cref="PlayerAudioPresenter"/> health wiring (its LateUpdate feeding the health observer and playing the
    /// resulting cue), driven through a real Fusion runner and the real player prefab. Playback is observed on the
    /// pooled sources of the real <see cref="AudioManager"/>.
    /// </summary>
    public sealed class PlayerAudioPresenterPlayModeTests
    {
        private const string PlayerPrefabGuid = "fea3a7b256f965a4eb9b965832939741";
        private const float LethalDamage = 100000f;

        private readonly List<Object> _owned = new();
        private readonly AudioRunnerFixture _fixture = new();
        private PlayerCorpseGenerationSimulationDriver _driver;
        private PlayerCharacter _player;
        private PlayerDownedStateNetworkController _downed;
        private AudioManager _manager;
        private AudioClip _damageClip;
        private AudioClip _deathClip;

        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return _fixture.Shutdown();
            for (int i = _owned.Count - 1; i >= 0; i--) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
            _manager = null;
        }

        [UnityTest]
        public IEnumerator Damage_PlaysTakeDamage()
        {
            yield return Begin(withAudioManager: true);

            yield return Hit(10f, allowDowned: false);

            Assert.That(AudioTestKit.PlayingCount(_manager, _damageClip), Is.EqualTo(1), "A non-fatal hit plays TakeDamage once.");
            Assert.That(AudioTestKit.PlayingCount(_manager, _deathClip), Is.Zero, "A non-fatal hit must not play Death.");
            Assert.That(_player.IsAlive, Is.True, "Precondition: the hit was not fatal.");
        }

        [UnityTest]
        public IEnumerator Death_PlaysDeathOnce_AndNotTakeDamage()
        {
            yield return Begin(withAudioManager: true);

            yield return Hit(LethalDamage, allowDowned: false);
            yield return AudioTestKit.WaitFrames(10);

            Assert.That(_player.IsAlive, Is.False, "Precondition: the hit defeated the player.");
            Assert.That(AudioTestKit.PlayingCount(_manager, _deathClip), Is.EqualTo(1), "Death plays exactly once per death.");
            Assert.That(AudioTestKit.PlayingCount(_manager, _damageClip), Is.Zero, "The fatal hit is a Death, not a TakeDamage.");
        }

        [UnityTest]
        public IEnumerator Revive_DoesNotPlayTakeDamage_AndASecondDeathPlaysDeathAgain()
        {
            yield return Begin(withAudioManager: true);

            // First hit: Health drops to 0 and the player is Downed (still alive) -> an ordinary TakeDamage.
            yield return Hit(LethalDamage, allowDowned: true);
            Assert.That(_player.IsDowned, Is.True, "Precondition: the player is Downed.");
            Assert.That(AudioTestKit.PlayingCount(_manager, _damageClip), Is.EqualTo(1));

            // Second hit depletes the reserve -> definitive defeat -> Death.
            yield return Hit(LethalDamage, allowDowned: true);
            yield return AudioTestKit.WaitFrames(10);
            Assert.That(_player.IsAlive, Is.False, "Precondition: the player was defeated.");
            Assert.That(AudioTestKit.PlayingCount(_manager, _deathClip), Is.EqualTo(1));

            // Alive again (a new Downed cycle starts) with Health still 0: coming back is not damage.
            Assert.That(_downed.TryEnterDowned(), Is.True);
            yield return AudioTestKit.WaitFrames(10);
            Assert.That(_player.IsAlive, Is.True, "Precondition: the player is back.");
            Assert.That(AudioTestKit.PlayingCount(_manager, _damageClip), Is.EqualTo(1), "Reviving must not play TakeDamage.");
            Assert.That(AudioTestKit.PlayingCount(_manager, _deathClip), Is.EqualTo(1), "Reviving must not replay Death.");

            // Dying again re-arms Death.
            yield return Hit(LethalDamage, allowDowned: true);
            yield return AudioTestKit.WaitFrames(10);
            Assert.That(_player.IsAlive, Is.False, "Precondition: the player died a second time.");
            Assert.That(AudioTestKit.PlayingCount(_manager, _deathClip), Is.EqualTo(2), "A second death plays Death again.");
        }

        [UnityTest]
        public IEnumerator AudioManagerAppearingLate_DoesNotCatchUpDamageObservedWithoutIt()
        {
            yield return Begin(withAudioManager: false);

            yield return Hit(10f, allowDowned: false);
            Assert.That(AudioManager.Instance, Is.Null, "Precondition: no AudioManager yet.");

            _manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            yield return AudioTestKit.WaitFrames(10);

            Assert.That(AudioTestKit.ClipsUsedBy(_manager), Is.Empty,
                "Damage observed while no AudioManager existed must not be played late.");

            // Positive guard: the presenter is still wired and plays the next hit now that audio is available.
            yield return Hit(10f, allowDowned: false);
            Assert.That(AudioTestKit.PlayingCount(_manager, _damageClip), Is.EqualTo(1), "The next hit still plays TakeDamage.");
        }

        private IEnumerator Begin(bool withAudioManager)
        {
            Assume.That(AudioManager.Instance, Is.Null, "Another AudioManager is already published.");
            yield return _fixture.Start("PlayerAudioPresenterRunner", typeof(PlayerCorpseGenerationSimulationDriver));
            _driver = _fixture.Root.GetComponent<PlayerCorpseGenerationSimulationDriver>();

            _damageClip = Own(AudioTestKit.Clip("take-damage"));
            _deathClip = Own(AudioTestKit.Clip("death"));
            var config = Own(AudioTestKit.Config<PlayerAudioConfig>(
                (AudioKeys.TakeDamage, _damageClip), (AudioKeys.Death, _deathClip)));
            if (withAudioManager) _manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();

            NetworkObject playerObject = _fixture.Runner.Spawn(_fixture.Load(PlayerPrefabGuid), Vector3.zero, Quaternion.identity);
            _player = playerObject.GetComponent<PlayerCharacter>();
            _downed = playerObject.GetComponent<PlayerDownedStateNetworkController>();
            var presenter = playerObject.GetComponentInChildren<PlayerAudioPresenter>(true);
            Assert.That(presenter, Is.Not.Null, "The player prefab must carry a PlayerAudioPresenter.");
            AudioTestKit.SetReference(presenter, "_audioConfig", config);
            AudioTestKit.SetFloat(_downed, "_downedDrainPerSecond", 0f);

            _driver.Target = _player;
            _driver.Receiver = playerObject.GetComponent<PlayerLootReceiver>();
            _driver.SetEntries(Array.Empty<LootEntry>());

            // The first valid Health sample only sets the observer's baseline; let it happen before any hit.
            yield return AudioTestKit.WaitFrames(5);
            Assert.That(_player.IsAlive, Is.True);
            if (_manager != null) Assert.That(AudioTestKit.ClipsUsedBy(_manager), Is.Empty, "Spawning must be silent.");
        }

        private IEnumerator Hit(float amount, bool allowDowned)
        {
            _driver.AllowDowned = allowDowned;
            _driver.RequestedDamageAmount = amount;
            _driver.IsRequested = true;
            int frames = 300;
            while (_driver.IsRequested && frames-- > 0) yield return null;
            Assert.That(_driver.IsRequested, Is.False, "Driver request was not consumed by a simulation tick.");
            yield return AudioTestKit.WaitFrames(5);
        }
    }
}
#endif

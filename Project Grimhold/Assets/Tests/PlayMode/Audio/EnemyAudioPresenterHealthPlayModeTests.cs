#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
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
    /// <see cref="EnemyAudioPresenter"/> health wiring (LateUpdate feeding the health observer), driven through a real
    /// Fusion runner and the real melee enemy prefab. Enemies cannot revive, so the revive rules stay covered by
    /// <c>HealthAudioObserverTests</c> and the player suite.
    /// </summary>
    public sealed class EnemyAudioPresenterHealthPlayModeTests
    {
        private const string EnemyPrefabGuid = "5deca87613df0fa409d98702aec643d4";

        private readonly List<Object> _owned = new();
        private readonly AudioRunnerFixture _fixture = new();
        private EnemyFatalDamageSimulationDriver _driver;
        private EnemyCharacter _enemy;
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

            yield return Hit(1f);

            Assert.That(_enemy.IsAlive, Is.True, "Precondition: the hit was not fatal.");
            Assert.That(AudioTestKit.PlayingCount(_manager, _damageClip), Is.EqualTo(1), "A non-fatal hit plays TakeDamage once.");
            Assert.That(AudioTestKit.PlayingCount(_manager, _deathClip), Is.Zero);
        }

        [UnityTest]
        public IEnumerator Death_PlaysDeathOnce_AndNotTakeDamage()
        {
            yield return Begin(withAudioManager: true);

            yield return Hit(100000f);
            yield return AudioTestKit.WaitFrames(10);

            Assert.That(_enemy.IsAlive, Is.False, "Precondition: the hit killed the enemy.");
            Assert.That(AudioTestKit.PlayingCount(_manager, _deathClip), Is.EqualTo(1), "Death plays exactly once per death.");
            Assert.That(AudioTestKit.PlayingCount(_manager, _damageClip), Is.Zero, "The fatal hit is a Death, not a TakeDamage.");
        }

        [UnityTest]
        public IEnumerator AudioManagerAppearingLate_DoesNotCatchUpDamageObservedWithoutIt()
        {
            yield return Begin(withAudioManager: false);

            yield return Hit(1f);
            Assert.That(AudioManager.Instance, Is.Null, "Precondition: no AudioManager yet.");

            _manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            yield return AudioTestKit.WaitFrames(10);

            Assert.That(AudioTestKit.ClipsUsedBy(_manager), Is.Empty,
                "Damage observed while no AudioManager existed must not be played late.");

            yield return Hit(1f);
            Assert.That(AudioTestKit.PlayingCount(_manager, _damageClip), Is.EqualTo(1), "The next hit still plays TakeDamage.");
        }

        private IEnumerator Begin(bool withAudioManager)
        {
            Assume.That(AudioManager.Instance, Is.Null, "Another AudioManager is already published.");
            yield return _fixture.Start("EnemyAudioPresenterRunner", typeof(EnemyFatalDamageSimulationDriver));
            _driver = _fixture.Root.GetComponent<EnemyFatalDamageSimulationDriver>();

            _damageClip = Own(AudioTestKit.Clip("take-damage"));
            _deathClip = Own(AudioTestKit.Clip("death"));
            var config = Own(AudioTestKit.Config<EnemyAudioConfig>(
                (AudioKeys.TakeDamage, _damageClip), (AudioKeys.Death, _deathClip)));
            if (withAudioManager) _manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();

            string lootError = null;
            bool lootConfigured = false;
            NetworkObject spawned = _fixture.Runner.Spawn(_fixture.Load(EnemyPrefabGuid), Vector3.zero, Quaternion.identity,
                inputAuthority: null, onBeforeSpawned: (runner, instance) =>
                    lootConfigured = NetworkSpawnManager.TryConfigureEnemyLootBeforeSpawn(runner, instance, 145_976UL, 0, out lootError));
            Assert.That(lootConfigured, Is.True, lootError);
            _enemy = spawned.GetComponent<EnemyCharacter>();
            var presenter = spawned.GetComponentInChildren<EnemyAudioPresenter>(true);
            Assert.That(presenter, Is.Not.Null, "The enemy prefab must carry an EnemyAudioPresenter.");
            AudioTestKit.SetReference(presenter, "_audioConfig", config);
            _driver.Target = _enemy;

            // The first valid Health sample only sets the observer's baseline; let it happen before any hit.
            yield return AudioTestKit.WaitFrames(5);
            Assert.That(_enemy.IsAlive, Is.True);
            if (_manager != null) Assert.That(AudioTestKit.ClipsUsedBy(_manager), Is.Empty, "Spawning must be silent.");
        }

        private IEnumerator Hit(float amount)
        {
            _driver.DamageAmount = amount;
            _driver.IsRequested = true;
            int frames = 300;
            while (_driver.IsRequested && frames-- > 0) yield return null;
            Assert.That(_driver.IsRequested, Is.False, "Driver request was not consumed by a simulation tick.");
            yield return AudioTestKit.WaitFrames(5);
        }
    }
}
#endif

#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Assert = NUnit.Framework.Assert;

namespace Tests.PlayMode.Audio
{
    /// <summary>
    /// Enemies have no reload mechanic (the skeleton throws bones by hand), so an enemy attack never plays a Reload cue,
    /// even if a config still carries one. Shoot/Attack keep playing exactly as before.
    /// </summary>
    public sealed class EnemyAudioPresenterTests
    {
        private readonly List<Object> _owned = new();
        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }

        [TearDown]
        public void Cleanup()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        [UnityTest]
        public IEnumerator RangedAttack_PlaysShoot_AndNeverReload_EvenWhenConfigCarriesReload()
        {
            AudioClip shoot = Own(AudioClip.Create("shoot", 44100, 1, 44100, false));
            AudioClip reload = Own(AudioClip.Create("reload", 44100, 1, 44100, false));
            AudioManager manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            AudioTestKit.StubCombatController combat = CreateEnemy(Config(("Shoot", shoot), ("Reload", reload)));

            combat.Raise(AttackType.Ranged);
            // Long enough for the removed 0.25 s reload delay to have elapsed many times over.
            yield return new WaitForSeconds(0.6f);

            Assert.That(ClipsUsedBy(manager).Contains(shoot), Is.True, "The ranged attack must still play Shoot.");
            Assert.That(ClipsUsedBy(manager).Contains(reload), Is.False, "Enemies have no reload mechanic: Reload must never play.");
        }

        [Test]
        public void MeleeAttack_StillPlaysAttack()
        {
            AudioClip attack = Own(AudioClip.Create("attack", 44100, 1, 44100, false));
            AudioManager manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            AudioTestKit.StubCombatController combat = CreateEnemy(Config(("Attack", attack)));

            combat.Raise(AttackType.Melee);

            Assert.That(ClipsUsedBy(manager).Contains(attack), Is.True);
        }

        private AudioTestKit.StubCombatController CreateEnemy(EnemyAudioConfig config)
        {
            var enemy = Own(new GameObject("Enemy audio owner"));
            var combat = enemy.AddComponent<AudioTestKit.StubCombatController>();
            var presenter = enemy.AddComponent<EnemyAudioPresenter>();
            AudioTestKit.SetReference(presenter, "_audioConfig", config);
            return combat;
        }

        private static List<AudioClip> ClipsUsedBy(AudioManager manager) => AudioTestKit.ClipsUsedBy(manager);

        private EnemyAudioConfig Config(params (string key, AudioClip clip)[] entries) =>
            Own(AudioTestKit.Config<EnemyAudioConfig>(entries));
    }
}
#endif

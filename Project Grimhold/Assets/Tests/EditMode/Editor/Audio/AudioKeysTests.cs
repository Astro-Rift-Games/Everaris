#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Audio
{
    /// <summary>
    /// Audio keys are centralized constants; lookups are case-insensitive so a wrong spelling fails silently.
    /// These tests pin the canonical spellings, the footstep key unification and the key-rule helpers.
    /// </summary>
    public sealed class AudioKeysTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        [TestCase(AudioKeys.Step, "Step")]
        [TestCase(AudioKeys.TakeDamage, "TakeDamage")]
        [TestCase(AudioKeys.Death, "Death")]
        [TestCase(AudioKeys.Swing, "Swing")]
        [TestCase(AudioKeys.Shoot, "Shoot")]
        [TestCase(AudioKeys.Cast, "Cast")]
        [TestCase(AudioKeys.Charge, "Charge")]
        [TestCase(AudioKeys.Reload, "Reload")]
        [TestCase(AudioKeys.Attack, "Attack")]
        [TestCase(AudioKeys.Hit, "Hit")]
        [TestCase(AudioKeys.Block, "Block")]
        [TestCase(AudioKeys.Town, "Town")]
        [TestCase(AudioKeys.Raid, "Raid")]
        public void Constants_MatchTheKeysUsedByAssetsAndAnimationEvents(string constant, string expected)
        {
            Assert.That(constant, Is.EqualTo(expected));
        }

        [Test]
        public void PlayerTimerFootsteps_ResolveTheStepKey_UsedByAnimationEventsAndAssets()
        {
            PlayerAudioPresenter presenter = CreatePresenter(enableTimerFootsteps: true, stepInterval: 0.35f);

            Assert.That(presenter.PollTimerFootstep(true, 0.016f), Is.EqualTo(AudioKeys.Step));
        }

        [Test]
        public void PlayerTimerFootsteps_RepeatEveryInterval_AndResetWhenStopped()
        {
            PlayerAudioPresenter presenter = CreatePresenter(enableTimerFootsteps: true, stepInterval: 0.35f);

            Assert.That(presenter.PollTimerFootstep(true, 0.1f), Is.EqualTo(AudioKeys.Step), "first poll while moving");
            Assert.That(presenter.PollTimerFootstep(true, 0.1f), Is.Null, "inside the interval");
            Assert.That(presenter.PollTimerFootstep(true, 0.3f), Is.EqualTo(AudioKeys.Step), "interval elapsed");
            Assert.That(presenter.PollTimerFootstep(false, 0.1f), Is.Null, "stopped");
            Assert.That(presenter.PollTimerFootstep(true, 0.1f), Is.EqualTo(AudioKeys.Step), "restarts immediately after stopping");
        }

        [Test]
        public void PlayerTimerFootsteps_Disabled_NeverResolveAKey()
        {
            PlayerAudioPresenter presenter = CreatePresenter(enableTimerFootsteps: false, stepInterval: 0.35f);

            Assert.That(presenter.PollTimerFootstep(true, 1f), Is.Null);
        }

        [Test]
        public void SpecFor_MapsEachConfigType_AndIgnoresUnknownTypes()
        {
            Assert.That(AudioKeys.SpecFor(typeof(PlayerAudioConfig)), Is.Not.Null);
            Assert.That(AudioKeys.SpecFor(typeof(EnemyAudioConfig)), Is.Not.Null);
            Assert.That(AudioKeys.SpecFor(typeof(WeaponAudioConfig)), Is.Not.Null);
            Assert.That(AudioKeys.SpecFor(typeof(MusicAudioConfig)), Is.Not.Null);
            Assert.That(AudioKeys.SpecFor(typeof(string)), Is.Null);
            Assert.That(AudioKeys.SpecFor(null), Is.Null);
        }

        [Test]
        public void Specs_DeclareTheKeysWithoutContentYetAsOptional()
        {
            Assert.That(AudioKeys.SpecFor(typeof(PlayerAudioConfig)).Optional, Does.Contain(AudioKeys.Death));
            Assert.That(AudioKeys.SpecFor(typeof(EnemyAudioConfig)).Optional, Does.Contain(AudioKeys.Death));
            AudioKeySpec weapon = AudioKeys.SpecFor(typeof(WeaponAudioConfig));
            Assert.That(weapon.Optional, Does.Contain(AudioKeys.Charge));
            Assert.That(weapon.Optional, Does.Contain(AudioKeys.Block));
            Assert.That(weapon.RequiredAnyOf, Does.Contain(AudioKeys.Cast));
            foreach (string key in new[] { AudioKeys.Death, AudioKeys.Charge, AudioKeys.Block })
            {
                Assert.That(weapon.Required, Does.Not.Contain(key), key);
                Assert.That(AudioKeys.SpecFor(typeof(PlayerAudioConfig)).Required, Does.Not.Contain(key), key);
                Assert.That(AudioKeys.SpecFor(typeof(EnemyAudioConfig)).Required, Does.Not.Contain(key), key);
            }
        }

        [Test]
        public void EnemySpec_DoesNotAcceptReload_WhileWeaponSpecKeepsIt()
        {
            AudioKeySpec enemy = AudioKeys.SpecFor(typeof(EnemyAudioConfig));
            AudioKeySpec weapon = AudioKeys.SpecFor(typeof(WeaponAudioConfig));

            Assert.That(enemy.IsKnown(AudioKeys.Reload), Is.False, "Enemies have no reload mechanic.");
            Assert.That(enemy.Optional, Does.Not.Contain(AudioKeys.Reload));
            Assert.That(enemy.FindUnknown(new[] { AudioKeys.TakeDamage, AudioKeys.Reload }), Is.EqualTo(new[] { AudioKeys.Reload }));
            Assert.That(weapon.Optional, Does.Contain(AudioKeys.Reload), "Player weapon reload audio is unchanged.");
        }

        [Test]
        public void FindMissing_ReportsRequiredKeysAndTheAnyOfGroup()
        {
            AudioKeySpec enemy = AudioKeys.SpecFor(typeof(EnemyAudioConfig));

            Assert.That(enemy.FindMissing(new[] { AudioKeys.TakeDamage, AudioKeys.Step, AudioKeys.Attack }), Is.Empty);
            Assert.That(enemy.FindMissing(new[] { AudioKeys.TakeDamage, AudioKeys.Step, AudioKeys.Shoot }), Is.Empty);
            Assert.That(enemy.FindMissing(new[] { AudioKeys.TakeDamage }),
                Is.EquivalentTo(new[] { AudioKeys.Step, "at least one of [Attack, Shoot]" }));
            Assert.That(enemy.FindMissing(new[] { "step", AudioKeys.TakeDamage, AudioKeys.Attack }),
                Is.EqualTo(new[] { AudioKeys.Step }), "required keys match the canonical spelling exactly");
        }

        [Test]
        public void FindUnknown_FlagsTyposWrongCaseEmptyAndOtherTypesKeys()
        {
            AudioKeySpec player = AudioKeys.SpecFor(typeof(PlayerAudioConfig));

            Assert.That(player.FindUnknown(new[] { AudioKeys.Step, AudioKeys.TakeDamage, AudioKeys.Death }), Is.Empty);
            Assert.That(player.FindUnknown(new[] { "Movement", "step", "TakeDamge", "", AudioKeys.Swing }),
                Is.EqualTo(new[] { "Movement", "step", "TakeDamge", "<empty key>", AudioKeys.Swing }));
        }

        [Test]
        public void FindDuplicates_IsCaseInsensitive_AndReportsEachKeyOnce()
        {
            Assert.That(AudioKeySpec.FindDuplicates(new[] { "Step", "TakeDamage" }), Is.Empty);
            Assert.That(AudioKeySpec.FindDuplicates(new[] { "Step", "step", "STEP", "Attack", "attack" }),
                Is.EqualTo(new[] { "step", "attack" }));
        }

        private PlayerAudioPresenter CreatePresenter(bool enableTimerFootsteps, float stepInterval)
        {
            _host = new GameObject("PlayerAudioPresenterTestHost");
            var presenter = _host.AddComponent<PlayerAudioPresenter>();
            var serialized = new SerializedObject(presenter);
            serialized.FindProperty("_enableTimerFootsteps").boolValue = enableTimerFootsteps;
            serialized.FindProperty("_stepInterval").floatValue = stepInterval;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return presenter;
        }
    }
}
#endif

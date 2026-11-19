#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace Tests.EditMode.Audio
{
    public sealed class HealthAudioObserverTests
    {
        private static HealthAudioCue Alive(HealthAudioObserver o, float health) => o.Observe(true, true, health);
        private static HealthAudioCue Dead(HealthAudioObserver o, float health = 0f) => o.Observe(true, false, health);

        [Test]
        public void FirstValidSample_IsBaselineOnly()
        {
            var o = new HealthAudioObserver();
            Assert.That(Alive(o, 50f), Is.EqualTo(HealthAudioCue.None));
            Assert.That(Alive(o, 50f), Is.EqualTo(HealthAudioCue.None));
            Assert.That(o.IsDead, Is.False);
        }

        [Test]
        public void FirstValidSample_AlreadyDead_DoesNotPlayDeath()
        {
            var o = new HealthAudioObserver();
            Assert.That(Dead(o), Is.EqualTo(HealthAudioCue.None));
            Assert.That(o.IsDead, Is.True);
            Assert.That(Dead(o), Is.EqualTo(HealthAudioCue.None));
        }

        [Test]
        public void Damage_EmitsTakeDamage_AndUpdatesBaseline()
        {
            var o = new HealthAudioObserver();
            Alive(o, 100f);
            Assert.That(Alive(o, 90f), Is.EqualTo(HealthAudioCue.TakeDamage));
            Assert.That(Alive(o, 90f), Is.EqualTo(HealthAudioCue.None), "Same health must not repeat the cue.");
            Assert.That(Alive(o, 40f), Is.EqualTo(HealthAudioCue.TakeDamage), "Several hits in one sample play one cue.");
        }

        [Test]
        public void SubEpsilonLoss_EmitsNothing()
        {
            var o = new HealthAudioObserver();
            Alive(o, 100f);
            Assert.That(Alive(o, 99.9995f), Is.EqualTo(HealthAudioCue.None));
        }

        [Test]
        public void Heal_EmitsNothing_AndRaisesBaseline()
        {
            var o = new HealthAudioObserver();
            Alive(o, 50f);
            Assert.That(Alive(o, 80f), Is.EqualTo(HealthAudioCue.None));
            Assert.That(Alive(o, 70f), Is.EqualTo(HealthAudioCue.TakeDamage), "Baseline must follow the healed value.");
        }

        [Test]
        public void Death_EmitsDeathOnce()
        {
            var o = new HealthAudioObserver();
            Alive(o, 10f);
            Assert.That(Dead(o), Is.EqualTo(HealthAudioCue.Death), "Death replaces the damage cue.");
            Assert.That(o.IsDead, Is.True);
            Assert.That(Dead(o), Is.EqualTo(HealthAudioCue.None));
            Assert.That(Dead(o), Is.EqualTo(HealthAudioCue.None));
        }

        [Test]
        public void Revive_WithLowerHealthThanBeforeDeath_EmitsNothing_AndSecondDeathPlaysAgain()
        {
            var o = new HealthAudioObserver();
            Alive(o, 100f);
            Assert.That(Dead(o), Is.EqualTo(HealthAudioCue.Death));
            Assert.That(Alive(o, 30f), Is.EqualTo(HealthAudioCue.None), "Revive below the pre-death sample is not damage.");
            Assert.That(o.IsDead, Is.False);
            Assert.That(Alive(o, 30f), Is.EqualTo(HealthAudioCue.None));
            Assert.That(Alive(o, 20f), Is.EqualTo(HealthAudioCue.TakeDamage), "Baseline is the revive health.");
            Assert.That(Dead(o), Is.EqualTo(HealthAudioCue.Death), "A second death must play again.");
            Assert.That(Dead(o), Is.EqualTo(HealthAudioCue.None));
        }

        [Test]
        public void InvalidSample_EmitsNothing_AndDoesNotTouchBaselineOrDeathState()
        {
            var o = new HealthAudioObserver();
            Alive(o, 100f);
            Assert.That(o.Observe(false, true, 0f), Is.EqualTo(HealthAudioCue.None), "Invalid with stale alive/health.");
            Assert.That(o.Observe(false, false, 0f), Is.EqualTo(HealthAudioCue.None));
            Assert.That(Alive(o, 100f), Is.EqualTo(HealthAudioCue.None), "Baseline stays 100; invalid health 0 was ignored.");
            Assert.That(Alive(o, 80f), Is.EqualTo(HealthAudioCue.TakeDamage));

            Dead(o);
            Assert.That(o.Observe(false, true, 80f), Is.EqualTo(HealthAudioCue.None));
            Assert.That(o.IsDead, Is.True, "Invalid sample must not re-arm death.");
            Assert.That(Dead(o), Is.EqualTo(HealthAudioCue.None));
        }

        [Test]
        public void InvalidFirstSample_DoesNotInitialize()
        {
            var o = new HealthAudioObserver();
            Assert.That(o.Observe(false, true, 0f), Is.EqualTo(HealthAudioCue.None));
            Assert.That(Alive(o, 60f), Is.EqualTo(HealthAudioCue.None), "First valid sample is still the baseline.");
        }

        [Test]
        public void ObservationContinuesWhilePlaybackUnavailable_NoCatchUpCue()
        {
            // The presenter keeps feeding samples even when AudioManager is missing and simply skips playing
            // the returned cue; a later sample with the same health must not replay the missed damage.
            var o = new HealthAudioObserver();
            Alive(o, 100f);
            Assert.That(Alive(o, 70f), Is.EqualTo(HealthAudioCue.TakeDamage), "Returned (and dropped by the presenter) while unavailable.");
            Assert.That(Alive(o, 70f), Is.EqualTo(HealthAudioCue.None), "No catch-up once playback is available.");
        }

        [Test]
        public void Reset_ForgetsBaselineAndDeathState()
        {
            var o = new HealthAudioObserver();
            Alive(o, 100f);
            Dead(o);
            o.Reset();
            Assert.That(o.IsDead, Is.False);
            Assert.That(Alive(o, 10f), Is.EqualTo(HealthAudioCue.None), "After Reset the next valid sample is a baseline.");
            Assert.That(Dead(o), Is.EqualTo(HealthAudioCue.Death));
        }
    }
}
#endif

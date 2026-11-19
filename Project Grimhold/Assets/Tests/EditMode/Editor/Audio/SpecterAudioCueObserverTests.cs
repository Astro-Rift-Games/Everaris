#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using ProjectGrimhold.Gameplay.Enemies.Specter;

namespace Tests.EditMode.Audio
{
    /// <summary>
    /// Pure rules of <see cref="SpecterAudioCueObserver"/>: a cue is emitted once per transition INTO
    /// Canalizacion / Detonacion / Reaparicion, the first valid sample (spawn, late join, host-migration restore)
    /// only seeds the baseline, and invalid samples never emit nor disturb the baseline.
    /// </summary>
    public sealed class SpecterAudioCueObserverTests
    {
        private SpecterAudioCueObserver _observer;

        [SetUp]
        public void SetUp() => _observer = new SpecterAudioCueObserver();

        [TestCase(SpecterStateType.Canalizacion, SpecterAudioCue.Channel)]
        [TestCase(SpecterStateType.Detonacion, SpecterAudioCue.Detonate)]
        [TestCase(SpecterStateType.Reaparicion, SpecterAudioCue.Reappear)]
        public void EnteringACueState_EmitsItsCue_Once(SpecterStateType state, SpecterAudioCue expected)
        {
            Assert.That(_observer.Observe(true, SpecterStateType.Reposo), Is.EqualTo(SpecterAudioCue.None), "seed");

            Assert.That(_observer.Observe(true, state), Is.EqualTo(expected));
            Assert.That(_observer.Observe(true, state), Is.EqualTo(SpecterAudioCue.None), "same state on later frames");
            Assert.That(_observer.Observe(true, state), Is.EqualTo(SpecterAudioCue.None));
        }

        [TestCase(SpecterStateType.Reposo)]
        [TestCase(SpecterStateType.Persecucion)]
        [TestCase(SpecterStateType.Recuperacion)]
        [TestCase(SpecterStateType.Desvanecimiento)]
        [TestCase(SpecterStateType.Desvanecido)]
        [TestCase(SpecterStateType.Muerte)]
        public void EnteringAStateWithoutCue_EmitsNothing(SpecterStateType state)
        {
            _observer.Observe(true, SpecterStateType.Canalizacion);

            Assert.That(_observer.Observe(true, state), Is.EqualTo(SpecterAudioCue.None));
        }

        [Test]
        public void FullAttackAndFadeCycle_EmitsEachCueExactlyOnce_InOrder()
        {
            var states = new[]
            {
                SpecterStateType.Reposo, SpecterStateType.Persecucion, SpecterStateType.Persecucion,
                SpecterStateType.Canalizacion, SpecterStateType.Canalizacion,
                SpecterStateType.Detonacion, SpecterStateType.Detonacion,
                SpecterStateType.Recuperacion, SpecterStateType.Desvanecimiento, SpecterStateType.Desvanecido,
                SpecterStateType.Desvanecido, SpecterStateType.Reaparicion, SpecterStateType.Reaparicion,
                SpecterStateType.Persecucion,
            };

            var cues = new System.Collections.Generic.List<SpecterAudioCue>();
            foreach (SpecterStateType state in states)
            {
                SpecterAudioCue cue = _observer.Observe(true, state);
                if (cue != SpecterAudioCue.None) cues.Add(cue);
            }

            Assert.That(cues, Is.EqualTo(new[] { SpecterAudioCue.Channel, SpecterAudioCue.Detonate, SpecterAudioCue.Reappear }));
        }

        [Test]
        public void ChannelCancelledByStunAndStartedAgain_PlaysTheChannelCueAgain()
        {
            _observer.Observe(true, SpecterStateType.Persecucion);

            Assert.That(_observer.Observe(true, SpecterStateType.Canalizacion), Is.EqualTo(SpecterAudioCue.Channel));
            Assert.That(_observer.Observe(true, SpecterStateType.Persecucion), Is.EqualTo(SpecterAudioCue.None));
            Assert.That(_observer.Observe(true, SpecterStateType.Canalizacion), Is.EqualTo(SpecterAudioCue.Channel));
        }

        [TestCase(SpecterStateType.Canalizacion)]
        [TestCase(SpecterStateType.Detonacion)]
        [TestCase(SpecterStateType.Reaparicion)]
        public void FirstValidSample_OnACueState_OnlySeedsTheBaseline(SpecterStateType joinedState)
        {
            Assert.That(_observer.Observe(true, joinedState), Is.EqualTo(SpecterAudioCue.None), "late join / restore");
            Assert.That(_observer.Observe(true, joinedState), Is.EqualTo(SpecterAudioCue.None));
        }

        [Test]
        public void InvalidSamples_NeverEmit_AndKeepTheBaseline()
        {
            Assert.That(_observer.Observe(false, SpecterStateType.Canalizacion), Is.EqualTo(SpecterAudioCue.None));
            Assert.That(_observer.Observe(false, SpecterStateType.Detonacion), Is.EqualTo(SpecterAudioCue.None));

            Assert.That(_observer.Observe(true, SpecterStateType.Canalizacion), Is.EqualTo(SpecterAudioCue.None),
                "the first VALID sample is the baseline, whatever invalid samples preceded it");

            _observer.Observe(false, SpecterStateType.Reposo);
            Assert.That(_observer.Observe(true, SpecterStateType.Canalizacion), Is.EqualTo(SpecterAudioCue.None),
                "an invalid sample must not clear the baseline");
            Assert.That(_observer.Observe(true, SpecterStateType.Detonacion), Is.EqualTo(SpecterAudioCue.Detonate));
        }

        [Test]
        public void Reset_ForgetsTheBaseline_SoTheNextValidSampleSeedsAgain()
        {
            _observer.Observe(true, SpecterStateType.Reposo);
            Assert.That(_observer.Observe(true, SpecterStateType.Canalizacion), Is.EqualTo(SpecterAudioCue.Channel));

            _observer.Reset();

            Assert.That(_observer.Observe(true, SpecterStateType.Detonacion), Is.EqualTo(SpecterAudioCue.None),
                "re-enabled mid-state: no cue for a state entered before");
            Assert.That(_observer.Observe(true, SpecterStateType.Recuperacion), Is.EqualTo(SpecterAudioCue.None));
            Assert.That(_observer.Observe(true, SpecterStateType.Canalizacion), Is.EqualTo(SpecterAudioCue.Channel));
        }
    }
}
#endif

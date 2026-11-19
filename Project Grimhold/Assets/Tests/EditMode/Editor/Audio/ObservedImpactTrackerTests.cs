#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace Tests.EditMode.Audio
{
    /// <summary>Pure decision of when a peer presents a replicated impact snapshot (see ObservedImpactTracker).</summary>
    public sealed class ObservedImpactTrackerTests
    {
        [Test]
        public void PeerWithoutInputAuthority_AcceptsEachNewerSequenceOnce()
        {
            var tracker = new ObservedImpactTracker();
            tracker.Seed(0);

            Assert.That(tracker.TryAccept(false, 1), Is.True);
            Assert.That(tracker.TryAccept(false, 1), Is.False, "The same sequence is presented only once.");
            Assert.That(tracker.TryAccept(false, 4), Is.True, "A gap (rejections share the sequence counter) still presents.");
            Assert.That(tracker.TryAccept(false, 4), Is.False);
        }

        [Test]
        public void PeerWithInputAuthority_NeverPresents_AndDoesNotReplayLaterWithoutIt()
        {
            var tracker = new ObservedImpactTracker();
            tracker.Seed(0);

            Assert.That(tracker.TryAccept(true, 1), Is.False, "The attacker hears impacts through the feedback RPC only.");
            Assert.That(tracker.TryAccept(true, 2), Is.False);
            Assert.That(tracker.TryAccept(false, 2), Is.False,
                "An impact consumed while holding Input Authority must not replay when authority is lost.");
            Assert.That(tracker.TryAccept(false, 3), Is.True);
        }

        [Test]
        public void SeededSequence_IsNeverPresented_ButTheNextOneIs()
        {
            var tracker = new ObservedImpactTracker();
            tracker.Seed(7);

            Assert.That(tracker.TryAccept(false, 7), Is.False, "A snapshot that existed at spawn/restore is not replayed.");
            Assert.That(tracker.TryAccept(false, 8), Is.True);
        }

        [Test]
        public void RestoredOlderSequence_IsSilent_AndLaterNewerImpactsPlay()
        {
            var tracker = new ObservedImpactTracker();
            tracker.Seed(5);

            Assert.That(tracker.TryAccept(false, 3), Is.False, "A regressed (restored) snapshot never plays.");
            Assert.That(tracker.TryAccept(false, 3), Is.False);
            Assert.That(tracker.TryAccept(false, 4), Is.True, "The tracker follows the replicated value, so the next real impact plays.");
        }

        [Test]
        public void EmptySnapshot_NeverPresents()
        {
            var tracker = new ObservedImpactTracker();
            tracker.Seed(0);

            Assert.That(tracker.TryAccept(false, 0), Is.False);
        }
    }
}
#endif

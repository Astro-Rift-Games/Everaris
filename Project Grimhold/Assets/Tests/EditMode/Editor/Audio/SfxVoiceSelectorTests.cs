#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace Tests.EditMode.Audio
{
    public sealed class SfxVoiceSelectorTests
    {
        [Test]
        public void EmptyPool_ReturnsNoVoice()
        {
            Assert.That(SfxVoiceSelector.Select(new bool[0], new long[0]), Is.EqualTo(-1));
        }

        [Test]
        public void FreeVoice_IsPreferredOverStealing()
        {
            var playing = new[] { true, true, false, true };
            var order = new long[] { 4, 1, 9, 2 };
            Assert.That(SfxVoiceSelector.Select(playing, order), Is.EqualTo(2));
        }

        [Test]
        public void SeveralFreeVoices_UsesFirstFree()
        {
            var playing = new[] { true, false, false };
            Assert.That(SfxVoiceSelector.Select(playing, new long[] { 1, 2, 3 }), Is.EqualTo(1));
        }

        [Test]
        public void FullPool_StealsVoiceThatStartedEarliest()
        {
            var playing = new[] { true, true, true, true };
            var order = new long[] { 7, 9, 3, 8 };
            Assert.That(SfxVoiceSelector.Select(playing, order), Is.EqualTo(2));
        }

        [Test]
        public void FullPool_IsNotRoundRobin_StealsEarliestRepeatedlyWhenItIsRestarted()
        {
            var playing = new[] { true, true, true };
            var order = new long[] { 1, 2, 3 };
            int first = SfxVoiceSelector.Select(playing, order);
            order[first] = 4; // The stolen voice is now the newest.
            int second = SfxVoiceSelector.Select(playing, order);
            order[second] = 5;
            int third = SfxVoiceSelector.Select(playing, order);
            Assert.That(new[] { first, second, third }, Is.EqualTo(new[] { 0, 1, 2 }));
            order[third] = 6;
            Assert.That(SfxVoiceSelector.Select(playing, order), Is.EqualTo(0), "Steals follow start age.");
        }

        [Test]
        public void FullPool_OlderVoiceAtHighIndex_IsStolenBeforeLowIndexNewerVoice()
        {
            var playing = new[] { true, true, true };
            var order = new long[] { 10, 11, 2 };
            Assert.That(SfxVoiceSelector.Select(playing, order), Is.EqualTo(2));
        }

        [Test]
        public void FullPool_EqualStartOrder_TieBreaksToLowestIndex()
        {
            var playing = new[] { true, true, true };
            Assert.That(SfxVoiceSelector.Select(playing, new long[] { 5, 5, 5 }), Is.EqualTo(0));
        }

        [Test]
        public void VoiceFinishedNaturally_IsFreeEvenWithOldStartOrder()
        {
            var playing = new[] { true, false, true };
            Assert.That(SfxVoiceSelector.Select(playing, new long[] { 1, 0, 2 }), Is.EqualTo(1));
        }
    }
}
#endif

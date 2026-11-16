using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Player.Presentation
{
    public sealed class AimStanceDrawCueTests
    {
        [Test]
        public void DrawComplete_FiresOnceAndNotBeforeDeadline()
        {
            var cue = new AimStanceDrawCue();
            Assert.That(cue.Observe(true, 10, .49f, .5f), Is.False);
            Assert.That(cue.Observe(true, 10, .5f, .5f), Is.True);
            Assert.That(cue.Observe(true, 10, .51f, .5f), Is.False);
        }

        [Test]
        public void NewStart_RearmsAfterCompletedDraw()
        {
            var cue = new AimStanceDrawCue();
            Assert.That(cue.Observe(true, 10, .5f, .5f), Is.True);
            Assert.That(cue.Observe(true, 20, .1f, .5f), Is.False);
            Assert.That(cue.Observe(true, 20, .5f, .5f), Is.True);
        }

        [Test]
        public void ResumeOldDraw_IsNotReplayedEvenIfClockMovesBack()
        {
            var cue = new AimStanceDrawCue();
            Assert.That(cue.Observe(true, 10, .601f, .5f), Is.False);
            Assert.That(cue.Observe(true, 10, .5f, .5f), Is.False);
        }

        [Test]
        public void EndBeforeDraw_DoesNotFireAndNextStanceCanComplete()
        {
            var cue = new AimStanceDrawCue();
            Assert.That(cue.Observe(true, 10, .2f, .5f), Is.False);
            Assert.That(cue.Observe(false, -1, 1f, .5f), Is.False);
            Assert.That(cue.Observe(true, 30, .5f, .5f), Is.True);
        }

        [Test]
        public void MissedRenderWindow_DoesNotReplayOnResume()
        {
            var cue = new AimStanceDrawCue();
            Assert.That(cue.Observe(true, 10, .1f, .5f), Is.False);
            Assert.That(cue.Observe(true, 10, 3f, .5f), Is.False);
        }

        [Test]
        public void PresenterWithoutOptionalReferences_IsSilent()
        {
            var type = typeof(CombatFeedbackPresenter).Assembly.GetType("AimStanceDrawPresenter");
            Assert.That(type, Is.Not.Null, "Optional draw presenter must exist.");
            var go = new GameObject("Draw cue");
            try
            {
                var presenter = go.AddComponent(type);
                Assert.DoesNotThrow(() => type.GetMethod("Present").Invoke(presenter, null));
                go.SetActive(false);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}

using NUnit.Framework;
using ProjectGrimhold.Gameplay.Visibility;
using UnityEngine;

namespace Tests.EditMode.Visibility
{
    /// <summary>
    /// Entity visibility and the fog edge read one smoothed facing per frame, so an entity
    /// never appears or vanishes ahead of the fog sweeping over it.
    /// </summary>
    public sealed class VisionFacingSmootherTests
    {
        [Test]
        public void FirstStep_SnapsToTheRawFacing()
        {
            var smoother = new VisionFacingSmoother();

            Assert.That(smoother.Step(Vector2.right, 12f, 0.1f, frame: 1), Is.EqualTo(Vector2.right));
        }

        [Test]
        public void LaterFrame_MovesTowardTheRawFacingLikeTheFog()
        {
            var smoother = new VisionFacingSmoother();
            smoother.Step(Vector2.up, 12f, 0.016f, frame: 1);

            Vector2 next = smoother.Step(Vector2.right, 12f, 0.016f, frame: 2);

            Assert.That(next, Is.EqualTo(VisionFogRules.SmoothFacing(Vector2.up, Vector2.right, 12f, 0.016f)));
            Assert.That(next, Is.Not.EqualTo(Vector2.right));
        }

        [Test]
        public void SecondCallInTheSameFrame_ReturnsTheSameFacingWithoutAdvancing()
        {
            var smoother = new VisionFacingSmoother();
            smoother.Step(Vector2.up, 12f, 0.016f, frame: 1);
            Vector2 first = smoother.Step(Vector2.right, 12f, 0.016f, frame: 2);

            Vector2 second = smoother.Step(Vector2.right, 12f, 0.016f, frame: 2);

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void StepAfterSkippedFrames_SnapsInsteadOfSweepingFromAStaleFacing()
        {
            var smoother = new VisionFacingSmoother();
            smoother.Step(Vector2.up, 12f, 0.016f, frame: 1);

            // The viewer was not queried for a while (spectator switch, not yet spawned).
            Vector2 resumed = smoother.Step(Vector2.right, 12f, 0.016f, frame: 40);

            Assert.That(resumed, Is.EqualTo(Vector2.right));
        }

        [Test]
        public void Reset_SnapsAgainOnTheNextStep()
        {
            var smoother = new VisionFacingSmoother();
            smoother.Step(Vector2.up, 12f, 0.016f, frame: 1);

            smoother.Reset();

            Assert.That(smoother.Step(Vector2.left, 12f, 0.016f, frame: 2), Is.EqualTo(Vector2.left));
        }
    }
}

using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Player.Presentation
{
    public sealed class CommittedAimDirectionTests
    {
        [TestCase(0f, true)]
        [TestCase(0.49999f, true)]
        [TestCase(0.5f, false)]
        [TestCase(0.6f, false)]
        [TestCase(-0.01f, false)]
        public void AcceptedWindow_UsesCommittedDirectionOnlyBeforeRelease(float seconds, bool committed)
        {
            var attack = Attack(AttackType.Ranged);
            Assert.That(CommittedAimDirection.Resolve(Vector2.up, attack, true, seconds),
                Is.EqualTo(committed ? attack.Direction : Vector2.up));
        }

        [Test]
        public void CancelledOrUnavailableClock_ReturnsLiveAim()
        {
            Assert.That(CommittedAimDirection.Resolve(Vector2.left, Attack(AttackType.Ranged), false, 0.1f),
                Is.EqualTo(Vector2.left));
        }

        [Test]
        public void MeleeAndUntimedAttacks_KeepLiveAim()
        {
            Assert.That(CommittedAimDirection.Resolve(Vector2.up, Attack(AttackType.Melee), true, 0.1f),
                Is.EqualTo(Vector2.up));
            var untimed = new AttackPerformedEvent(default, AttackType.Ranged, Vector2.zero, Vector2.down, 10);
            Assert.That(CommittedAimDirection.Resolve(Vector2.up, untimed, true, 0.1f), Is.EqualTo(Vector2.up));
        }

        private static AttackPerformedEvent Attack(AttackType type) =>
            new(default, type, Vector2.zero, new Vector2(3f, -4f), 10, 1, 7, 40, 0.5f);
    }
}

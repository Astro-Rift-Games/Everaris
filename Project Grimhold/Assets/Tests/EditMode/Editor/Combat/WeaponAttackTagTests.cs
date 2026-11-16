#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Combat
{
    public sealed class WeaponAttackTagTests
    {
        [TestCase(AttackType.Melee, true)]
        [TestCase(AttackType.Ranged, false)]
        public void ConfirmedImpact_ClassifiesWeaponTypeAndForwardsFatal(AttackType type, bool melee)
        {
            var tag = new WeaponAttackTag(EquipmentSlot.WeaponSetAOffHand, 7, 9, type);
            var feedback = new CombatPresentationEvent(1, CombatFeedbackKind.ConfirmedImpact,
                new EntityId(2), Vector2.one, 5f, 10, AttackFailureReason.None, tag, Vector2.right, true);
            Assert.That(feedback.IsWeaponImpact, Is.True);
            Assert.That(feedback.IsMeleeImpact, Is.EqualTo(melee));
            Assert.That(feedback.IsFatal, Is.True);
            Assert.That(feedback.WeaponTag, Is.EqualTo(tag));
            Assert.That(feedback.SourceSlot, Is.EqualTo(EquipmentSlot.WeaponSetAOffHand));
            Assert.That(feedback.AttackSequence, Is.EqualTo(7));
            Assert.That(feedback.WeaponCatalogIndexPlusOne, Is.EqualTo(9));
            Assert.That(feedback.AttackType, Is.EqualTo(type));
            Assert.That(feedback.HitDirection, Is.EqualTo(Vector2.right));
        }

        [Test]
        public void UntaggedImpact_RemainsGenericAndNonFatal()
        {
            var feedback = new CombatPresentationEvent(1, CombatFeedbackKind.ConfirmedImpact,
                new EntityId(2), Vector2.one, 5f, 10, AttackFailureReason.None);
            Assert.That(feedback.IsWeaponImpact, Is.False);
            Assert.That(feedback.IsMeleeImpact, Is.False);
            Assert.That(feedback.IsFatal, Is.False);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void NonpositiveSequence_DoesNotTagAnImpact(int sequence)
        {
            var tag = new WeaponAttackTag(EquipmentSlot.WeaponSetAMainHand, sequence, 9, AttackType.Ranged);
            var feedback = new CombatPresentationEvent(1, CombatFeedbackKind.ConfirmedImpact,
                default, default, 5f, 10, AttackFailureReason.None, tag);
            Assert.That(feedback.HasWeaponTag, Is.False);
            Assert.That(feedback.IsWeaponImpact, Is.False);
            Assert.That(feedback.IsMeleeImpact, Is.False);
        }

        [Test]
        public void Rejection_IsNotAWeaponImpactEvenWithTag()
        {
            var tag = new WeaponAttackTag(EquipmentSlot.None, 7, 9, AttackType.Melee);
            var feedback = new CombatPresentationEvent(1, CombatFeedbackKind.AttackRejected,
                default, default, 0f, 10, AttackFailureReason.CooldownActive, tag);
            Assert.That(feedback.IsWeaponImpact, Is.False);
            Assert.That(feedback.IsMeleeImpact, Is.False);
        }
    }
}
#endif

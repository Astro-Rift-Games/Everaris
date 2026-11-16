#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Audio
{
    public sealed class WeaponAudioCueTests
    {
        private static AttackPerformedEvent Attack(AttackType type = AttackType.Ranged,
            EquipmentSlot slot = EquipmentSlot.None) => new(new EntityId(1), type,
                Vector2.zero, Vector2.right, 10, 1, 7, 20, 0.5f, sourceSlot: slot);

        [TestCase(AttackType.Ranged, EquipmentSlot.None)]
        [TestCase(AttackType.Melee, EquipmentSlot.None)]
        [TestCase(AttackType.Ranged, EquipmentSlot.WeaponSetAMainHand)]
        [TestCase(AttackType.Melee, EquipmentSlot.WeaponSetAOffHand)]
        public void ReleaseCue_NotBeforeDeadline_ExactlyOnceEvenIfClockRewinds(AttackType type, EquipmentSlot slot)
        {
            var cue = new WeaponAudioCue();
            cue.Accept(Attack(type, slot));
            Assert.That(cue.TryRelease(0f), Is.False, "Acceptance is not release.");
            Assert.That(cue.TryRelease(0.499f), Is.False);
            Assert.That(cue.TryRelease(0.5f), Is.True, "The release deadline must emit a cue.");
            Assert.That(cue.TryRelease(0.6f), Is.False);
            Assert.That(cue.TryRelease(0.2f), Is.False);
            Assert.That(cue.TryRelease(0.5f), Is.False);
        }

        [Test]
        public void ResumeAndDuplicateAcceptance_DoNotReplayRelease()
        {
            var cue = new WeaponAudioCue();
            cue.Accept(Attack(), resumed: true);
            Assert.That(cue.TryRelease(0.5f), Is.False);
            cue.Accept(Attack());
            Assert.That(cue.TryRelease(0.5f), Is.False);
        }

        [Test]
        public void Reload_RequiresActualReloadStart_AndReplaysOnlyAfterInterruption()
        {
            var cue = new WeaponAudioCue();
            cue.Accept(Attack());
            var reload = new WeaponReloadState();
            reload.MarkShot();
            Assert.That(cue.TryReload(reload), Is.False, "Unloaded alone is not reloading.");
            reload.Advance(20, 0.05f, 1f, true);
            Assert.That(cue.TryReload(reload), Is.True, "A real reload start emits the cue.");
            Assert.That(cue.TryReload(reload), Is.False);
            reload.Advance(21, 0.05f, 1f, false);
            Assert.That(cue.TryReload(reload), Is.False);
            reload.Advance(25, 0.05f, 1f, true);
            Assert.That(cue.TryReload(reload), Is.True);
            reload.Advance(45, 0.05f, 1f, true);
            Assert.That(cue.TryReload(reload), Is.False);
        }

        [TestCase(true, false, "Hit")]
        [TestCase(true, true, "Attack")]
        [TestCase(false, false, "Block")]
        [TestCase(false, true, "Block")]
        public void Impact_CharacterHitFallback_AttackPrecedence_SceneryUnchanged(bool character, bool attack, string key)
        {
            Assert.That(WeaponAudioCue.ImpactKey(character, attack), Is.EqualTo(key));
        }

        [TestCase(AttackType.Ranged, true, "Cast")]
        [TestCase(AttackType.Ranged, false, "Shoot")]
        [TestCase(AttackType.Melee, true, "Swing")]
        [TestCase(AttackType.Melee, false, "Swing")]
        public void Release_OptionalCastReplacesShoot_ExistingKeysUnchanged(AttackType type, bool cast, string key)
        {
            Assert.That(WeaponAudioCue.ReleaseKey(type, cast), Is.EqualTo(key));
        }

        [TestCase(EquipmentSlot.WeaponSetAMainHand)]
        [TestCase(EquipmentSlot.WeaponSetAOffHand)]
        public void Charge_OnceAtAcceptance_StopsAtRelease(EquipmentSlot slot)
        {
            var cue = new WeaponAudioCue();
            cue.Accept(Attack(slot: slot));
            Assert.That(cue.TryCharge(-0.01f), Is.False);
            Assert.That(cue.TryCharge(0f), Is.True, "Charge must start at confirmed acceptance.");
            Assert.That(cue.Charging, Is.True);
            cue.Accept(Attack(slot: slot));
            Assert.That(cue.TryCharge(0.1f), Is.False, "Duplicate acceptance must not replay Charge.");
            Assert.That(cue.TryRelease(0.499f), Is.False);
            Assert.That(cue.Charging, Is.True);
            Assert.That(cue.TryRelease(0.5f), Is.True);
            Assert.That(cue.Charging, Is.False, "Release must cut Charge before Cast.");
            Assert.That(cue.TryCharge(0f), Is.False);
        }

        [Test]
        public void Charge_CancellationAndSuspendStop_ResumeDoesNotReplay()
        {
            var cue = new WeaponAudioCue();
            cue.Accept(Attack());
            Assert.That(cue.TryCharge(0f), Is.True, "Charge must start before cancellation.");
            cue.CancelRelease();
            Assert.That(cue.Charging, Is.False);
            Assert.That(cue.TryCharge(0f), Is.False);
            Assert.That(cue.TryRelease(1f), Is.False);
            var offhand = Attack(slot: EquipmentSlot.WeaponSetAOffHand);
            cue.Accept(offhand);
            Assert.That(cue.TryCharge(0f), Is.True);
            cue.Suspend();
            Assert.That(cue.Charging, Is.False);
            cue.Accept(offhand, resumed: true);
            Assert.That(cue.TryCharge(0f), Is.False);
            var resumed = new WeaponAudioCue();
            resumed.Accept(Attack(), resumed: true);
            Assert.That(resumed.TryCharge(0f), Is.False);
        }

        [Test]
        public void Charge_LateAcceptanceAtReleaseIsSilent()
        {
            var cue = new WeaponAudioCue();
            cue.Accept(Attack());
            Assert.That(cue.TryCharge(0.5f), Is.False);
            Assert.That(cue.TryRelease(0.5f), Is.True);
        }

        [Test]
        public void LegacyUntimedAttack_StillReleasesImmediately()
        {
            var cue = new WeaponAudioCue();
            cue.Accept(new AttackPerformedEvent(new EntityId(2), AttackType.Melee, Vector2.zero, Vector2.up, 1));
            Assert.That(cue.TryRelease(0f), Is.True);
            Assert.That(cue.TryRelease(0f), Is.False);
        }
    }
}
#endif

#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Player.Presentation
{
    public sealed partial class PlayerPresentationStateSeamTests
    {
        private readonly List<Object> _chargeObjects = new();

        [TearDown]
        public void ReleaseChargeTestObjects()
        {
            foreach (Object item in _chargeObjects) if (item != null) Object.DestroyImmediate(item);
            _chargeObjects.Clear();
        }

        private (GameObject avatar, FakePlayerPresentationState state) ChargeRig(string weaponName = "MagicWand", bool configured = true)
        {
            GameObject avatar = CreateAvatar();
            var state = WeaponState(EquipmentSlot.WeaponSetAMainHand, weaponName);
            var loot = Object.Instantiate(state.AttackWeapon);
            var weapon = Object.Instantiate(loot.WeaponDefinition);
            _chargeObjects.Add(loot);
            _chargeObjects.Add(weapon);
            var lootData = new SerializedObject(loot);
            lootData.FindProperty("_weaponDefinition").objectReferenceValue = weapon;
            lootData.ApplyModifiedPropertiesWithoutUndo();
            var data = new SerializedObject(weapon);
            var chargeField = data.FindProperty("_presentation._windupChargeVfx");
            var burstField = data.FindProperty("_presentation._releaseBurstParticles");
            Assert.That(chargeField, Is.Not.Null, "Weapon presentation must expose optional windup charge data");
            Assert.That(burstField, Is.Not.Null, "Release burst must belong to the weapon, not its shared AttackVfx asset");
            if (configured)
            {
                var profile = ScriptableObject.CreateInstance<WindupChargeVfx>();
                _chargeObjects.Add(profile);
                var prefab = new GameObject("ChargeTestPrefab");
                SceneManager.MoveGameObjectToScene(prefab, _scene);
                var particles = prefab.AddComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.playOnAwake = false;
                main.duration = .1f;
                main.startLifetime = .1f;
                var profileData = new SerializedObject(profile);
                profileData.FindProperty("_prefab").objectReferenceValue = particles;
                profileData.FindProperty("_minimumScale").floatValue = .2f;
                profileData.FindProperty("_maximumScale").floatValue = 1f;
                profileData.FindProperty("_fullDrawMultiplier").floatValue = 2f;
                profileData.ApplyModifiedPropertiesWithoutUndo();
                if (weapon.Presentation.AttackVfx.Visual is CastFlashVfxVisualDefinition)
                    chargeField.objectReferenceValue = profile;
                burstField.objectReferenceValue = particles;
            }
            else
            {
                // The shipped weapon assets now carry charge/burst data; the unconfigured rig clears its clone.
                chargeField.objectReferenceValue = null;
                burstField.objectReferenceValue = null;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            state.AttackWeapon = loot;
            state.Equipment[EquipmentSlot.WeaponSetAMainHand] = loot;
            if (weapon.Handedness != WeaponHandedness.TwoHanded)
                state.Equipment[EquipmentSlot.WeaponSetAOffHand] = loot;
            state.AttackSeconds = 0f;
            BindAndInitialize(avatar, state);
            return (avatar, state);
        }

        private ParticleSystem ChargeInstance(GameObject avatar, bool off = false) =>
            avatar.GetComponentInChildren<PlayerAnimatorView>(true).transform.Find(off ? "OffHandWindupCharge" : "MainHandWindupCharge")?.GetComponent<ParticleSystem>();

        private static AttackPerformedEvent ChargeAttack(FakePlayerPresentationState state, bool off = false, bool aimed = false) =>
            new(default, state.AttackWeapon.WeaponDefinition.PrimaryAttack is RangedAttackConfig ? AttackType.Ranged : AttackType.Melee,
                Vector2.zero, Vector2.down, 10, 1, 7, 40,
                state.AttackWeapon.WeaponDefinition.AttackReleaseSeconds, aimed,
                off ? EquipmentSlot.WeaponSetAOffHand : EquipmentSlot.WeaponSetAMainHand);

        [TestCase(false)]
        [TestCase(true)]
        public void OptionalParticleHelpers_NullTintPreservesAuthoredRootAndChildColors(bool burstOnly)
        {
            var (avatar, state) = ChargeRig();
            var weapon = state.AttackWeapon.WeaponDefinition;
            var prefab = weapon.Presentation.ReleaseBurstParticles;
            var main = prefab.main;
            main.startColor = Color.red;
            var child = new GameObject("AuthoredChild");
            child.transform.SetParent(prefab.transform, false);
            var childParticles = child.AddComponent<ParticleSystem>();
            childParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var childMain = childParticles.main;
            childMain.playOnAwake = false;
            childMain.startColor = Color.blue;
            Transform root = avatar.GetComponentInChildren<PlayerAnimatorView>(true).transform;
            Assert.That(weapon.Presentation.AttackVfx.TryResolvePose(3, weapon.Presentation.BladeReach, out var pose), Is.True);
            var charge = new WindupChargePresentation();
            try
            {
                if (burstOnly) AttackReleaseBurstLifetime.Play(prefab, root, pose, null, 0);
                else
                {
                    charge.Begin(ChargeAttack(state), weapon, null, false);
                    Assert.That(charge.CheckClock(state, out float seconds), Is.True);
                    charge.Present(root, pose, seconds, false, 0);
                }
                Transform effect = root.Find(burstOnly ? "AttackReleaseBurst" : "MainHandWindupCharge");
                Assert.That(effect, Is.Not.Null);
                Assert.That(effect.GetComponent<ParticleSystem>().isPlaying, Is.True);
                Assert.That(effect.GetComponent<ParticleSystem>().main.startColor.color, Is.EqualTo(Color.red));
                Assert.That(effect.Find("AuthoredChild").GetComponent<ParticleSystem>().main.startColor.color, Is.EqualTo(Color.blue));
            }
            finally { charge.Dispose(); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Charge_WindowRampTintAndCastingTip_PerHand(bool off)
        {
            var (avatar, state) = ChargeRig();
            PresentFrame(avatar);
            Assert.That(ChargeInstance(avatar, off), Is.Null, "No charge before acceptance");
            state.RaiseAttack(ChargeAttack(state, off));
            PresentFrame(avatar);
            var charge = ChargeInstance(avatar, off);
            Assert.That(charge, Is.Not.Null);
            Assert.That(charge.transform.localScale.x, Is.EqualTo(.2f).Within(.001f), "Zero ramp at acceptance");
            Assert.That(charge.isPlaying, Is.True);
            var presenter = avatar.GetComponentInChildren<PlayerAttackVfxPresenter>(true);
            Assert.That(presenter.TryGetWeaponTint(state.AttackWeapon.WeaponDefinition.DamageType, out Color tint), Is.True);
            Assert.That(charge.main.startColor.color, Is.EqualTo(tint));
            float release = state.AttackWeapon.WeaponDefinition.AttackReleaseSeconds;
            state.AttackSeconds = release - .001f;
            PresentFrame(avatar);
            Assert.That(charge.transform.localScale.x, Is.EqualTo(1f).Within(.01f));
            state.AttackSeconds = release;
            PresentFrame(avatar);
            Assert.That(charge.isPlaying, Is.False, "Release clears charge, without changing the cast flash");
            var flash = state.AttackWeapon.WeaponDefinition.Presentation.AttackVfx.Visual as CastFlashVfxVisualDefinition;
            Transform root = avatar.GetComponentInChildren<PlayerAnimatorView>(true).transform;
            var renderer = off ? root.Find("OffHandAttackVfx")?.GetComponent<SpriteRenderer>() : root.Find("AttackVfx").GetComponent<SpriteRenderer>();
            Assert.That(renderer.enabled, Is.True);
            var burst = root.Find("AttackReleaseBurst");
            Assert.That(burst, Is.Not.Null);
            Assert.That(burst.GetComponent<ParticleSystem>().main.startColor.color, Is.EqualTo(tint), "Valid palette tints the release burst as well as the charge");
            Assert.That(Vector3.Distance(charge.transform.localPosition, renderer.transform.localPosition + (Vector3)flash.Center), Is.LessThan(.001f));
        }

        [TestCase("cancel")]
        [TestCase("downed")]
        [TestCase("disable")]
        public void Charge_InterruptionClearsImmediately(string reason)
        {
            var (avatar, state) = ChargeRig();
            state.RaiseAttack(ChargeAttack(state));
            PresentFrame(avatar);
            var instance = ChargeInstance(avatar);
            Assert.That(instance.isPlaying, Is.True);
            if (reason == "cancel") state.HasAttackClock = false;
            else if (reason == "downed") state.IsDowned = true;
            else avatar.GetComponentInChildren<PlayerAttackVfxPresenter>(true).ReleasePresentation();
            if (reason != "disable") PresentFrame(avatar);
            Assert.That(instance.isPlaying, Is.False);
            Assert.That(instance.particleCount, Is.Zero);
        }

        [Test]
        public void Charge_ResumeUsesRemainingPhaseAndNeverReplaysBurst()
        {
            var (avatar, state) = ChargeRig();
            state.AttackSeconds = .175f;
            state.ResumeAttack(ChargeAttack(state));
            PresentFrame(avatar);
            var charge = ChargeInstance(avatar);
            Assert.That(charge.transform.localScale.x, Is.EqualTo(.6f).Within(.001f));
            state.AttackSeconds = .35f;
            PresentFrame(avatar);
            Assert.That(charge.isPlaying, Is.False);
            Assert.That(avatar.GetComponentInChildren<PlayerAnimatorView>(true).transform.Find("AttackReleaseBurst"), Is.Null);
            state.ResumeAttack(ChargeAttack(state));
            PresentFrame(avatar);
            Assert.That(charge.isPlaying, Is.False);
        }

        [Test]
        public void Charge_ResumeAfterLocalClearRestoresOnlyRemainingWindow()
        {
            var (avatar, state) = ChargeRig();
            state.RaiseAttack(ChargeAttack(state));
            PresentFrame(avatar);
            var charge = ChargeInstance(avatar);
            avatar.GetComponentInChildren<PlayerAttackVfxPresenter>(true).CancelAndRestore();
            state.AttackSeconds = .175f;
            state.ResumeAttack(ChargeAttack(state));
            PresentFrame(avatar);
            Assert.That(charge.isPlaying, Is.True);
            Assert.That(charge.transform.localScale.x, Is.EqualTo(.6f).Within(.001f));
        }

        [TestCase(DamageType.Physical)]
        [TestCase(DamageType.Magical)]
        [TestCase(DamageType.TrueDamage)]
        public void Charge_UsesConfirmedWeaponDamageType(DamageType type)
        {
            var (avatar, state) = ChargeRig();
            var data = new SerializedObject(state.AttackWeapon.WeaponDefinition);
            data.FindProperty("_damageType").enumValueIndex = (int)type;
            data.ApplyModifiedPropertiesWithoutUndo();
            state.RaiseAttack(ChargeAttack(state));
            PresentFrame(avatar);
            var presenter = avatar.GetComponentInChildren<PlayerAttackVfxPresenter>(true);
            Assert.That(presenter.TryGetWeaponTint(type, out Color expected), Is.True);
            Assert.That(ChargeInstance(avatar).main.startColor.color, Is.EqualTo(expected));
        }

        [TestCase(false, 1f)]
        [TestCase(true, 2f)]
        public void Charge_StaffFullyDrawnIntensifies(bool full, float expected)
        {
            var (avatar, state) = ChargeRig("MagicStaff");
            state.IsAimStance = true;
            state.AimStanceSeconds = full ? 10f : 0f;
            state.AttackSeconds = .45f;
            state.RaiseAttack(ChargeAttack(state, aimed: full));
            PresentFrame(avatar);
            Assert.That(ChargeInstance(avatar).transform.localScale.x, Is.EqualTo(.6f * expected).Within(.001f));
        }

        [Test]
        public void Charge_NullDataPreservesExistingFlashAndAllocatesNoEffects()
        {
            var (avatar, state) = ChargeRig(configured: false);
            state.RaiseAttack(ChargeAttack(state));
            PresentFrame(avatar);
            Assert.That(ChargeInstance(avatar), Is.Null);
            state.AttackSeconds = .35f;
            PresentFrame(avatar);
            Assert.That(ChargeInstance(avatar), Is.Null);
            Transform root = avatar.GetComponentInChildren<PlayerAnimatorView>(true).transform;
            Assert.That(root.Find("AttackReleaseBurst"), Is.Null);
            Assert.That(root.Find("AttackVfx").GetComponent<SpriteRenderer>().enabled, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Burst_OnceAtVisualReleaseInExactSlashPose(bool off)
        {
            var (avatar, state) = ChargeRig("MagicSword");
            state.RaiseAttack(ChargeAttack(state, off));
            PresentFrame(avatar);
            Transform root = avatar.GetComponentInChildren<PlayerAnimatorView>(true).transform;
            Assert.That(root.Find("AttackReleaseBurst"), Is.Null);
            var definition = state.AttackWeapon.WeaponDefinition;
            state.AttackSeconds = definition.AttackReleaseSeconds - definition.Presentation.AttackVfx.ReleaseLeadSeconds;
            PresentFrame(avatar);
            Transform burst = root.Find("AttackReleaseBurst");
            Assert.That(burst, Is.Not.Null);
            Transform slash = root.Find(off ? "OffHandAttackVfx" : "AttackVfx");
            Assert.That(Vector3.Distance(burst.position, slash.position), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(burst.rotation, slash.rotation), Is.LessThan(.01f));
            var effect = burst.GetComponent<ParticleSystem>();
            Assert.That(effect.main.loop, Is.False);
            Assert.That(effect.main.startColor.color, Is.EqualTo(slash.GetComponent<SpriteRenderer>().color));
            PresentFrame(avatar);
            int count = 0;
            foreach (Transform child in root) if (child.name == "AttackReleaseBurst") count++;
            Assert.That(count, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Burst_MeleeResumeDoesNotReplay(bool off)
        {
            var (avatar, state) = ChargeRig("MagicCinquedea");
            var weapon = state.AttackWeapon.WeaponDefinition;
            state.AttackSeconds = weapon.AttackReleaseSeconds - weapon.Presentation.AttackVfx.ReleaseLeadSeconds;
            state.ResumeAttack(ChargeAttack(state, off));
            PresentFrame(avatar);
            Assert.That(avatar.GetComponentInChildren<PlayerAnimatorView>(true).transform.Find("AttackReleaseBurst"), Is.Null);
        }

        [Test]
        public void Charge_NewAttackReusesInstanceAndRefreshesDepth()
        {
            var (avatar, state) = ChargeRig();
            state.RaiseAttack(ChargeAttack(state));
            PresentFrame(avatar);
            var charge = ChargeInstance(avatar);
            int frontOrder = charge.GetComponent<ParticleSystemRenderer>().sortingOrder;
            avatar.GetComponentInChildren<PlayerAttackVfxPresenter>(true).CancelAndRestore();
            state.AimDirection = state.FacingDirection = Vector2.up;
            state.RaiseAttack(new AttackPerformedEvent(default, AttackType.Ranged, Vector2.zero, Vector2.up,
                50, 1, 8, 80, .35f, sourceSlot: EquipmentSlot.WeaponSetAMainHand));
            PresentFrame(avatar);
            Assert.That(ChargeInstance(avatar), Is.SameAs(charge));
            Assert.That(charge.isPlaying, Is.True);
            Assert.That(charge.GetComponent<ParticleSystemRenderer>().sortingOrder, Is.Not.EqualTo(frontOrder));
        }

        [Test]
        public void Charge_HandsDoNotReplaceEachOtherAndReuseInstances()
        {
            var (avatar, state) = ChargeRig();
            state.RaiseAttack(ChargeAttack(state));
            PresentFrame(avatar);
            var main = ChargeInstance(avatar);
            state.RaiseAttack(ChargeAttack(state, true));
            PresentFrame(avatar);
            Assert.That(main.isPlaying, Is.True);
            Assert.That(ChargeInstance(avatar, true).isPlaying, Is.True);
            Assert.That(ChargeInstance(avatar), Is.SameAs(main));
            avatar.GetComponentInChildren<PlayerAttackVfxPresenter>(true).CancelAndRestore();
            Assert.That(main.isPlaying, Is.False);
            Assert.That(ChargeInstance(avatar, true).isPlaying, Is.False);
        }
    }
}
#endif

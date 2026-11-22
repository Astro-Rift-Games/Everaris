#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Tests.EditMode.Combat
{
    public sealed class MagicWeaponFeelAssignmentTests
    {
        private const string Definitions = "Assets/Scriptable Objects/Loot/Definitions/";
        private const string MagicVfx = "Assets/Prefabs/VFX/Magic/";
        private const string Palette = "Assets/Scriptable Objects/AttackVfxTintPalette.asset";

        [TestCase("MagicStaff")]
        [TestCase("MagicWand")]
        [TestCase("TrainingWand")]
        public void CastingWeapon_HasValidWindupCharge_AndStaysValid(string weapon)
        {
            var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Definitions + weapon + "WeaponDefinition.asset");
            WindupChargeVfx charge = definition.Presentation.WindupChargeVfx;
            Assert.That(charge, Is.Not.Null, weapon);
            Assert.That(charge.TryValidate(out string chargeError), Is.True, chargeError);
            Assert.That(AssetDatabase.GetAssetPath(charge.Prefab), Is.EqualTo(MagicVfx + "MagicWindupCharge.prefab"));
            Assert.That(definition.TryValidate(out string error), Is.True, error);
        }

        [TestCase("MagicSword", "MagicSwordReleaseBurst")]
        [TestCase("MagicCinquedea", "MagicCinquedeaReleaseBurst")]
        public void MagicBlade_HasReleaseBurst_AndStaysValid(string weapon, string burst)
        {
            var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Definitions + weapon + "WeaponDefinition.asset");
            Assert.That(AssetDatabase.GetAssetPath(definition.Presentation.ReleaseBurstParticles),
                Is.EqualTo(MagicVfx + burst + ".prefab"));
            Assert.That(definition.TryValidate(out string error), Is.True, error);
        }

        [TestCase("MagicWindupCharge")]
        [TestCase("MagicProjectileTrail")]
        [TestCase("MagicProjectileImpact")]
        [TestCase("MagicProjectileFizzle")]
        [TestCase("MagicSwordReleaseBurst")]
        [TestCase("MagicCinquedeaReleaseBurst")]
        public void MagicVfxPrefab_IsVisualOnly_WithOneWiredParticleDrivenLight(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MagicVfx + name + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<Fusion.NetworkObject>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Collider2D>(true), Is.Empty);
            ParticleDrivenLight2D[] lights = prefab.GetComponentsInChildren<ParticleDrivenLight2D>(true);
            Assert.That(lights, Has.Length.EqualTo(1));
            var serialized = new SerializedObject(lights[0]);
            Assert.That(serialized.FindProperty("_light").objectReferenceValue, Is.SameAs(lights[0].GetComponent<Light2D>()));
            var driver = serialized.FindProperty("_particles").objectReferenceValue as ParticleSystem;
            Assert.That(driver, Is.Not.Null);
            Assert.That(driver.transform.IsChildOf(prefab.transform), Is.True);
        }

        [Test]
        public void ProjectilePrefab_RegistersTrailsThroughItsDetacher()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MagicProjectile.prefab");
            var detacher = prefab.GetComponent<ProjectileTrailDetacher>();
            Assert.That(detacher, Is.Not.Null);
            var presenter = new SerializedObject(prefab.GetComponent<ProjectileVisualProfilePresenter>());
            Assert.That(presenter.FindProperty("_trailDetacher").objectReferenceValue, Is.SameAs(detacher));
            Assert.That(prefab.GetComponent<Fusion.NetworkObject>().NetworkedBehaviours, Does.Contain(detacher));
        }

        [Test]
        public void Player_TintsMagicImpactParticlesAndDamageNumbers()
        {
            var palette = AssetDatabase.LoadAssetAtPath<AttackVfxTintPalette>(Palette);
            Assert.That(palette, Is.Not.Null);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkPlayer.prefab");
            var combat = new SerializedObject(player.GetComponent<PlayerCombatNetworkController>());
            Assert.That(combat.FindProperty("_impactTintPalette").objectReferenceValue, Is.SameAs(palette));
            foreach (string path in new[] { "Assets/Prefabs/NetworkPlayer.prefab", "Assets/Prefabs/UI/PlayerUI/LocalGameplayHud.prefab" })
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (CombatFeedbackPresenter feedback in root.GetComponentsInChildren<CombatFeedbackPresenter>(true))
                    Assert.That(new SerializedObject(feedback).FindProperty("_tintPalette").objectReferenceValue,
                        Is.SameAs(palette), path);
            }
        }
    }
}
#endif

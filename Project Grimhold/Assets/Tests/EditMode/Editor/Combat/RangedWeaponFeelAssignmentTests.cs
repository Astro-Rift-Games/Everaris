#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Tests.EditMode.Combat
{
    public sealed class RangedWeaponFeelAssignmentTests
    {
        private const string Definitions = "Assets/Scriptable Objects/Loot/Definitions/";
        private const string RangedVfx = "Assets/Prefabs/VFX/Ranged/";
        private const string HitReactions = "Assets/Scriptable Objects/Combat/HitReactions/";

        [TestCase("LongBow", "ArrowTrail", "BowReleaseBurst", "Light")]
        [TestCase("CompoundBow", "ArrowTrail", "BowReleaseBurst", "Light")]
        [TestCase("LightCrossbow", "BoltTrail", "CrossbowReleaseBurst", "Medium")]
        public void RangedWeapon_HasTrailImpactFizzleReleaseBurstAndHitReaction_AndStaysValid(
            string weapon, string trail, string burst, string reaction)
        {
            var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Definitions + weapon + "WeaponDefinition.asset");
            ProjectileVisualProfile profile = definition.ProjectileVisual;
            Assert.That(profile, Is.Not.Null, weapon);
            Assert.That(AssetDatabase.GetAssetPath(profile.TrailPrefab), Is.EqualTo(RangedVfx + trail + ".prefab"));
            Assert.That(AssetDatabase.GetAssetPath(profile.ImpactParticles), Is.EqualTo(RangedVfx + "ArrowImpact.prefab"));
            Assert.That(AssetDatabase.GetAssetPath(profile.FizzleParticles), Is.EqualTo(RangedVfx + "ArrowFizzle.prefab"));
            Assert.That(profile.StickOnImpact, Is.True);
            Assert.That(AssetDatabase.GetAssetPath(definition.Presentation.ReleaseBurstParticles),
                Is.EqualTo(RangedVfx + burst + ".prefab"));
            Assert.That(AssetDatabase.GetAssetPath(definition.Presentation.HitReaction),
                Is.EqualTo(HitReactions + reaction + ".asset"));
            Assert.That(definition.Presentation.HitstopSeconds, Is.Zero, "Hitstop pauses a melee swing; ranged weapons have none.");
            Assert.That(definition.TryValidate(out string error), Is.True, error);
        }

        [Test]
        public void Crossbow_ShakesHarderThanTheSharedDealDamageDefault_BowsKeepTheDefault()
        {
            var config = AssetDatabase.LoadAssetAtPath<CameraShakeConfig>("Assets/Scriptable Objects/CameraShakeConfig.asset");
            WeaponDealShake crossbow = Load("LightCrossbow").Presentation.DealShake;
            Assert.That(crossbow.Enabled, Is.True);
            Assert.That(crossbow.Intensity, Is.GreaterThan(config.DealDamageIntensity));
            Assert.That(Load("LongBow").Presentation.DealShake.Enabled, Is.False);
            Assert.That(Load("CompoundBow").Presentation.DealShake.Enabled, Is.False);
        }

        [TestCase("ArrowTrail")]
        [TestCase("BoltTrail")]
        [TestCase("ArrowImpact")]
        [TestCase("ArrowFizzle")]
        [TestCase("BowReleaseBurst")]
        [TestCase("CrossbowReleaseBurst")]
        public void RangedVfxPrefab_IsVisualOnly_AndEmitsNoLight(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RangedVfx + name + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<Fusion.NetworkObject>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Collider2D>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Light2D>(true), Is.Empty, "Physical projectiles do not glow.");
        }

        private static WeaponDefinition Load(string weapon) =>
            AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Definitions + weapon + "WeaponDefinition.asset");
    }
}
#endif

#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Combat
{
    public sealed class ProjectileVisualAssignmentTests
    {
        private const string Definitions = "Assets/Scriptable Objects/Loot/Definitions/";
        private const string Art = "Assets/Art/Projectiles/New/";
        private const string Catalog = "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset";

        [TestCase("LongBow", "Arrow-V2")]
        [TestCase("CompoundBow", "Arrow-V1")]
        [TestCase("MagicStaff", "MagicMissile")]
        [TestCase("MagicWand", "MagicDart-V1")]
        [TestCase("TrainingWand", "MagicDart-V2")]
        public void Weapon_UsesAssignedPrecoloredSpriteOnlyProfile(string weapon, string art)
        {
            var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Definitions + weapon + "WeaponDefinition.asset");
            Assert.That(definition, Is.Not.Null);
            var profile = definition.ProjectileVisual;
            Assert.That(profile, Is.Not.Null, weapon);
            var sprite = AssetDatabase.LoadAllAssetsAtPath(Art + "Projectiles-" + art + ".png").OfType<Sprite>().Single();
            Assert.That(profile.Sprite, Is.SameAs(sprite));
            Assert.That(profile.AnimatorController, Is.Null);
            Assert.That(profile.UseDamageTypeTint, Is.False);
            Assert.That(profile.OverrideTint, Is.False);
            Assert.That(profile.Tint, Is.EqualTo(Color.white));
            Assert.That(profile.TrailPrefab, Is.Null);
            Assert.That(profile.ImpactParticles, Is.Null);
            Assert.That(profile.FizzleParticles, Is.Null);
        }

        [Test]
        public void Crossbow_RetainsDefaultArrowControllerWithoutProfile()
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Definitions + "LightCrossbowWeaponDefinition.asset");
            Assert.That(weapon, Is.Not.Null);
            Assert.That(weapon.ProjectileVisual, Is.Null);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MagicProjectile.prefab");
            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(animator.enabled, Is.True);
            Assert.That(AssetDatabase.GetAssetPath(animator.runtimeAnimatorController), Is.EqualTo("Assets/Animations/Projectiles/Arrow.controller"));
        }

        [TestCase("Arrow-V1")]
        [TestCase("Arrow-V2")]
        [TestCase("MagicMissile")]
        [TestCase("MagicDart-V1")]
        [TestCase("MagicDart-V2")]
        public void Sprite_UsesPixelArtImportSettingsAndCenteredFullRect(string art)
        {
            string path = Art + "Projectiles-" + art + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.maxTextureSize, Is.EqualTo(64));
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect));
            var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single();
            Assert.That(sprite.rect, Is.EqualTo(new Rect(0, 0, sprite.texture.width, sprite.texture.height)));
            Assert.That(sprite.pivot, Is.EqualTo(sprite.rect.size * .5f));
        }

        [Test]
        public void Variant_HasRootPresenterExplicitlyWiredToChildVisualsAndEquipmentCatalog()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MagicProjectile.prefab");
            var presenter = prefab.GetComponent<ProjectileVisualProfilePresenter>();
            Assert.That(presenter, Is.Not.Null);
            var serialized = new SerializedObject(presenter);
            Assert.That(serialized.FindProperty("_spriteRenderer").objectReferenceValue, Is.SameAs(prefab.GetComponentInChildren<SpriteRenderer>(true)));
            Assert.That(serialized.FindProperty("_animator").objectReferenceValue, Is.SameAs(prefab.GetComponentInChildren<Animator>(true)));
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkPlayer.prefab");
            var equipment = new SerializedObject(player.GetComponent<PlayerWeaponEquipmentNetworkController>());
            var catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(Catalog);
            Assert.That(serialized.FindProperty("_catalog").objectReferenceValue, Is.SameAs(catalog));
            Assert.That(equipment.FindProperty("_lootCatalog").objectReferenceValue, Is.SameAs(catalog));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlaceholderProjectile.prefab").GetComponent<ProjectileVisualProfilePresenter>(), Is.Null);
        }

        [Test]
        public void VisualChild_RotatesUpwardArtOntoProjectilePositiveXAxis()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MagicProjectile.prefab");
            var renderer = prefab.GetComponentInChildren<SpriteRenderer>(true);
            Vector3 forward = prefab.transform.InverseTransformDirection(renderer.transform.up);
            Assert.That(Vector3.Distance(forward, Vector3.right), Is.LessThan(.0001f));
            Assert.That(renderer.color, Is.EqualTo(Color.white));
        }
    }
}
#endif

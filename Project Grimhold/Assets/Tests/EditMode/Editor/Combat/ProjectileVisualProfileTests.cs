#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Combat
{
    public sealed class ProjectileVisualProfileTests
    {
        private readonly List<Object> _owned = new();
        private GameObject _root;
        private SpriteRenderer _renderer;
        private Animator _animator;
        private ProjectileVisualProfilePresenter _presenter;
        private ProjectileVisualProfile _profile;
        private WeaponDefinition _weapon;
        private WeaponAttackTag Tag => new(EquipmentSlot.WeaponSetAMainHand, 1, 1, AttackType.Ranged);

        [SetUp]
        public void Setup()
        {
            _root = Own(new GameObject("ProfileTest"));
            _renderer = _root.AddComponent<SpriteRenderer>();
            _renderer.color = Color.green;
            _animator = _root.AddComponent<Animator>();
            _presenter = _root.AddComponent<ProjectileVisualProfilePresenter>();
            var equipment = _root.AddComponent<PlayerWeaponEquipmentNetworkController>();
            _profile = Own(ScriptableObject.CreateInstance<ProjectileVisualProfile>());
            _weapon = Own(ScriptableObject.CreateInstance<WeaponDefinition>());
            var loot = Own(ScriptableObject.CreateInstance<LootDefinition>());
            Set(loot, "_id", "profile-test");
            Set(loot, "_weaponDefinition", _weapon);
            var catalog = Own(ScriptableObject.CreateInstance<LootDefinitionCatalog>());
            var serialized = new SerializedObject(catalog);
            var entries = serialized.FindProperty("_definitions");
            entries.arraySize = 1;
            entries.GetArrayElementAtIndex(0).objectReferenceValue = loot;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Set(equipment, "_lootCatalog", catalog);
            Set(_presenter, "_equipmentSource", equipment);
            Set(_presenter, "_spriteRenderer", _renderer);
            Set(_presenter, "_animator", _animator);
            Set(_weapon, "_projectileVisual", _profile);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (ParticleSystem particle in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include))
                if (particle.name.StartsWith("ProfileTest") && particle.name.EndsWith("(Clone)"))
                    Object.DestroyImmediate(particle.gameObject);
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        [Test]
        public void TaggedProfile_AppliesSpriteAndTint_DisablesArrowAnimator_ThenRestoresForReuse()
        {
            var texture = Own(new Texture2D(2, 2));
            var sprite = Own(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f));
            Set(_profile, "_sprite", sprite);
            Set(_profile, "_overrideTint", true);
            Set(_profile, "_tint", Color.magenta);
            _presenter.BeginFlight(Tag, DamageType.Magical);
            Assert.That(_renderer.sprite, Is.SameAs(sprite));
            Assert.That(_renderer.color, Is.EqualTo(Color.magenta));
            Assert.That(_animator.enabled, Is.False, "Arrow animation must not overwrite a profile sprite.");
            _presenter.RestoreDefaults();
            Assert.That(_renderer.sprite, Is.Null);
            Assert.That(_renderer.color, Is.EqualTo(Color.green));
            Assert.That(_animator.enabled, Is.True);
            _presenter.BeginFlight(default, DamageType.Physical);
            Assert.That(_renderer.color, Is.EqualTo(Color.green));
        }

        [Test]
        public void TaggedProfile_UsesOwnAnimator_AndRestoresOriginalController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Projectiles/Arrow.controller");
            Assert.That(controller, Is.Not.Null);
            Set(_profile, "_animatorController", controller);
            _presenter.BeginFlight(Tag, DamageType.Magical);
            Assert.That(_animator.runtimeAnimatorController, Is.SameAs(controller));
            Assert.That(_animator.enabled, Is.True);
            _presenter.RestoreDefaults();
            Assert.That(_animator.runtimeAnimatorController, Is.Null);
        }

        [TestCase(0, true)] [TestCase(1, false)] [TestCase(99, true)]
        public void MissingTagProfileOrCatalogEntry_LeavesDefaults(int index, bool profile)
        {
            if (!profile) Set(_weapon, "_projectileVisual", null);
            Set(_profile, "_overrideTint", true);
            Set(_profile, "_tint", Color.red);
            _presenter.BeginFlight(new WeaponAttackTag(EquipmentSlot.WeaponSetAMainHand, index == 0 ? 0 : 1, index, AttackType.Ranged), DamageType.Magical);
            Assert.That(_renderer.color, Is.EqualTo(Color.green));
            Assert.That(_animator.enabled, Is.True);
        }

        [Test]
        public void ProfileImpactAndExpiry_AreSeparateOneShots_AndSupersedeDefaults()
        {
            var impact = Particles("ProfileTestImpact");
            var fizzle = Particles("ProfileTestFizzle");
            Set(_profile, "_impactParticles", impact);
            Set(_profile, "_fizzleParticles", fizzle);
            _presenter.BeginFlight(Tag, DamageType.Magical);
            Assert.That(_presenter.HasImpactOverride, Is.True);
            Assert.That(_presenter.HasFizzleOverride, Is.True);
            var defaults = _root.AddComponent<ProjectileImpactParticlePresenter>();
            Set(defaults, "_impactParticles", Particles("ProfileTestDefault"));
            defaults.PresentImpact(new Vector2(2, 3), false);
            _presenter.PresentImpact(new Vector2(2, 3), false);
            Assert.That(Count("ProfileTestDefault(Clone)"), Is.Zero);
            Assert.That(Count("ProfileTestImpact(Clone)"), Is.EqualTo(1));
            Assert.That(Count("ProfileTestFizzle(Clone)"), Is.Zero);
            _presenter.PresentExpiry(Vector2.zero, Vector2.right);
            Assert.That(Count("ProfileTestFizzle(Clone)"), Is.EqualTo(1));
            foreach (ParticleSystem clone in Object.FindObjectsByType<ParticleSystem>())
                if (clone.name == "ProfileTestImpact(Clone)" || clone.name == "ProfileTestFizzle(Clone)")
                {
                    Assert.That(clone.main.loop, Is.False);
                    Assert.That(clone.main.stopAction, Is.EqualTo(ParticleSystemStopAction.Destroy));
                }
            Assert.That(impact.main.loop, Is.True, "Profile templates must not be mutated.");
            Assert.That(fizzle.main.loop, Is.True);
            _presenter.RestoreDefaults();
            Assert.That(_presenter.HasImpactOverride, Is.False);
            Assert.That(_presenter.HasFizzleOverride, Is.False);
        }

        [TestCase(false)] [TestCase(true)]
        public void DefaultImpact_EmitsOnCharacterAndGeometry_NullPaletteKeepsColor(bool geometry)
        {
            var presenter = _root.AddComponent<ProjectileImpactParticlePresenter>();
            var particles = Particles("ProfileTestDefault");
            Set(presenter, "_impactParticles", particles);
            presenter.PresentImpact(Vector2.one, geometry);
            Assert.That(Count("ProfileTestDefault(Clone)"), Is.EqualTo(1));
            foreach (ParticleSystem clone in Object.FindObjectsByType<ParticleSystem>())
                if (clone.name == "ProfileTestDefault(Clone)")
                {
                    Assert.That(clone.main.startColor.color, Is.EqualTo(Color.green));
                    Assert.That((clone.main.loop, clone.main.stopAction),
                        Is.EqualTo((particles.main.loop, particles.main.stopAction)),
                        "Default impact looping and stop action must remain authored.");
                }
        }

        [Test]
        public void DamageTypePalette_TintsProfileAndCharacterImpact_WithoutMutatingTemplate()
        {
            var palette = AssetDatabase.LoadAssetAtPath<AttackVfxTintPalette>("Assets/Scriptable Objects/AttackVfxTintPalette.asset");
            Assert.That(palette, Is.Not.Null);
            Assert.That(palette.TryGetTint(DamageType.Magical, out Color expected), Is.True);
            Set(_presenter, "_tintPalette", palette);
            Set(_profile, "_useDamageTypeTint", true);
            _presenter.BeginFlight(Tag, DamageType.Magical);
            Assert.That(_renderer.color, Is.EqualTo(expected));
            var defaults = _root.AddComponent<ProjectileImpactParticlePresenter>();
            var particles = Particles("ProfileTestTint");
            Set(defaults, "_impactParticles", particles);
            Set(defaults, "_tintPalette", palette);
            defaults.BeginFlight(DamageType.Magical);
            defaults.PresentImpact(Vector2.one, false);
            Assert.That(Count("ProfileTestTint(Clone)"), Is.EqualTo(1));
            foreach (ParticleSystem clone in Object.FindObjectsByType<ParticleSystem>())
                if (clone.name == "ProfileTestTint(Clone)")
                {
                    Assert.That(clone.main.startColor.color, Is.EqualTo(expected));
                    Assert.That((clone.main.loop, clone.main.stopAction),
                        Is.EqualTo((particles.main.loop, particles.main.stopAction)));
                }
            Assert.That(particles.main.startColor.color, Is.EqualTo(Color.green));
        }

        [Test]
        public void PrefabCatalogReference_ResolvesWithoutALiveEquipmentInstance()
        {
            var equipment = _root.GetComponent<PlayerWeaponEquipmentNetworkController>();
            var serialized = new SerializedObject(equipment);
            Set(_presenter, "_catalog", serialized.FindProperty("_lootCatalog").objectReferenceValue);
            Set(_presenter, "_equipmentSource", null);
            Set(_profile, "_overrideTint", true);
            Set(_profile, "_tint", Color.magenta);
            _presenter.BeginFlight(Tag, DamageType.Magical);
            Assert.That(_renderer.color, Is.EqualTo(Color.magenta));
        }

        [Test]
        public void ProfileTrail_IsAChild_AndRegisteredWithDetacher()
        {
            var detacher = _root.AddComponent<ProjectileTrailDetacher>();
            Set(_presenter, "_trailDetacher", detacher);
            var trail = Own(new GameObject("ProfileTestTrail"));
            trail.AddComponent<TrailRenderer>();
            Set(_profile, "_trailPrefab", trail);
            _presenter.BeginFlight(Tag, DamageType.Magical);
            Assert.That(_root.transform.Find("ProfileTestTrail(Clone)"), Is.Not.Null);
        }

        private ParticleSystem Particles(string name)
        {
            var particles = Own(new GameObject(name)).AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.startColor = Color.green;
            return particles;
        }
        private static int Count(string name)
        {
            int count = 0;
            foreach (ParticleSystem particles in Object.FindObjectsByType<ParticleSystem>()) if (particles.name == name) count++;
            return count;
        }
        private T Own<T>(T item) where T : Object { _owned.Add(item); return item; }
        private static void Set(Object target, string field, object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            Assert.That(property, Is.Not.Null, field);
            if (value is bool boolean) property.boolValue = boolean;
            else if (value is Color color) property.colorValue = color;
            else if (value is string text) property.stringValue = text;
            else property.objectReferenceValue = value as Object;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif

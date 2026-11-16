#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.Audio
{
    public sealed class WeaponAudioPresenterTests
    {
        private readonly List<Object> _owned = new();
        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }
        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        [TearDown]
        public void Cleanup()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        [TestCase(AttackType.Ranged, true, true)]
        [TestCase(AttackType.Ranged, true)]
        [TestCase(AttackType.Melee, true)]
        [TestCase(AttackType.Ranged, false)]
        public void Impact_PlaysConfirmedWeaponOrUntaggedDefault(AttackType type, bool tagged, bool destroyedConfig = false)
        {
            var manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            AudioClip confirmed = Own(AudioClip.Create("confirmed", 44100, 1, 44100, false));
            AudioClip generic = Own(AudioClip.Create("generic", 44100, 1, 44100, false));
            WeaponAudioConfig weaponConfig = Config(confirmed);
            WeaponAudioConfig fallback = Config(generic);
            var original = AssetDatabase.LoadAssetAtPath<LootDefinition>("Assets/Scriptable Objects/Loot/Definitions/ArmingSword.asset");
            var weapon = Own(Object.Instantiate(original.WeaponDefinition));
            var loot = Own(Object.Instantiate(original));
            Set(weapon, "_audioConfig", weaponConfig);
            Set(loot, "_weaponDefinition", weapon);
            var catalog = Own(ScriptableObject.CreateInstance<LootDefinitionCatalog>());
            Set(catalog, "_definitions", new List<LootDefinition> { loot });
            var owner = Own(new GameObject("Audio owner"));
            var equipment = owner.AddComponent<PlayerWeaponEquipmentNetworkController>();
            Set(equipment, "_lootCatalog", catalog);
            var presenter = owner.AddComponent<WeaponAudioPresenter>();
            Set(presenter, "_fallbackAudioConfig", fallback);
            Assert.That(catalog.TryGetIndex(loot.LootId, out int index), Is.True);
            var tag = tagged ? new WeaponAttackTag(EquipmentSlot.WeaponSetAMainHand, 1, index + 1, type) : default;
            var feedback = new CombatPresentationEvent(1, CombatFeedbackKind.ConfirmedImpact,
                new EntityId(99), new Vector2(2, 3), 1f, 10, AttackFailureReason.None, tag);
            if (destroyedConfig) Object.DestroyImmediate(weaponConfig);
            presenter.SendMessage("OnCombatFeedbackResolved", feedback);
            AudioSource played = null;
            foreach (var source in manager.GetComponentsInChildren<AudioSource>())
                if (source.clip != null) played = source;
            Assert.That(played, Is.Not.Null);
            Assert.That(played.clip, Is.SameAs(tagged && !destroyedConfig ? confirmed : generic), "Impact must use its confirmed identity, not current equipment.");
            Assert.That(played.transform.position, Is.EqualTo(new Vector3(2, 3, 0)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MissingOptionalKeys_AreQuietAndDoNotPlayAudio()
        {
            var presenter = Own(new GameObject("Unbound audio")).AddComponent<WeaponAudioPresenter>();
            var manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            Set(presenter, "_fallbackAudioConfig", Own(ScriptableObject.CreateInstance<WeaponAudioConfig>()));
            presenter.SendMessage("OnCombatFeedbackResolved", new CombatPresentationEvent(1, CombatFeedbackKind.ConfirmedImpact,
                new EntityId(99), Vector2.zero, 1f, 10, AttackFailureReason.None));
            foreach (var source in manager.GetComponentsInChildren<AudioSource>()) Assert.That(source.clip, Is.Null);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Release_CastOverridesShoot_OrKeepsLegacyShoot(bool hasCast)
        {
            var manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            var owner = Own(new GameObject("Audio owner"));
            var presenter = owner.AddComponent<WeaponAudioPresenter>();
            var shoot = Own(AudioClip.Create("shoot", 44100, 1, 44100, false));
            var cast = Own(AudioClip.Create("cast", 44100, 1, 44100, false));
            var config = KeyConfig(("Shoot", shoot));
            if (hasCast) config = KeyConfig(("Shoot", shoot), ("cAsT", cast));
            Set(presenter, "_fallbackAudioConfig", config);
            presenter.SendMessage("OnAttackPerformed", new AttackPerformedEvent(new EntityId(1),
                AttackType.Ranged, Vector2.zero, Vector2.right, 1));
            AudioSource played = null;
            foreach (var source in manager.GetComponentsInChildren<AudioSource>())
                if (source.clip != null) played = source;
            Assert.That(played, Is.Not.Null);
            Assert.That(played.clip, Is.SameAs(hasCast ? cast : shoot));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Charge_AttachedPerHand_StopsOnReleaseOrDisable(bool disable)
        {
            Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            var owner = Own(new GameObject("Audio owner"));
            var presenter = owner.AddComponent<WeaponAudioPresenter>();
            var charge = Own(AudioClip.Create("charge", 441000, 1, 44100, false));
            Set(presenter, "_fallbackAudioConfig", KeyConfig(("Charge", charge)));
            var main = new AttackPerformedEvent(new EntityId(1), AttackType.Ranged, Vector2.zero,
                Vector2.right, 10, 1, 0, 20, 0.5f, sourceSlot: EquipmentSlot.WeaponSetAMainHand);
            var off = new AttackPerformedEvent(new EntityId(1), AttackType.Melee, Vector2.zero,
                Vector2.right, 10, 1, 0, 20, 0.5f, sourceSlot: EquipmentSlot.WeaponSetAOffHand);
            presenter.SendMessage("OnAttackPerformed", main);
            presenter.SendMessage("OnAttackPerformed", off);
            var sources = owner.GetComponentsInChildren<AudioSource>();
            Assert.That(sources.Length, Is.EqualTo(2), "Each hand needs its own stoppable Charge source.");
            foreach (var source in sources) Assert.That(source.isPlaying, Is.True);
            presenter.SendMessage("OnAttackPerformed", main);
            Assert.That(owner.GetComponentsInChildren<AudioSource>().Length, Is.EqualTo(2));
            if (disable) presenter.enabled = false;
            else typeof(WeaponAudioPresenter).GetMethod("PlayRelease", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(presenter, new object[] { main });
            Assert.That(sources[0].isPlaying, Is.False, "Charge is cut at release/disable.");
            Assert.That(sources[1].isPlaying, Is.EqualTo(!disable), "Other hand remains independent.");
            presenter.enabled = false;
            presenter.enabled = true;
            presenter.SendMessage("OnAttackPresentationResumed", main);
            presenter.SendMessage("OnAttackPerformed", main);
            foreach (var source in sources) Assert.That(source.isPlaying, Is.False, "Resume must remain silent.");
        }

        private WeaponAudioConfig KeyConfig(params (string key, AudioClip clip)[] entries)
        {
            var config = Own(ScriptableObject.CreateInstance<WeaponAudioConfig>());
            var list = new List<AudioEntry>();
            foreach (var entry in entries)
            {
                object custom = new CustomClip();
                typeof(CustomClip).GetField("_clips", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(custom, new[] { entry.clip });
                list.Add(new AudioEntry { Key = entry.key, Clip = (CustomClip)custom });
            }
            typeof(EntityAudioConfig).GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(config, list);
            typeof(EntityAudioConfig).GetField("_clipDictionary", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(config, null);
            return config;
        }

        private WeaponAudioConfig Config(AudioClip clip)
        {
            var config = Own(ScriptableObject.CreateInstance<WeaponAudioConfig>());
            object custom = new CustomClip();
            typeof(CustomClip).GetField("_clips", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(custom, new[] { clip });
            typeof(EntityAudioConfig).GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(config,
                new List<AudioEntry> { new() { Key = "Attack", Clip = (CustomClip)custom }, new() { Key = "Block", Clip = (CustomClip)custom } });
            typeof(EntityAudioConfig).GetField("_clipDictionary", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(config, null);
            return config;
        }
    }
}
#endif

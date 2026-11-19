#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.Audio
{
    public sealed class WeaponAudioPresenterTests
    {
        private readonly List<Object> _owned = new();
        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }
        private static void Set(Object target, string field, Object value) => AudioTestKit.SetReference(target, field, value);

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
            var weapon = Own(ScriptableObject.CreateInstance<WeaponDefinition>());
            var loot = Own(ScriptableObject.CreateInstance<LootDefinition>());
            Set(weapon, "_audioConfig", weaponConfig);
            AudioTestKit.SetString(loot, "_id", "TEST_AUDIO_WEAPON");
            Set(loot, "_weaponDefinition", weapon);
            var catalog = Own(ScriptableObject.CreateInstance<LootDefinitionCatalog>());
            AudioTestKit.SetReferences(catalog, "_definitions", new Object[] { loot });
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
            presenter.OnCombatFeedbackResolved(feedback);
            AudioSource played = null;
            foreach (var source in manager.GetComponentsInChildren<AudioSource>())
                if (source.clip != null) played = source;
            Assert.That(played, Is.Not.Null);
            Assert.That(played.clip, Is.SameAs(tagged && !destroyedConfig ? confirmed : generic), "Impact must use its confirmed identity, not current equipment.");
            Assert.That(played.transform.position, Is.EqualTo(new Vector3(2, 3, 0)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Impact_RepeatedDeliveryOfSameSequence_PlaysOnce_AndNewSequencePlaysAgain()
        {
            var manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            AudioClip impact = Own(AudioClip.Create("impact", 44100, 1, 44100, false));
            var presenter = Own(new GameObject("Audio owner")).AddComponent<WeaponAudioPresenter>();
            Set(presenter, "_fallbackAudioConfig", Config(impact));
            int Playing()
            {
                int count = 0;
                foreach (var source in manager.GetComponentsInChildren<AudioSource>())
                    if (source.isPlaying && source.clip == impact) count++;
                return count;
            }
            CombatPresentationEvent Feedback(int sequence) => new(sequence, CombatFeedbackKind.ConfirmedImpact,
                new EntityId(99), Vector2.zero, 1f, 10, AttackFailureReason.None);

            presenter.OnCombatFeedbackResolved(Feedback(5));
            Assert.That(Playing(), Is.EqualTo(1));
            presenter.OnCombatFeedbackResolved(Feedback(5));
            Assert.That(Playing(), Is.EqualTo(1), "A repeated delivery of the same feedback must stay silent.");
            presenter.OnCombatFeedbackResolved(Feedback(6));
            Assert.That(Playing(), Is.EqualTo(2), "The next feedback sequence still plays.");
            presenter.OnCombatFeedbackResolved(Feedback(5));
            Assert.That(Playing(), Is.EqualTo(2), "An older sequence delivered late must stay silent.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Impact_AfterDisableEnable_SameSequencePlaysAgain()
        {
            var manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            AudioClip impact = Own(AudioClip.Create("impact", 44100, 1, 44100, false));
            var presenter = Own(new GameObject("Audio owner")).AddComponent<WeaponAudioPresenter>();
            Set(presenter, "_fallbackAudioConfig", Config(impact));
            int Playing()
            {
                int count = 0;
                foreach (var source in manager.GetComponentsInChildren<AudioSource>())
                    if (source.isPlaying && source.clip == impact) count++;
                return count;
            }
            var feedback = new CombatPresentationEvent(7, CombatFeedbackKind.ConfirmedImpact,
                new EntityId(99), Vector2.zero, 1f, 10, AttackFailureReason.None);

            presenter.OnCombatFeedbackResolved(feedback);
            presenter.OnCombatFeedbackResolved(feedback);
            Assert.That(Playing(), Is.EqualTo(1), "Same-instance repeat stays silent.");
            presenter.enabled = false;
            presenter.enabled = true;
            presenter.OnCombatFeedbackResolved(feedback);
            Assert.That(Playing(), Is.EqualTo(2), "A re-enabled presenter starts a fresh sequence history.");
        }

        [Test]
        public void Impact_IgnoredFeedbackKinds_DoNotConsumeTheSequence()
        {
            var manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            AudioClip impact = Own(AudioClip.Create("impact", 44100, 1, 44100, false));
            var presenter = Own(new GameObject("Audio owner")).AddComponent<WeaponAudioPresenter>();
            Set(presenter, "_fallbackAudioConfig", Config(impact));
            presenter.OnCombatFeedbackResolved(new CombatPresentationEvent(3, CombatFeedbackKind.AttackRejected,
                new EntityId(99), Vector2.zero, 0f, 10, AttackFailureReason.None));
            presenter.OnCombatFeedbackResolved(new CombatPresentationEvent(3, CombatFeedbackKind.ConfirmedImpact,
                new EntityId(99), Vector2.zero, 1f, 10, AttackFailureReason.None));
            int playing = 0;
            foreach (var source in manager.GetComponentsInChildren<AudioSource>())
                if (source.isPlaying && source.clip == impact) playing++;
            Assert.That(playing, Is.EqualTo(1));
        }

        [Test]
        public void MissingOptionalKeys_AreQuietAndDoNotPlayAudio()
        {
            var presenter = Own(new GameObject("Unbound audio")).AddComponent<WeaponAudioPresenter>();
            var manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            var block = Own(AudioClip.Create("block", 44100, 1, 44100, false));
            Set(presenter, "_fallbackAudioConfig", Own(ScriptableObject.CreateInstance<WeaponAudioConfig>()));
            presenter.OnCombatFeedbackResolved(new CombatPresentationEvent(1, CombatFeedbackKind.ConfirmedImpact,
                new EntityId(99), Vector2.zero, 1f, 10, AttackFailureReason.None));
            Assert.That(AudioTestKit.ClipsUsedBy(manager), Is.Empty, "An empty config has no key to play.");
            LogAssert.NoUnexpectedReceived();

            // Positive guard: the very same delivery path plays as soon as the key exists, so the silence above is
            // caused by the missing key and not by the event being dropped before it reaches the config.
            Set(presenter, "_fallbackAudioConfig", KeyConfig(("Block", block)));
            presenter.OnCombatFeedbackResolved(new CombatPresentationEvent(2, CombatFeedbackKind.ConfirmedImpact,
                new EntityId(99), Vector2.zero, 1f, 10, AttackFailureReason.None));
            Assert.That(AudioTestKit.ClipsUsedBy(manager), Is.EqualTo(new[] { block }));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Release_CastOverridesShoot_OrKeepsLegacyShoot(bool hasCast)
        {
            var manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            var owner = Own(new GameObject("Audio owner"));
            var combat = owner.AddComponent<AudioTestKit.StubCombatController>();
            var presenter = owner.AddComponent<WeaponAudioPresenter>();
            var shoot = Own(AudioClip.Create("shoot", 44100, 1, 44100, false));
            var cast = Own(AudioClip.Create("cast", 44100, 1, 44100, false));
            var config = KeyConfig(("Shoot", shoot));
            if (hasCast) config = KeyConfig(("Shoot", shoot), ("cAsT", cast));
            Set(presenter, "_fallbackAudioConfig", config);
            combat.Raise(new AttackPerformedEvent(new EntityId(1), AttackType.Ranged, Vector2.zero, Vector2.right, 1));
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
            var combat = owner.AddComponent<AudioTestKit.StubCombatController>();
            var presenter = owner.AddComponent<WeaponAudioPresenter>();
            var charge = Own(AudioClip.Create("charge", 441000, 1, 44100, false));
            Set(presenter, "_fallbackAudioConfig", KeyConfig(("Charge", charge)));
            var main = new AttackPerformedEvent(new EntityId(1), AttackType.Ranged, Vector2.zero,
                Vector2.right, 10, 1, 0, 20, 0.5f, sourceSlot: EquipmentSlot.WeaponSetAMainHand);
            var off = new AttackPerformedEvent(new EntityId(1), AttackType.Melee, Vector2.zero,
                Vector2.right, 10, 1, 0, 20, 0.5f, sourceSlot: EquipmentSlot.WeaponSetAOffHand);
            combat.Raise(main);
            combat.Raise(off);
            var sources = owner.GetComponentsInChildren<AudioSource>();
            Assert.That(sources.Length, Is.EqualTo(2), "Each hand needs its own stoppable Charge source.");
            foreach (var source in sources) Assert.That(source.isPlaying, Is.True);
            combat.Raise(main);
            Assert.That(owner.GetComponentsInChildren<AudioSource>().Length, Is.EqualTo(2));
            if (disable) presenter.enabled = false;
            else presenter.PlayRelease(main);
            Assert.That(sources[0].isPlaying, Is.False, "Charge is cut at release/disable.");
            Assert.That(sources[1].isPlaying, Is.EqualTo(!disable), "Other hand remains independent.");
            presenter.enabled = false;
            presenter.enabled = true;
            presenter.OnAttackPresentationResumed(main);
            combat.Raise(main);
            foreach (var source in sources) Assert.That(source.isPlaying, Is.False, "Resume must remain silent.");
        }

        private static ObservedImpactSnapshot Observed(int sequence, int weaponIndexPlusOne = 0, Vector2 hitPoint = default,
            int targetId = 99) => new()
        {
            Sequence = sequence, TargetIdValue = targetId, HitPoint = hitPoint, WeaponCatalogIndexPlusOne = weaponIndexPlusOne
        };

        [Test]
        public void ObservedImpact_PlaysOneWorldSoundPerNewSequence_AtTheImpactPoint_AndIgnoresRepeats()
        {
            var manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            AudioClip impact = Own(AudioTestKit.Clip("observed impact"));
            var presenter = Own(new GameObject("Audio owner")).AddComponent<WeaponAudioPresenter>();
            Set(presenter, "_fallbackAudioConfig", KeyConfig(("Block", impact)));

            presenter.OnObservedImpactPresented(Observed(5, hitPoint: new Vector2(2f, 3f)));
            Assert.That(AudioTestKit.PlayingCount(manager, impact), Is.EqualTo(1));
            AudioSource played = null;
            foreach (var source in manager.GetComponentsInChildren<AudioSource>())
                if (source.isPlaying && source.clip == impact) played = source;
            Assert.That(played.transform.position, Is.EqualTo(new Vector3(2f, 3f, 0f)), "The cue sits at the replicated impact point.");
            Assert.That(played.spatialBlend, Is.EqualTo(1f), "An observed impact is a world sound (3D), whatever the clip's own blend.");

            presenter.OnObservedImpactPresented(Observed(5, hitPoint: new Vector2(2f, 3f)));
            Assert.That(AudioTestKit.PlayingCount(manager, impact), Is.EqualTo(1), "A repeated sequence stays silent.");
            presenter.OnObservedImpactPresented(Observed(6));
            Assert.That(AudioTestKit.PlayingCount(manager, impact), Is.EqualTo(2), "The next sequence plays.");
            presenter.OnObservedImpactPresented(Observed(4));
            Assert.That(AudioTestKit.PlayingCount(manager, impact), Is.EqualTo(2), "An older sequence stays silent.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ObservedImpact_HasItsOwnSequenceHistory_IndependentOfTheAttackerFeedbackPath()
        {
            var manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            AudioClip impact = Own(AudioTestKit.Clip("impact"));
            var presenter = Own(new GameObject("Audio owner")).AddComponent<WeaponAudioPresenter>();
            Set(presenter, "_fallbackAudioConfig", KeyConfig(("Block", impact)));

            presenter.OnCombatFeedbackResolved(new CombatPresentationEvent(9, CombatFeedbackKind.ConfirmedImpact,
                new EntityId(99), Vector2.zero, 1f, 10, AttackFailureReason.None));
            presenter.OnObservedImpactPresented(Observed(1));

            Assert.That(AudioTestKit.PlayingCount(manager, impact), Is.EqualTo(2),
                "Each path dedupes by its own history; the attacker path never suppresses an observed impact.");
        }

        [Test]
        public void ObservedImpact_AfterDisableEnable_StartsAFreshHistory()
        {
            var manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            AudioClip impact = Own(AudioTestKit.Clip("impact"));
            var presenter = Own(new GameObject("Audio owner")).AddComponent<WeaponAudioPresenter>();
            Set(presenter, "_fallbackAudioConfig", KeyConfig(("Block", impact)));

            presenter.OnObservedImpactPresented(Observed(3));
            presenter.OnObservedImpactPresented(Observed(3));
            Assert.That(AudioTestKit.PlayingCount(manager, impact), Is.EqualTo(1));
            presenter.enabled = false;
            presenter.enabled = true;
            presenter.OnObservedImpactPresented(Observed(3));
            Assert.That(AudioTestKit.PlayingCount(manager, impact), Is.EqualTo(2), "A re-used presenter starts a fresh observed history.");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ObservedImpact_ResolvesTheConfirmedWeaponConfig_OrTheGenericFallback(bool tagged)
        {
            var manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            AudioClip confirmed = Own(AudioTestKit.Clip("confirmed"));
            AudioClip generic = Own(AudioTestKit.Clip("generic"));
            var weapon = Own(ScriptableObject.CreateInstance<WeaponDefinition>());
            var loot = Own(ScriptableObject.CreateInstance<LootDefinition>());
            Set(weapon, "_audioConfig", Config(confirmed));
            AudioTestKit.SetString(loot, "_id", "TEST_AUDIO_WEAPON");
            Set(loot, "_weaponDefinition", weapon);
            var catalog = Own(ScriptableObject.CreateInstance<LootDefinitionCatalog>());
            AudioTestKit.SetReferences(catalog, "_definitions", new Object[] { loot });
            var owner = Own(new GameObject("Audio owner"));
            Set(owner.AddComponent<PlayerWeaponEquipmentNetworkController>(), "_lootCatalog", catalog);
            var presenter = owner.AddComponent<WeaponAudioPresenter>();
            Set(presenter, "_fallbackAudioConfig", Config(generic));
            Assert.That(catalog.TryGetIndex(loot.LootId, out int index), Is.True);

            presenter.OnObservedImpactPresented(Observed(1, tagged ? index + 1 : 0));

            Assert.That(AudioTestKit.PlayingCount(manager, tagged ? confirmed : generic), Is.EqualTo(1));
            Assert.That(AudioTestKit.PlayingCount(manager, tagged ? generic : confirmed), Is.Zero,
                "The impact uses its confirmed weapon identity, never the observer's own equipment.");
        }

        private WeaponAudioConfig KeyConfig(params (string key, AudioClip clip)[] entries) =>
            Own(AudioTestKit.Config<WeaponAudioConfig>(entries));

        private WeaponAudioConfig Config(AudioClip clip) => KeyConfig(("Attack", clip), ("Block", clip));
    }
}
#endif

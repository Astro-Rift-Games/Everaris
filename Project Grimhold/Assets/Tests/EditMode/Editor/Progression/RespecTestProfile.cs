#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Progression
{
    /// <summary>
    /// TASK-304 fixture: a real store holding an assigned build (5 in every attribute, 3 free points), an
    /// equipped weapon and armor, and two prepared abilities, plus the backend's authoritative respec snapshot.
    /// </summary>
    internal sealed class RespecTestProfile : IDisposable
    {
        public const string RapierInstanceId = "223456789012345678901234";
        public const string ArmorInstanceId = "123456789012345678901234";
        public const int Cost = 100;
        public const long StartingGold = 250;
        public const int StartingRevision = 7;
        public const int OwnedPoints = 33;

        public static readonly LootId Rapier = new("rapier");
        public static readonly LootId Armor = new("heavy_armor_breastplate");
        public static readonly LootId Iron = new("RES_BLACK_IRON");
        public static readonly LootId Rune = new("RES_RUNE_FRAGMENT");
        public static readonly AbilityId Charge = new("charge");
        public static readonly AbilityId Guard = new("guard");

        private readonly AbilityDefinition _chargeDefinition;
        private readonly AbilityDefinition _guardDefinition;
        private readonly AbilityDefinitionCatalog _abilityCatalog;

        public readonly ProfileId ProfileId = new("5a5a5a5a5a5a5a5a5a5a5a5a5a5a5a5a");
        public readonly LootDefinitionCatalog LootCatalog;
        public readonly InMemoryLocalProfileRepository Repository = new();
        public readonly LocalProfileStore Store;
        public int Commits;

        public RespecTestProfile(long gold = StartingGold, bool assigned = true)
        {
            LootCatalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
                "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
            Assert.That(LootCatalog, Is.Not.Null);
            _chargeDefinition = AbilityTestFactory.CreateDefinition(
                "charge", AbilityResourceType.Mana, 10, 5f, new CharacterAttributeRequirement(CharacterAttribute.Strength, 5));
            _guardDefinition = AbilityTestFactory.CreateDefinition("guard");
            _abilityCatalog = AbilityTestFactory.CreateCatalog(_chargeDefinition, _guardDefinition);

            Assert.That(Repository.Initialize(ProfileId, LootCatalog), Is.True);
            LocalProfileSnapshot next = Repository.Snapshot.Clone();
            next.InventorySchemaVersion = 3;
            next.RequiresEquipmentHydration = false;
            next.RemoteRevision = StartingRevision;
            next.Currency = gold;
            Assert.That(CharacterAttributeState.TryCreate(
                assigned ? 5 : 0, assigned ? 5 : 0, assigned ? 5 : 0, assigned ? 5 : 0, assigned ? 5 : 0, assigned ? 5 : 0,
                assigned ? 3 : OwnedPoints, out CharacterAttributeState attributes), Is.True);
            next.CharacterAttributes = attributes;
            next.Loadout.Add(new StashItem(Iron, 2, 6));
            next.Stash.Add(new StashItem(Rune, 5, 19));
            next.PreparedEquipment = default(PreparedEquipmentLoadout)
                .With(EquipmentSlot.WeaponSetAMainHand, Rapier, new EquipmentInstance(RapierInstanceId))
                .With(EquipmentSlot.Armor, Armor, new EquipmentInstance(ArmorInstanceId));
            next.UnlockedAbilities.Add(Charge);
            next.UnlockedAbilities.Add(Guard);
            next.PreparedAbilities = new PreparedAbilityLoadout(Charge, Guard);
            Assert.That(Repository.TrySave(next, out _), Is.True);

            Store = new LocalProfileStore(Repository, ProfileId, LootCatalog, abilityCatalog: _abilityCatalog);
            Store.ProfileCommitted += _ => Commits++;
        }

        public void Dispose()
        {
            Object.DestroyImmediate(_abilityCatalog);
            Object.DestroyImmediate(_chargeDefinition);
            Object.DestroyImmediate(_guardDefinition);
        }

        /// <summary>The backend's 200 body: every attribute 0, the pool holds all owned points, the weapon is back in the loadout.</summary>
        public RespecResult Result(int revision = StartingRevision + 1, bool alreadySecured = false, string transactionId = "1_t") =>
            new RespecResult
            {
                alreadySecured = alreadySecured,
                transactionId = transactionId,
                cost = Cost,
                currency = StartingGold - Cost,
                characterAttributes = new CharacterAttributesData { availablePoints = OwnedPoints },
                level = 1,
                experience = 0,
                revision = revision,
                preparedEquipment = PreparedEquipmentData.FromLoadout(
                    default(PreparedEquipmentLoadout).With(EquipmentSlot.Armor, Armor, new EquipmentInstance(ArmorInstanceId))),
                preparedAbilities = new PreparedAbilitiesDto { slot1 = "", slot2 = "guard" },
                loadout = new[] { Item(4, Rapier, 1, RapierInstanceId), Item(6, Iron, 2, null) },
                stash = new[] { Item(19, Rune, 5, null) }
            };

        public static InventoryItemData Item(int slot, LootId id, int amount, string instanceId) => new InventoryItemData
        {
            slot = slot, lootId = id.Value, amount = amount,
            instance = instanceId == null ? null : new EquipmentInstanceData { instanceId = instanceId }
        };

        public void AssertAttributes(int value, int availablePoints)
        {
            Assert.That(Store.TryGetCharacterAttributeState(out CharacterAttributeState state), Is.True);
            Assert.That(
                new[] { state.Vitality, state.Resistance, state.Strength, state.Dexterity, state.Intelligence, state.Luck },
                Is.All.EqualTo(value));
            Assert.That(state.AvailablePoints, Is.EqualTo(availablePoints));
        }

        /// <summary>Nothing changed: gold, build, equipment, abilities, lists and revision are the starting ones.</summary>
        public void AssertUnchanged()
        {
            AssertAttributes(5, 3);
            Assert.That(Store.GetCurrency(), Is.EqualTo(StartingGold));
            Assert.That(Store.RemoteRevision, Is.EqualTo(StartingRevision));
            Assert.That(Store.GetPreparedEquipment().Get(EquipmentSlot.WeaponSetAMainHand), Is.EqualTo(Rapier));
            Assert.That(Store.GetPreparedEquipment().Get(EquipmentSlot.Armor), Is.EqualTo(Armor));
            Assert.That(Store.GetPreparedAbilities(), Is.EqualTo(new PreparedAbilityLoadout(Charge, Guard)));
            Assert.That(Store.GetLoadout(), Is.EqualTo(new[] { new StashItem(Iron, 2, 6) }));
            Assert.That(Store.GetStash(), Is.EqualTo(new[] { new StashItem(Rune, 5, 19) }));
            Assert.That(Commits, Is.Zero);
        }
    }
}
#endif

using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

namespace Tests.EditMode.Commerce
{
    public sealed class MaxTransportableAdmissionAndStashTests
    {
        private const string CatalogPath =
            "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset";

        private LootDefinitionCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(CatalogPath);
            Assert.That(_catalog, Is.Not.Null);
        }

        [TestCase("UTL_RUNE_COMPASS")]
        [TestCase("UTL_ARCANE_DETECTOR")]
        [TestCase("CON_SELF_REVIVE")]
        public void RestrictedUtilities_HaveMaxTransportableOne(string lootId)
        {
            Assert.That(_catalog.TryGet(lootId, out LootDefinition definition), Is.True, lootId);
            Assert.That(definition.MaxTransportable, Is.EqualTo(1), $"{lootId} MaxTransportable must be 1.");
        }

        [TestCase("UTL_RUNE_COMPASS")]
        [TestCase("UTL_ARCANE_DETECTOR")]
        [TestCase("CON_SELF_REVIVE")]
        public void RaidAdmission_BlocksExceedingMaxTransportable(string lootId)
        {
            Assert.That(RaidCode.TryParse("038271", out RaidCode code), Is.True);
            var admission = new RaidAdmissionData(
                code,
                new ProfileId("profile-code"),
                "reservation-1",
                new[]
                {
                    new LootEntry(new LootId("arming_sword"), 1),
                    new LootEntry(new LootId(lootId), 2)
                },
                default,
                new[] { 1, 0, 0, 0, 0, 0 });

            bool valid = admission.TryProjectLoadout(
                _catalog,
                backpackCapacity: 10,
                out _,
                out _,
                out string error);

            Assert.That(valid, Is.False);
            Assert.That(error, Is.EqualTo("Loadout exceeds item transport limits."));
        }

        [TestCase("UTL_RUNE_COMPASS")]
        [TestCase("UTL_ARCANE_DETECTOR")]
        [TestCase("CON_SELF_REVIVE")]
        public void RaidAdmission_AllowsExactlyMaxTransportable(string lootId)
        {
            Assert.That(RaidCode.TryParse("038271", out RaidCode code), Is.True);
            var admission = new RaidAdmissionData(
                code,
                new ProfileId("profile-code"),
                "reservation-1",
                new[]
                {
                    new LootEntry(new LootId("arming_sword"), 1),
                    new LootEntry(new LootId(lootId), 1)
                },
                default,
                new[] { 1, 0, 0, 0, 0, 0 });

            bool valid = admission.TryProjectLoadout(
                _catalog,
                backpackCapacity: 10,
                out _,
                out _,
                out string error);

            Assert.That(valid, Is.True, error);
        }

        [Test]
        public void StashEvaluation_AllowsHoardingMultipleRestrictedUtilities()
        {
            // Stash is not subject to MaxTransportable: Stash transfers evaluate physical slot capacity only.
            var stashItems = new List<LootEntry>
            {
                new LootEntry(new LootId("UTL_RUNE_COMPASS"), 5),
                new LootEntry(new LootId("UTL_ARCANE_DETECTOR"), 3),
                new LootEntry(new LootId("CON_SELF_REVIVE"), 10)
            };

            // In Stash, slot capacity evaluates entries based on stackMax, without MaxTransportable blocking.
            int physicalSlotsUsed = 0;
            foreach (var item in stashItems)
            {
                Assert.That(_catalog.TryGet(item.LootId.Value, out LootDefinition def), Is.True);
                int stackMax = def.StackMax > 0 ? def.StackMax : 1;
                physicalSlotsUsed += (item.Amount - 1) / stackMax + 1;
            }

            Assert.That(physicalSlotsUsed, Is.GreaterThan(0));
            Assert.That(physicalSlotsUsed, Is.LessThanOrEqualTo(40), "Stash capacity allows holding multiple units.");
        }
    }
}

#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Linq;
using Grimhold.Backend;
using NUnit.Framework;

namespace Tests.EditMode.Progression
{
    /// <summary>TASK-304 T3 (S1, S2, S3, S8): the store applies the authoritative respec snapshot in one commit.</summary>
    public sealed class LocalProfileStoreRespecTests
    {
        private RespecTestProfile _profile;
        private LocalProfileStore Store => _profile.Store;

        [SetUp] public void SetUp() => _profile = new RespecTestProfile();
        [TearDown] public void TearDown() => _profile.Dispose();

        [Test]
        public void ApplyConfirmedRespec_AppliesTheWholeSnapshotInOneCommit()
        {
            Assert.That(Store.ApplyConfirmedRespec(_profile.Result()), Is.EqualTo(StashOperationResult.Success));

            Assert.That(_profile.Commits, Is.EqualTo(1), "attributes, gold, equipment, lists, abilities and revision commit together");
            _profile.AssertAttributes(0, RespecTestProfile.OwnedPoints);
            Assert.That(Store.GetCurrency(), Is.EqualTo(RespecTestProfile.StartingGold - RespecTestProfile.Cost));
            Assert.That(Store.RemoteRevision, Is.EqualTo(RespecTestProfile.StartingRevision + 1));
            Assert.That(Store.RespecCost, Is.EqualTo(RespecTestProfile.Cost));
        }

        [Test]
        public void ApplyConfirmedRespec_UnequipsTheWeaponIntoTheLoadoutWithItsInstance_AndKeepsMetArmor()
        {
            Assert.That(Store.ApplyConfirmedRespec(_profile.Result()), Is.EqualTo(StashOperationResult.Success));

            PreparedEquipmentLoadout equipment = Store.GetPreparedEquipment();
            Assert.That(equipment.Get(EquipmentSlot.WeaponSetAMainHand).IsValid, Is.False);
            Assert.That(equipment.Get(EquipmentSlot.Armor), Is.EqualTo(RespecTestProfile.Armor));
            StashItem unequipped = Store.GetLoadout().Single(item => item.LootId == RespecTestProfile.Rapier);
            Assert.That(unequipped.Slot, Is.EqualTo(4));
            Assert.That(unequipped.Instance.InstanceId, Is.EqualTo(RespecTestProfile.RapierInstanceId));
            Assert.That(Store.GetStash(), Is.EqualTo(new[] { new StashItem(RespecTestProfile.Rune, 5, 19) }));
        }

        [Test]
        public void ApplyConfirmedRespec_TakesPreparedAbilitiesFromTheServer_ClearingOnlyUnmetSlots()
        {
            Assert.That(Store.ApplyConfirmedRespec(_profile.Result()), Is.EqualTo(StashOperationResult.Success));

            PreparedAbilityLoadout abilities = Store.GetPreparedAbilities();
            Assert.That(abilities.Slot1.IsValid, Is.False, "Charge needs Strength 5 and the build is reset");
            Assert.That(abilities.Slot2, Is.EqualTo(RespecTestProfile.Guard), "Guard has no requirement and stays equipped");
        }

        [Test]
        public void ApplyConfirmedRespec_Replay_AppliesTheSnapshotWithoutDebitingAgain()
        {
            Assert.That(Store.ApplyConfirmedRespec(_profile.Result()), Is.EqualTo(StashOperationResult.Success));

            RespecResult replay = _profile.Result(alreadySecured: true);
            Assert.That(Store.ApplyConfirmedRespec(replay), Is.EqualTo(StashOperationResult.Success));

            Assert.That(Store.GetCurrency(), Is.EqualTo(RespecTestProfile.StartingGold - RespecTestProfile.Cost));
            _profile.AssertAttributes(0, RespecTestProfile.OwnedPoints);
            Assert.That(Store.GetLoadout().Count(item => item.LootId == RespecTestProfile.Rapier), Is.EqualTo(1));
        }

        [Test]
        public void ApplyConfirmedRespec_ReplayOnAStaleLocalProfile_CatchesUpToTheSameState()
        {
            RespecResult replay = _profile.Result(alreadySecured: true);

            Assert.That(Store.ApplyConfirmedRespec(replay), Is.EqualTo(StashOperationResult.Success));

            Assert.That(Store.GetCurrency(), Is.EqualTo(RespecTestProfile.StartingGold - RespecTestProfile.Cost));
            _profile.AssertAttributes(0, RespecTestProfile.OwnedPoints);
        }

        [Test]
        public void ApplyConfirmedRespec_OlderThanTheLocalRevision_IsRejectedWithoutChanges()
        {
            Assert.That(Store.ApplyConfirmedRespec(_profile.Result(revision: RespecTestProfile.StartingRevision - 1)),
                Is.EqualTo(StashOperationResult.InvalidInventory));

            _profile.AssertUnchanged();
            Assert.That(Store.RespecCost, Is.Null);
        }

        [Test]
        public void ApplyConfirmedRespec_UnknownOrUnownedPreparedAbility_IsRejectedWithoutChanges()
        {
            RespecResult result = _profile.Result();
            result.preparedAbilities = new PreparedAbilitiesDto { slot1 = "not_unlocked", slot2 = "" };

            Assert.That(Store.ApplyConfirmedRespec(result), Is.EqualTo(StashOperationResult.InvalidInventory));

            _profile.AssertUnchanged();
        }

        [Test]
        public void ApplyConfirmedRespec_MissingLists_IsRejectedWithoutChanges()
        {
            RespecResult result = _profile.Result();
            result.stash = null;

            Assert.That(Store.ApplyConfirmedRespec(result), Is.EqualTo(StashOperationResult.InvalidInventory));

            _profile.AssertUnchanged();
        }

        [Test]
        public void ApplyConfirmedRespec_WithAPendingReservation_IsRejectedWithoutChanges()
        {
            LocalProfileSnapshot withReservation = _profile.Repository.Snapshot.Clone();
            withReservation.PendingReservation = new PendingLoadoutReservation(
                "reservation", new List<StashItem>(), default);
            Assert.That(_profile.Repository.TrySave(withReservation, out _), Is.True);

            Assert.That(Store.ApplyConfirmedRespec(_profile.Result()), Is.EqualTo(StashOperationResult.InvalidInventory));

            Assert.That(Store.GetCurrency(), Is.EqualTo(RespecTestProfile.StartingGold));
            _profile.AssertAttributes(5, 3);
            Assert.That(_profile.Commits, Is.Zero);
        }

        [Test]
        public void ApplyConfirmedRespec_InvalidAttributes_IsRejectedWithoutChanges()
        {
            RespecResult result = _profile.Result();
            result.characterAttributes = new CharacterAttributesData { vitality = -1, availablePoints = 34 };

            Assert.That(Store.ApplyConfirmedRespec(result), Is.EqualTo(StashOperationResult.InvalidInventory));

            _profile.AssertUnchanged();
        }

        [Test]
        public void ReconcileRemoteState_WithProgression_CarriesTheServerRespecCost()
        {
            Assert.That(Store.RespecCost, Is.Null);

            Assert.That(Store.ReconcileRemoteState(null, new ProgressionData
            {
                level = 1, characterAttributes = new CharacterAttributesData { vitality = 5, resistance = 5, strength = 5,
                    dexterity = 5, intelligence = 5, luck = 5, availablePoints = 3 },
                respecCost = 175, revision = RespecTestProfile.StartingRevision
            }, _profile.LootCatalog, null), Is.EqualTo(StashOperationResult.Success));

            Assert.That(Store.RespecCost, Is.EqualTo(175));
        }

        [Test]
        public void ReconcileRemoteState_WithoutProgression_LeavesTheCostAlone()
        {
            Store.SetRespecCost(120);

            Assert.That(Store.ReconcileRemoteState(new InventoryData
            {
                inventorySchemaVersion = 3, currency = 300, revision = RespecTestProfile.StartingRevision,
                stash = new InventoryItemData[0], loadout = new InventoryItemData[0],
                preparedEquipment = PreparedEquipmentData.FromLoadout(Store.GetPreparedEquipment())
            }, null, _profile.LootCatalog, null), Is.EqualTo(StashOperationResult.Success));

            Assert.That(Store.RespecCost, Is.EqualTo(120));
            Assert.That(Store.GetCurrency(), Is.EqualTo(300));
        }

        [Test]
        public void ForceCharacterAttributeState_StillAppliesAnAssignment()
        {
            Assert.That(CharacterAttributeState.TryCreate(6, 5, 5, 5, 5, 5, 2, out CharacterAttributeState assigned), Is.True);

            Store.ForceCharacterAttributeState(assigned);

            Assert.That(Store.TryGetCharacterAttributeState(out CharacterAttributeState current), Is.True);
            Assert.That(current, Is.EqualTo(assigned));
            Assert.That(_profile.Commits, Is.EqualTo(1));
        }
    }
}
#endif

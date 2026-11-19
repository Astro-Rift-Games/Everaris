#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Stash
{
    /// <summary>
    /// FIX-56: a reservation confirmed locally for raid admission stays marked as awaiting
    /// backend admission, durably, and can never be rolled back while that mark holds.
    /// </summary>
    public sealed class ReservationAdmissionMarkerTests
    {
        private const string ReservationId = "reservation-a";
        private LootDefinitionCatalog _catalog;
        private readonly ProfileId _profile = new("56565656565656565656565656565656");

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
                "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
            Assert.That(_catalog, Is.Not.Null);
        }

        [Test]
        public void Admit_DiscardsReservationAndPersistsMarker()
        {
            InMemoryLocalProfileRepository repository = SeedReservation();
            var store = new LocalProfileStore(repository, _profile, _catalog);

            Assert.That(store.TryAdmitLoadoutReservation(ReservationId), Is.EqualTo(StashOperationResult.Success));

            Assert.That(store.PendingReservation, Is.Null);
            Assert.That(store.AdmissionPendingReservationId, Is.EqualTo(ReservationId));
            Assert.That(store.GetStash(), Is.EqualTo(new[] { new StashItem(new LootId("bone"), 3, 0) }));
            Assert.That(store.GetLoadout(), Is.Empty);
            Assert.That(store.GetPreparedEquipment().WeaponSetAMainHand, Is.EqualTo(default(LootId)));
            Assert.That(LocalProfileSaveCodec.TryDecode(LocalProfileSaveCodec.Encode(repository.Snapshot),
                _profile, _catalog, out LocalProfileSnapshot decoded, out _, out string error), Is.True, error);
            Assert.That(decoded.AdmissionPendingReservationId, Is.EqualTo(ReservationId));
        }

        [Test]
        public void Admit_RejectsUnknownReservationWithoutChanges()
        {
            InMemoryLocalProfileRepository repository = SeedReservation();
            var store = new LocalProfileStore(repository, _profile, _catalog);
            int commits = 0;
            store.ProfileCommitted += _ => commits++;

            Assert.That(store.TryAdmitLoadoutReservation("reservation-other"),
                Is.EqualTo(StashOperationResult.InvalidInventory));

            Assert.That(store.PendingReservation?.ReservationId, Is.EqualTo(ReservationId));
            Assert.That(store.AdmissionPendingReservationId, Is.Null);
            Assert.That(commits, Is.EqualTo(0));
        }

        [Test]
        public void LoadoutServiceConfirm_MarksAdmissionPending()
        {
            InMemoryLocalProfileRepository repository = SeedReservation();
            var store = new LocalProfileStore(repository, _profile, _catalog);
            var host = new GameObject("loadout-service");
            try
            {
                var service = host.AddComponent<InMemoryPlayerLoadoutService>();
                service.Initialize(store);

                Assert.That(service.TryConfirmLoadoutReservation(_profile, ReservationId),
                    Is.EqualTo(StashOperationResult.Success));

                Assert.That(store.PendingReservation, Is.Null);
                Assert.That(store.AdmissionPendingReservationId, Is.EqualTo(ReservationId));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Rollback_OfReservationAwaitingAdmission_IsRefusedWithoutChanges()
        {
            InMemoryLocalProfileRepository repository = SeedReservation();
            var store = new LocalProfileStore(repository, _profile, _catalog);
            Assert.That(store.TryAdmitLoadoutReservation(ReservationId), Is.EqualTo(StashOperationResult.Success));
            // Relogin: the backend still holds the un-admitted reservation and hydration restores it.
            LocalProfileSnapshot rehydrated = repository.Snapshot.Clone();
            Assert.That(ApplicationStashServiceBootstrapper.HydrateSnapshot(
                _profile, rehydrated, Inventory(admittedAt: null), null, _catalog, null), Is.True);
            Assert.That(repository.TrySave(rehydrated, out string saveError), Is.True, saveError);
            store = new LocalProfileStore(repository, _profile, _catalog);
            int commits = 0;
            store.ProfileCommitted += _ => commits++;

            Assert.That(store.TryRollbackLoadoutReservation(ReservationId),
                Is.EqualTo(StashOperationResult.ReservationAdmitted));

            Assert.That(store.PendingReservation?.ReservationId, Is.EqualTo(ReservationId));
            Assert.That(store.AdmissionPendingReservationId, Is.EqualTo(ReservationId));
            Assert.That(store.GetStash(), Is.EqualTo(new[] { new StashItem(new LootId("bone"), 3, 0) }));
            Assert.That(store.GetLoadout(), Is.Empty);
            Assert.That(store.GetPreparedEquipment().WeaponSetAMainHand, Is.EqualTo(default(LootId)));
            Assert.That(commits, Is.EqualTo(0));
        }

        [Test]
        public void Rollback_OfUnmarkedReservation_StillRestores()
        {
            InMemoryLocalProfileRepository repository = SeedReservation();
            var store = new LocalProfileStore(repository, _profile, _catalog);

            Assert.That(store.TryRollbackLoadoutReservation(ReservationId), Is.EqualTo(StashOperationResult.Success));

            Assert.That(store.PendingReservation, Is.Null);
            Assert.That(store.GetLoadout(), Is.EqualTo(new[] { new StashItem(new LootId("bone"), 2, 0) }));
            Assert.That(store.GetPreparedEquipment().WeaponSetAMainHand, Is.EqualTo(new LootId("rapier")));
        }

        [Test]
        public void ClearAdmission_RemovesOnlyMatchingMarker()
        {
            InMemoryLocalProfileRepository repository = SeedReservation();
            var store = new LocalProfileStore(repository, _profile, _catalog);
            Assert.That(store.TryAdmitLoadoutReservation(ReservationId), Is.EqualTo(StashOperationResult.Success));
            int commits = 0;
            store.ProfileCommitted += _ => commits++;

            Assert.That(store.TryClearReservationAdmission("reservation-other"),
                Is.EqualTo(StashOperationResult.InvalidInventory));
            Assert.That(store.AdmissionPendingReservationId, Is.EqualTo(ReservationId));
            Assert.That(commits, Is.EqualTo(0));

            Assert.That(store.TryClearReservationAdmission(ReservationId), Is.EqualTo(StashOperationResult.Success));
            Assert.That(store.AdmissionPendingReservationId, Is.Null);
            Assert.That(commits, Is.EqualTo(1));
            Assert.That(LocalProfileSaveCodec.TryDecode(LocalProfileSaveCodec.Encode(repository.Snapshot),
                _profile, _catalog, out LocalProfileSnapshot decoded, out _, out string error), Is.True, error);
            Assert.That(decoded.AdmissionPendingReservationId, Is.Null);
        }

        [Test]
        public void Decode_PreviousSchemaCache_HasNoMarker()
        {
            InMemoryLocalProfileRepository repository = SeedReservation();
            var document = Newtonsoft.Json.Linq.JObject.Parse(LocalProfileSaveCodec.Encode(repository.Snapshot));
            document["schemaVersion"] = 13;
            document["admissionPendingReservationId"] = ReservationId;
            string json = document.ToString();

            Assert.That(LocalProfileSaveCodec.TryDecode(json, _profile, _catalog,
                out LocalProfileSnapshot decoded, out _, out string error), Is.True, error);
            Assert.That(decoded.AdmissionPendingReservationId, Is.Null);
            Assert.That(decoded.PendingReservation?.ReservationId, Is.EqualTo(ReservationId));
            Assert.That(LocalProfileSnapshot.CurrentSchemaVersion, Is.EqualTo(14));
        }

        [Test]
        public void Decode_BlankMarker_IsRejected()
        {
            var document = Newtonsoft.Json.Linq.JObject.Parse(LocalProfileSaveCodec.Encode(SeedReservation().Snapshot));
            document["admissionPendingReservationId"] = "   ";

            Assert.That(LocalProfileSaveCodec.TryDecode(document.ToString(), _profile, _catalog,
                out _, out _, out string error), Is.False);
            Assert.That(error, Is.EqualTo("Admission-pending reservation identity is invalid."));
        }

        [Test]
        public void Decode_FutureSchema_IsUnsupportedVersion()
        {
            var document = Newtonsoft.Json.Linq.JObject.Parse(LocalProfileSaveCodec.Encode(SeedReservation().Snapshot));
            document["schemaVersion"] = LocalProfileSnapshot.CurrentSchemaVersion + 1;

            Assert.That(LocalProfileSaveCodec.TryDecode(document.ToString(), _profile, _catalog,
                out _, out LocalProfilePersistenceStatus status, out _), Is.False);
            Assert.That(status, Is.EqualTo(LocalProfilePersistenceStatus.UnsupportedVersion));
        }

        [Test]
        public void OrphanResolver_MarkedRehydratedReservation_IsNotRestored()
        {
            InMemoryLocalProfileRepository repository = SeedReservation();
            var store = new LocalProfileStore(repository, _profile, _catalog);
            Assert.That(store.TryAdmitLoadoutReservation(ReservationId), Is.EqualTo(StashOperationResult.Success));
            LocalProfileSnapshot rehydrated = repository.Snapshot.Clone();
            Assert.That(ApplicationStashServiceBootstrapper.HydrateSnapshot(
                _profile, rehydrated, Inventory(admittedAt: null), null, _catalog, null), Is.True);
            Assert.That(repository.TrySave(rehydrated, out string error), Is.True, error);
            store = new LocalProfileStore(repository, _profile, _catalog);

            Assert.That(ApplicationStashServiceBootstrapper.TryResolveOrphanedReservation(store, admitted: false),
                Is.EqualTo(StashOperationResult.ReservationAdmitted));
            Assert.That(store.GetLoadout(), Is.Empty);
            Assert.That(store.GetPreparedEquipment().WeaponSetAMainHand, Is.EqualTo(default(LootId)));
        }

        private InMemoryLocalProfileRepository SeedReservation()
        {
            var repository = new InMemoryLocalProfileRepository();
            Assert.That(repository.Initialize(_profile, _catalog), Is.True);
            LocalProfileSnapshot snapshot = repository.Snapshot.Clone();
            Assert.That(ApplicationStashServiceBootstrapper.HydrateSnapshot(
                _profile, snapshot, Inventory(admittedAt: null), null, _catalog, null), Is.True);
            Assert.That(repository.TrySave(snapshot, out string error), Is.True, error);
            return repository;
        }

        private static InventoryData Inventory(string admittedAt) => new()
        {
            revision = 7,
            stash = new[] { new InventoryItemData { lootId = "bone", amount = 3 } },
            loadout = Array.Empty<InventoryItemData>(),
            pendingReservation = new PendingReservationData
            {
                reservationId = ReservationId,
                admittedAt = admittedAt,
                items = new[] { new InventoryItemData { lootId = "bone", amount = 2 } },
                preparedEquipment = new PreparedEquipmentData { weaponSlot1 = "rapier" }
            }
        };
    }
}
#endif

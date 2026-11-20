#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;

namespace Tests.EditMode.Stash
{
    /// <summary>
    /// FIX-56 (T3): at login a reservation that was confirmed locally but never admitted on the backend is
    /// resolved as a loss (admit, then loss clear), never as a rollback that would hand used items back.
    /// </summary>
    public sealed class ReservationLoginAdmissionTests
    {
        private const string ReservationId = ReservationSeed.ReservationId;
        private static readonly StashItem[] StashAfterLoss = { new StashItem(new LootId("bone"), 3, 0) };

        private InMemoryLocalProfileRepository _repository;

        /// <summary>Marked reservation after a raid whose admit never reached the backend, then a relogin.</summary>
        private LocalProfileStore ReloginWithMarkedReservation(InventoryData? backend = null)
        {
            _repository = ReservationSeed.Hydrated(ReservationSeed.Inventory());
            var store = new LocalProfileStore(_repository, ReservationSeed.Profile, ReservationSeed.Catalog());
            Assert.That(store.TryAdmitLoadoutReservation(ReservationId), Is.EqualTo(StashOperationResult.Success));
            return ReservationSeed.Relogin(_repository, backend ?? ReservationSeed.Inventory());
        }

        [Test]
        public async Task MarkedUnadmittedReservation_IsLostThenAdmittedThenClearedAsLoss_NeverRolledBack()
        {
            LocalProfileStore store = ReloginWithMarkedReservation();
            Assert.That(store.PendingReservation?.ReservationId, Is.EqualTo(ReservationId), "Hydration restores the backend reservation.");
            int commits = 0;
            store.ProfileCommitted += _ => commits++;

            string markedId = ApplicationStashServiceBootstrapper.ResolveAdmissionMarkOnLogin(store, ReservationSeed.Inventory());

            Assert.That(markedId, Is.EqualTo(ReservationId));
            Assert.That(store.PendingReservation, Is.Null);
            Assert.That(store.GetStash(), Is.EqualTo(StashAfterLoss));
            Assert.That(store.GetLoadout(), Is.Empty);
            Assert.That(store.GetPreparedEquipment().WeaponSetAMainHand, Is.EqualTo(default(LootId)));
            Assert.That(store.AdmissionPendingReservationId, Is.EqualTo(ReservationId), "The mark stays until the backend proves the admission.");
            Assert.That(commits, Is.EqualTo(1));

            using var backend = new ReservationBackendHarness(store, (_, _) => BackendReply.Ok(11));
            bool resolved = await ApplicationStashServiceBootstrapper.ResolveAdmissionMarkedReservationAsync(backend.Service, markedId);

            Assert.That(resolved, Is.True);
            RecordedRequest[] requests = backend.Requests;
            Assert.That(requests.Length, Is.EqualTo(2));
            Assert.That(requests[0].IsAdmit, Is.True);
            Assert.That(requests[0].Body, Does.Contain($"\"reservationId\":\"{ReservationId}\""));
            Assert.That(requests[1].IsDelete, Is.True);
            Assert.That(requests[1].Rollback, Is.Null, "A used reservation is cleared as a loss, never rolled back.");
            Assert.That(requests[1].ExpectedReservationId, Is.EqualTo(ReservationId));
            Assert.That(requests.Any(r => r.Rollback == "true"), Is.False);
            Assert.That(store.AdmissionPendingReservationId, Is.Null);
            Assert.That(store.GetStash(), Is.EqualTo(StashAfterLoss));
            Assert.That(store.GetLoadout(), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AdmitFailure_RollsNothingBackAndKeepsTheMark(bool transient)
        {
            LocalProfileStore store = ReloginWithMarkedReservation();
            string markedId = ApplicationStashServiceBootstrapper.ResolveAdmissionMarkOnLogin(store, ReservationSeed.Inventory());
            Assert.That(markedId, Is.EqualTo(ReservationId));
            using var backend = new ReservationBackendHarness(store, (_, _) => transient
                ? BackendReply.TransportFailure() : BackendReply.Error(400, "VALIDATION_FAILED"));
            if (transient)
                for (int round = 0; round < RemoteInventoryService.AdmissionRetryRounds; round++)
                    UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                        new System.Text.RegularExpressions.Regex(@"^\[RemoteIdempotentRetryPolicy\] Max retries reached for error \w+\. Aborting\.$"));

            bool resolved = await ApplicationStashServiceBootstrapper.ResolveAdmissionMarkedReservationAsync(backend.Service, markedId);

            Assert.That(resolved, Is.False);
            Assert.That(backend.Requests.All(r => r.IsAdmit), Is.True, "No loss clear and no rollback after a failed admit.");
            Assert.That(backend.Requests.Any(r => r.IsDelete), Is.False);
            Assert.That(store.AdmissionPendingReservationId, Is.EqualTo(ReservationId));
            Assert.That(store.PendingReservation, Is.Null);
            Assert.That(store.GetStash(), Is.EqualTo(StashAfterLoss));
            Assert.That(store.GetLoadout(), Is.Empty);
            Assert.That(store.GetPreparedEquipment().WeaponSetAMainHand, Is.EqualTo(default(LootId)));
        }

        [Test]
        public void UnmarkedUnadmittedOrphan_IsLeftToTheExistingRollback()
        {
            _repository = ReservationSeed.Hydrated(ReservationSeed.Inventory());
            LocalProfileStore store = ReservationSeed.Relogin(_repository, ReservationSeed.Inventory());
            string before = LocalProfileSaveCodec.Encode(_repository.Snapshot);
            int commits = 0;
            store.ProfileCommitted += _ => commits++;

            string markedId = ApplicationStashServiceBootstrapper.ResolveAdmissionMarkOnLogin(store, ReservationSeed.Inventory());

            Assert.That(markedId, Is.Null);
            Assert.That(commits, Is.Zero);
            Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(before));
            Assert.That(ApplicationStashServiceBootstrapper.TryResolveOrphanedReservation(store, false),
                Is.EqualTo(StashOperationResult.Success));
            Assert.That(store.GetLoadout(), Is.EqualTo(new[] { new StashItem(new LootId("bone"), 2, 0) }));
            Assert.That(store.GetPreparedEquipment().WeaponSetAMainHand, Is.EqualTo(new LootId("rapier")));
        }

        [Test]
        public void StaleMark_BackendHoldsNoReservation_IsCleared()
        {
            LocalProfileStore store = ReloginWithMarkedReservation(ReservationSeed.Inventory(reservationId: null));
            Assert.That(store.AdmissionPendingReservationId, Is.EqualTo(ReservationId));

            string markedId = ApplicationStashServiceBootstrapper.ResolveAdmissionMarkOnLogin(
                store, ReservationSeed.Inventory(reservationId: null));

            Assert.That(markedId, Is.Null);
            Assert.That(store.AdmissionPendingReservationId, Is.Null);
            AssertMarkPersistedAs(null);
            Assert.That(store.GetStash(), Is.EqualTo(StashAfterLoss));
            Assert.That(store.GetLoadout(), Is.Empty);
        }

        [Test]
        public void StaleMark_BackendHoldsADifferentReservation_IsClearedAndTheOtherIsLeftAlone()
        {
            InventoryData other = ReservationSeed.Inventory(reservationId: "reservation-b");
            LocalProfileStore store = ReloginWithMarkedReservation(other);
            Assert.That(store.PendingReservation?.ReservationId, Is.EqualTo("reservation-b"));

            string markedId = ApplicationStashServiceBootstrapper.ResolveAdmissionMarkOnLogin(store, other);

            Assert.That(markedId, Is.Null);
            Assert.That(store.AdmissionPendingReservationId, Is.Null);
            AssertMarkPersistedAs(null);
            Assert.That(store.PendingReservation?.ReservationId, Is.EqualTo("reservation-b"),
                "A different un-admitted orphan keeps the existing rollback path.");
        }

        [Test]
        public void MarkedReservationAdmittedOnBackend_ClearsTheMarkAndKeepsTheAdmittedLossPath()
        {
            InventoryData admitted = ReservationSeed.Inventory(admittedAt: "2026-10-29T12:00:00.000Z");
            LocalProfileStore store = ReloginWithMarkedReservation(admitted);

            string markedId = ApplicationStashServiceBootstrapper.ResolveAdmissionMarkOnLogin(store, admitted);

            Assert.That(markedId, Is.Null);
            Assert.That(store.AdmissionPendingReservationId, Is.Null);
            Assert.That(store.PendingReservation?.ReservationId, Is.EqualTo(ReservationId));
        }

        [Test]
        public void OfflineLogin_WithoutBackendData_KeepsTheMark()
        {
            LocalProfileStore store = ReloginWithMarkedReservation();

            string markedId = ApplicationStashServiceBootstrapper.ResolveAdmissionMarkOnLogin(store, null);

            Assert.That(markedId, Is.Null);
            Assert.That(store.AdmissionPendingReservationId, Is.EqualTo(ReservationId));
            Assert.That(store.PendingReservation?.ReservationId, Is.EqualTo(ReservationId));
        }

        [Test]
        public void PendingExtractionCommit_IsNotTouched()
        {
            LocalProfileStore store = ReloginWithMarkedReservation();
            Assert.That(store.TryCommitExtraction(new ExtractionReceipt("raid-a", ReservationSeed.Profile, 1),
                Array.Empty<StashItem>(), default, 0, 1, 0), Is.EqualTo(StashOperationResult.Success));
            string before = LocalProfileSaveCodec.Encode(_repository.Snapshot);

            string markedId = ApplicationStashServiceBootstrapper.ResolveAdmissionMarkOnLogin(store, ReservationSeed.Inventory());

            Assert.That(markedId, Is.Null);
            Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(before));
        }

        private void AssertMarkPersistedAs(string expected)
        {
            Assert.That(LocalProfileSaveCodec.TryDecode(LocalProfileSaveCodec.Encode(_repository.Snapshot),
                ReservationSeed.Profile, ReservationSeed.Catalog(), out LocalProfileSnapshot decoded, out _, out string error),
                Is.True, error);
            Assert.That(decoded.AdmissionPendingReservationId, Is.EqualTo(expected));
        }
    }
}
#endif

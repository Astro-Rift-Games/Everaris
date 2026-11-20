#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Linq;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;

namespace Tests.EditMode.Stash
{
    /// <summary>
    /// FIX-56 (T2): the admission confirmation is retried in bounded rounds with progressive pauses over
    /// the real HTTP boundary, the local mark is cleared only on backend proof, and a marked reservation
    /// is never rolled back on the backend.
    /// </summary>
    public sealed class ReservationAdmissionRetryTests
    {
        private const string ReservationId = ReservationSeed.ReservationId;
        private const int InnerAttempts = RemoteIdempotentRetryPolicy.MaxRetries;

        private InMemoryLocalProfileRepository _repository;
        private LocalProfileStore _store;

        [SetUp]
        public void SetUp()
        {
            _repository = ReservationSeed.Hydrated(ReservationSeed.Inventory());
            _store = new LocalProfileStore(_repository, ReservationSeed.Profile, ReservationSeed.Catalog());
            Assert.That(_store.TryAdmitLoadoutReservation(ReservationId), Is.EqualTo(StashOperationResult.Success));
        }

        private static void ExpectExhaustedInnerRounds(int rounds)
        {
            for (int i = 0; i < rounds; i++)
                UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                    new System.Text.RegularExpressions.Regex(@"^\[RemoteIdempotentRetryPolicy\] Max retries reached for error \w+\. Aborting\.$"));
        }

        [TestCase(4)]
        [TestCase(7)]
        public async Task TransientFailuresAcrossRounds_EndAdmittedAndClearThePersistedMark(int failuresBeforeSuccess)
        {
            using var backend = new ReservationBackendHarness(_store,
                (index, _) => index < failuresBeforeSuccess ? BackendReply.TransportFailure() : BackendReply.Ok(9));
            ExpectExhaustedInnerRounds(failuresBeforeSuccess / InnerAttempts);

            var (success, error) = await backend.Service.EnsureReservationAdmittedAsync(ReservationId);

            Assert.That(success, Is.True, error.error);
            RecordedRequest[] requests = backend.Requests;
            Assert.That(requests.Length, Is.EqualTo(failuresBeforeSuccess + 1));
            Assert.That(requests.All(r => r.IsAdmit), Is.True);
            Assert.That(requests.All(r => r.Body.Contains($"\"reservationId\":\"{ReservationId}\"")), Is.True,
                "Every attempt must carry the exact reservation id.");
            int roundsUsed = failuresBeforeSuccess / InnerAttempts + 1;
            Assert.That(backend.RoundDelays.Count, Is.EqualTo(roundsUsed - 1));
            Assert.That(backend.RoundDelays, Is.Ordered.Ascending.And.Unique, "Round pauses must be progressive.");
            Assert.That(backend.RoundDelays, Is.EqualTo(new[] { 2000, 4000, 8000 }.Take(roundsUsed - 1)));
            Assert.That(_store.AdmissionPendingReservationId, Is.Null);
            Assert.That(_store.RemoteRevision, Is.EqualTo(9));
            Assert.That(LocalProfileSaveCodec.TryDecode(LocalProfileSaveCodec.Encode(_repository.Snapshot),
                ReservationSeed.Profile, ReservationSeed.Catalog(), out LocalProfileSnapshot decoded, out _, out string decodeError),
                Is.True, decodeError);
            Assert.That(decoded.AdmissionPendingReservationId, Is.Null);
        }

        [Test]
        public async Task PersistentTransientFailure_ExhaustsBoundedRoundsAndKeepsTheMark()
        {
            using var backend = new ReservationBackendHarness(_store, (_, _) => BackendReply.TransportFailure());
            int commits = 0;
            _store.ProfileCommitted += _ => commits++;
            ExpectExhaustedInnerRounds(RemoteInventoryService.AdmissionRetryRounds);

            var (success, error) = await backend.Service.EnsureReservationAdmittedAsync(ReservationId);

            Assert.That(success, Is.False);
            Assert.That(BackendErrorUtility.IsTransportFailure(error.error), Is.True, error.error);

            Assert.That(backend.Requests.Length, Is.EqualTo(RemoteInventoryService.AdmissionRetryRounds * InnerAttempts));
            Assert.That(backend.Requests.All(r => r.IsAdmit), Is.True);
            Assert.That(backend.RoundDelays, Is.EqualTo(new[] { 2000, 4000, 8000 }));
            Assert.That(_store.AdmissionPendingReservationId, Is.EqualTo(ReservationId));
            Assert.That(commits, Is.EqualTo(0));
        }

        [Test]
        public async Task NoActiveReservation_StopsAtOnceAndClearsTheMark()
        {
            using var backend = new ReservationBackendHarness(_store,
                (_, _) => BackendReply.Error(422, "NO_ACTIVE_RESERVATION"));

            var (success, error) = await backend.Service.EnsureReservationAdmittedAsync(ReservationId);

            Assert.That(success, Is.False);
            Assert.That(error.error, Is.EqualTo("NO_ACTIVE_RESERVATION"));
            Assert.That(backend.Requests.Length, Is.EqualTo(1));
            Assert.That(backend.RoundDelays, Is.Empty);
            Assert.That(_store.AdmissionPendingReservationId, Is.Null);
            Assert.That(LocalProfileSaveCodec.TryDecode(LocalProfileSaveCodec.Encode(_repository.Snapshot),
                ReservationSeed.Profile, ReservationSeed.Catalog(), out LocalProfileSnapshot decoded, out _, out string decodeError),
                Is.True, decodeError);
            Assert.That(decoded.AdmissionPendingReservationId, Is.Null);
        }

        [TestCase(400, "VALIDATION_FAILED")]
        [TestCase(409, "RESERVATION_MISMATCH")]
        public async Task TerminalCode_KeepsTheMarkWithoutFurtherRounds(int status, string code)
        {
            using var backend = new ReservationBackendHarness(_store, (_, _) => BackendReply.Error(status, code));

            var (success, error) = await backend.Service.EnsureReservationAdmittedAsync(ReservationId);

            Assert.That(success, Is.False);
            Assert.That(error.error, Is.EqualTo(code));
            Assert.That(backend.Requests.Length, Is.EqualTo(1));
            Assert.That(backend.RoundDelays, Is.Empty);
            Assert.That(_store.AdmissionPendingReservationId, Is.EqualTo(ReservationId));
        }

        [Test]
        public async Task Rollback_OfMarkedReservation_IsRefusedWithoutAnyRequest()
        {
            using var backend = new ReservationBackendHarness(_store, (_, _) => BackendReply.Ok(9));

            var (success, error) = await backend.Service.RollbackPendingReservationAsync(ReservationId);

            Assert.That(success, Is.False);
            Assert.That(error.error, Is.EqualTo("RESERVATION_ADMITTED"));
            Assert.That(backend.Requests, Is.Empty);
            Assert.That(_store.AdmissionPendingReservationId, Is.EqualTo(ReservationId));
        }

        [Test]
        public async Task Rollback_OfAnotherReservation_StillReachesTheBackend()
        {
            using var backend = new ReservationBackendHarness(_store, (_, _) => BackendReply.Ok(9));

            var (success, error) = await backend.Service.RollbackPendingReservationAsync("reservation-other");

            Assert.That(success, Is.True, error.error);
            RecordedRequest request = backend.Requests.Single();
            Assert.That(request.IsDelete, Is.True);
            Assert.That(request.Rollback, Is.EqualTo("true"));
            Assert.That(request.ExpectedReservationId, Is.EqualTo("reservation-other"));
            Assert.That(_store.AdmissionPendingReservationId, Is.EqualTo(ReservationId));
        }
    }
}
#endif

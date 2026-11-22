#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;

namespace Tests.EditMode.Progression
{
    /// <summary>TASK-304 T3 (S1, S2, S3): remote-first respec, idempotent by transaction id, applied once.</summary>
    public sealed class AttributeRespecServiceTests
    {
        private RespecTestProfile _profile;
        private readonly List<(string transactionId, int revision)> _calls = new();
        private readonly Queue<Func<(bool, RespecResult, BackendError)>> _responses = new();
        private int _reconciles;
        private bool _reconcileSucceeds;
        private Action _onReconcile;
        private AttributeRespecService _service;

        [SetUp]
        public void SetUp()
        {
            _profile = new RespecTestProfile();
            _calls.Clear();
            _responses.Clear();
            _reconciles = 0;
            _reconcileSucceeds = true;
            _onReconcile = null;
            _service = new AttributeRespecService(_profile.Store, Backend, Reconcile);
        }

        [TearDown] public void TearDown() => _profile.Dispose();

        private Task<(bool success, RespecResult data, BackendError error)> Backend(string transactionId, int expectedRevision)
        {
            _calls.Add((transactionId, expectedRevision));
            var response = _responses.Dequeue();
            var (success, data, error) = response();
            return Task.FromResult((success, data, error));
        }

        private Task<bool> Reconcile()
        {
            _reconciles++;
            _onReconcile?.Invoke();
            return Task.FromResult(_reconcileSucceeds);
        }

        private void Respond(RespecResult result) => _responses.Enqueue(() => (true, result, default));
        private void Fail(string code) => _responses.Enqueue(() => (false, default, new BackendError { error = code }));

        [Test]
        public async Task Success_SendsTheIdAndLocalRevision_AndAppliesTheSnapshotOnce()
        {
            Respond(_profile.Result(transactionId: "10_aa"));

            AttributeRespecOutcome outcome = await _service.RespecAsync("10_aa");

            Assert.That(outcome.Status, Is.EqualTo(AttributeRespecStatus.Applied));
            Assert.That(outcome.IsSuccess, Is.True);
            Assert.That(_calls, Is.EqualTo(new[] { ("10_aa", RespecTestProfile.StartingRevision) }));
            Assert.That(_reconciles, Is.Zero);
            Assert.That(_profile.Commits, Is.EqualTo(1));
            _profile.AssertAttributes(0, RespecTestProfile.OwnedPoints);
            Assert.That(_profile.Store.GetCurrency(), Is.EqualTo(RespecTestProfile.StartingGold - RespecTestProfile.Cost));
        }

        [Test]
        public async Task Replay_AppliesTheReturnedSnapshotTheSameWay_WithoutADoubleDebit()
        {
            Respond(_profile.Result(alreadySecured: true));

            AttributeRespecOutcome outcome = await _service.RespecAsync("10_aa");

            Assert.That(outcome.Status, Is.EqualTo(AttributeRespecStatus.AlreadyApplied));
            Assert.That(outcome.IsSuccess, Is.True);
            Assert.That(_profile.Store.GetCurrency(), Is.EqualTo(RespecTestProfile.StartingGold - RespecTestProfile.Cost));
            _profile.AssertAttributes(0, RespecTestProfile.OwnedPoints);
            Assert.That(_profile.Commits, Is.EqualTo(1));
        }

        [Test]
        public async Task LostResponse_ReconcilesOnceAndKeepsTheTransactionOpen_ThenTheRetryReplaysAndChargesOnce()
        {
            Fail(BackendErrorUtility.Timeout);
            // The server applied the first request; the reread brings the local profile to that state.
            _onReconcile = () => _profile.Store.ApplyConfirmedRespec(_profile.Result());

            AttributeRespecOutcome lost = await _service.RespecAsync("10_aa");

            Assert.That(lost.Status, Is.EqualTo(AttributeRespecStatus.Unconfirmed));
            Assert.That(lost.IsFinal, Is.False, "the same transaction id must be reused for the retry");
            Assert.That(_reconciles, Is.EqualTo(1));

            _onReconcile = null;
            Respond(_profile.Result(alreadySecured: true));
            AttributeRespecOutcome retry = await _service.RespecAsync("10_aa");

            Assert.That(retry.Status, Is.EqualTo(AttributeRespecStatus.AlreadyApplied));
            Assert.That(_calls.ConvertAll(call => call.transactionId), Is.EqualTo(new[] { "10_aa", "10_aa" }));
            Assert.That(_calls[1].revision, Is.EqualTo(RespecTestProfile.StartingRevision + 1), "the retry carries the reconciled revision");
            Assert.That(_profile.Store.GetCurrency(), Is.EqualTo(RespecTestProfile.StartingGold - RespecTestProfile.Cost));
        }

        [Test]
        public async Task BackendThrows_IsUnconfirmedAndReconciles()
        {
            _responses.Enqueue(() => throw new InvalidOperationException("transport"));

            AttributeRespecOutcome outcome = await _service.RespecAsync("10_aa");

            Assert.That(outcome.Status, Is.EqualTo(AttributeRespecStatus.Unconfirmed));
            Assert.That(outcome.IsFinal, Is.False);
            Assert.That(_reconciles, Is.EqualTo(1));
            _profile.AssertUnchanged();
        }

        [TestCase("NO_ASSIGNED_POINTS")]
        [TestCase("INSUFFICIENT_FUNDS")]
        [TestCase("RESERVATION_PENDING")]
        [TestCase("REVISION_CONFLICT")]
        [TestCase("TRANSACTION_ID_REUSED")]
        [TestCase("TRANSACTION_RETENTION_EXCEEDED")]
        [TestCase("INVENTORY_FULL")]
        public async Task DefinitiveRejection_LeavesTheProfileUntouched_AndReleasesTheTransaction(string code)
        {
            Fail(code);

            AttributeRespecOutcome outcome = await _service.RespecAsync("10_aa");

            Assert.That(outcome.Status, Is.EqualTo(AttributeRespecStatus.Rejected));
            Assert.That(outcome.ErrorCode, Is.EqualTo(code));
            Assert.That(outcome.IsFinal, Is.True);
            Assert.That(outcome.IsSuccess, Is.False);
            Assert.That(_reconciles, Is.EqualTo(1), "a rejection refreshes the local revision and balance");
            _profile.AssertUnchanged();
        }

        [Test]
        public async Task Rejection_WhenTheReconciliationFails_ReportsRecoveryFailedButStaysFinal()
        {
            _reconcileSucceeds = false;
            Fail("INSUFFICIENT_FUNDS");

            AttributeRespecOutcome outcome = await _service.RespecAsync("10_aa");

            Assert.That(outcome.Status, Is.EqualTo(AttributeRespecStatus.RecoveryFailed));
            Assert.That(outcome.ErrorCode, Is.EqualTo("INSUFFICIENT_FUNDS"));
            Assert.That(outcome.IsFinal, Is.True);
        }

        [Test]
        public async Task LostResponse_WhenTheReconciliationFails_StaysOpenForTheRetry()
        {
            _reconcileSucceeds = false;
            Fail(BackendErrorUtility.NetworkError);

            AttributeRespecOutcome outcome = await _service.RespecAsync("10_aa");

            Assert.That(outcome.Status, Is.EqualTo(AttributeRespecStatus.RecoveryFailed));
            Assert.That(outcome.IsFinal, Is.False);
        }

        [Test]
        public async Task SuccessThatTheStoreRefuses_ReconcilesOnce_AndIsFinal()
        {
            Respond(_profile.Result(revision: RespecTestProfile.StartingRevision - 1));

            AttributeRespecOutcome outcome = await _service.RespecAsync("10_aa");

            Assert.That(outcome.Status, Is.EqualTo(AttributeRespecStatus.Diverged));
            Assert.That(outcome.IsFinal, Is.True);
            Assert.That(_reconciles, Is.EqualTo(1));
            _profile.AssertUnchanged();
        }

        [Test]
        public async Task WithoutABackend_IsUnavailableAndSendsNothing()
        {
            var service = new AttributeRespecService(_profile.Store, null, Reconcile);

            AttributeRespecOutcome outcome = await service.RespecAsync("10_aa");

            Assert.That(outcome.Status, Is.EqualTo(AttributeRespecStatus.Unavailable));
            Assert.That(outcome.IsFinal, Is.True);
            Assert.That(_calls, Is.Empty);
            _profile.AssertUnchanged();
        }

        [Test]
        public async Task EnsureCost_FetchesTheServerCostOnce_ThenUsesTheStoredValue()
        {
            int fetches = 0;
            var service = new AttributeRespecService(_profile.Store, Backend, Reconcile,
                () => { fetches++; return Task.FromResult<int?>(RespecTestProfile.Cost); });

            Assert.That(await service.EnsureCostAsync(), Is.True);
            Assert.That(await service.EnsureCostAsync(), Is.True);

            Assert.That(fetches, Is.EqualTo(1));
            Assert.That(_profile.Store.RespecCost, Is.EqualTo(RespecTestProfile.Cost));
        }

        [Test]
        public async Task EnsureCost_WhenTheServerCostIsUnavailable_ReportsFalseAndStoresNothing()
        {
            var service = new AttributeRespecService(_profile.Store, Backend, Reconcile,
                () => Task.FromResult<int?>(null));

            Assert.That(await service.EnsureCostAsync(), Is.False);

            Assert.That(_profile.Store.RespecCost, Is.Null);
        }
    }
}
#endif

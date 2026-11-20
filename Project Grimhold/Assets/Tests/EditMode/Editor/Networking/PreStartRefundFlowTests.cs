#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;

public sealed class PreStartRefundFlowTests
{
    [TestCase("RESERVATION_MISMATCH")]
    [TestCase("RAID_ALREADY_STARTED")]
    [TestCase("REFUND_WINDOW_EXPIRED")]
    [TestCase("EXTRACTION_RESULT_PENDING")]
    [TestCase("RESULT_ALREADY_CONSUMED")]
    public void IsTerminalRejection_RecognizesTheContractCodes(string code)
    {
        Assert.That(PreStartRefundFlow.IsTerminalRejection(new BackendError { error = code }), Is.True);
    }

    [TestCase("TIMEOUT")]
    [TestCase("UNAUTHORIZED")]
    [TestCase("TOO_MANY_REQUESTS")]
    [TestCase("")]
    public void IsTerminalRejection_TransientOrUnrelatedErrorsAreNotRejections(string code)
    {
        Assert.That(PreStartRefundFlow.IsTerminalRejection(new BackendError { error = code }), Is.False);
    }

    [Test]
    public async Task Success_RestoresLocalOwnershipOnceAfterTheBackendRefund()
    {
        var calls = new List<string>();
        PreStartRefundOutcome outcome = await PreStartRefundFlow.ExecuteAsync(
            Task.CompletedTask,
            () => { calls.Add("refund"); return Task.FromResult((true, default(BackendError))); },
            () => { calls.Add("restore"); return StashOperationResult.Success; },
            () => { calls.Add("loss"); return Task.CompletedTask; });

        Assert.That(outcome, Is.EqualTo(PreStartRefundOutcome.Refunded));
        Assert.That(calls, Is.EqualTo(new[] { "refund", "restore" }));
    }

    [Test]
    public async Task TerminalRejection_FallsBackToLossClearWithoutRestoring()
    {
        var calls = new List<string>();
        PreStartRefundOutcome outcome = await PreStartRefundFlow.ExecuteAsync(
            Task.CompletedTask,
            () => { calls.Add("refund"); return Task.FromResult((false, new BackendError { error = "RAID_ALREADY_STARTED" })); },
            () => { calls.Add("restore"); return StashOperationResult.Success; },
            () => { calls.Add("loss"); return Task.CompletedTask; });

        Assert.That(outcome, Is.EqualTo(PreStartRefundOutcome.FellBackToLoss));
        Assert.That(calls, Is.EqualTo(new[] { "refund", "loss" }));
    }

    [Test]
    public async Task TransientExhaustion_NeitherRestoresNorConsumes()
    {
        var calls = new List<string>();
        PreStartRefundOutcome outcome = await PreStartRefundFlow.ExecuteAsync(
            Task.CompletedTask,
            () => { calls.Add("refund"); return Task.FromResult((false, new BackendError { error = "TIMEOUT" })); },
            () => { calls.Add("restore"); return StashOperationResult.Success; },
            () => { calls.Add("loss"); return Task.CompletedTask; });

        Assert.That(outcome, Is.EqualTo(PreStartRefundOutcome.Failed));
        Assert.That(calls, Is.EqualTo(new[] { "refund" }));
    }

    [Test]
    public async Task LocalRestoreFailure_IsReportedAfterTheBackendRefundSucceeded()
    {
        PreStartRefundOutcome outcome = await PreStartRefundFlow.ExecuteAsync(
            Task.CompletedTask,
            () => Task.FromResult((true, default(BackendError))),
            () => StashOperationResult.InvalidInventory,
            () => Task.CompletedTask);

        Assert.That(outcome, Is.EqualTo(PreStartRefundOutcome.LocalRestoreFailed));
    }

    [Test]
    public async Task PendingAdmission_CompletesBeforeTheRefundIsRequested()
    {
        var admission = new TaskCompletionSource<bool>();
        var calls = new List<string>();
        Task<PreStartRefundOutcome> flow = PreStartRefundFlow.ExecuteAsync(
            admission.Task,
            () => { calls.Add("refund"); return Task.FromResult((true, default(BackendError))); },
            () => StashOperationResult.Success,
            () => Task.CompletedTask);

        await Task.Yield();
        Assert.That(calls, Is.Empty, "The refund must not race the in-flight admission request.");
        admission.SetResult(true);
        await flow;
        Assert.That(calls, Is.EqualTo(new[] { "refund" }));
    }

    [Test]
    public async Task FaultedAdmission_DoesNotBlockTheRefund()
    {
        var admission = Task.FromException(new InvalidOperationException("admit transport failure"));
        int refunds = 0;
        PreStartRefundOutcome outcome = await PreStartRefundFlow.ExecuteAsync(
            admission,
            () => { refunds++; return Task.FromResult((true, default(BackendError))); },
            () => StashOperationResult.Success,
            () => Task.CompletedTask);

        Assert.That(outcome, Is.EqualTo(PreStartRefundOutcome.Refunded));
        Assert.That(refunds, Is.EqualTo(1));
    }
}
#endif

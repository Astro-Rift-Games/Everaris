using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// FIX-08: Host authoritative extraction publication must retry transient failures with backoff and
/// NEVER dispatch the client commit RPC until publication succeeds, while exposing status to Results UI.
/// </summary>
[TestFixture]
public sealed class ExtractionAuthorityPublishCoordinatorTests
{
    [Test]
    public void FirstPublishFails_SecondSucceeds_BlocksCommitRpcUntilPublished_AndNeverDuplicates()
    {
        var coordinator = new ExtractionAuthorityPublishCoordinator(maxRounds: 4, baseBackoffSeconds: 1.0f, maxBackoffSeconds: 8.0f);

        Assert.IsTrue(coordinator.CanStartAttempt(resultSequence: 1, now: 0f));
        Assert.IsFalse(coordinator.ShouldDispatchCommitRpc(1));

        // Attempt 1 starts -> Publishing.
        coordinator.MarkAttemptStarted(1);
        Assert.AreEqual(AuthorityPublishStatus.Publishing, coordinator.Status);
        Assert.IsFalse(coordinator.CanStartAttempt(1, now: 0.1f), "Must not overlap in-flight publication.");
        Assert.IsFalse(coordinator.ShouldDispatchCommitRpc(1));

        // Attempt 1 fails transiently -> Retrying (1s backoff); commit RPC stays BLOCKED!
        AuthorityPublishStatus afterFail = coordinator.MarkAttemptFailed(1, now: 0.2f, isRetryable: true);
        Assert.AreEqual(AuthorityPublishStatus.Retrying, afterFail);
        Assert.IsFalse(coordinator.ShouldDispatchCommitRpc(1), "Commit RPC must NOT be dispatched after failed publish.");
        Assert.IsFalse(coordinator.CanStartAttempt(1, now: 0.9f), "Must respect backoff before retrying.");

        // Attempt 2 starts after backoff -> Retrying, then succeeds -> Published!
        Assert.IsTrue(coordinator.CanStartAttempt(1, now: 1.25f));
        coordinator.MarkAttemptStarted(1);
        Assert.AreEqual(AuthorityPublishStatus.Retrying, coordinator.Status);
        coordinator.MarkAttemptSucceeded(1);

        Assert.AreEqual(AuthorityPublishStatus.Published, coordinator.Status);
        Assert.IsTrue(coordinator.ShouldDispatchCommitRpc(1), "Commit RPC is enabled once publication succeeds.");
        Assert.IsFalse(coordinator.CanStartAttempt(1, now: 100f), "Once published, must never republish duplicate result.");
    }

    [Test]
    public void ExhaustedOrNonRetryableFailure_TransitionsToFailed_AndNeverDispatchesBrokenCommitRpc()
    {
        var coordinator = new ExtractionAuthorityPublishCoordinator(maxRounds: 2, baseBackoffSeconds: 0.5f, maxBackoffSeconds: 2.0f);

        coordinator.MarkAttemptStarted(3);
        Assert.AreEqual(AuthorityPublishStatus.Retrying, coordinator.MarkAttemptFailed(3, now: 0f, isRetryable: true));

        coordinator.MarkAttemptStarted(3);
        Assert.AreEqual(AuthorityPublishStatus.Failed, coordinator.MarkAttemptFailed(3, now: 1f, isRetryable: true));

        Assert.IsFalse(coordinator.ShouldDispatchCommitRpc(3),
            "Client must never receive commit RPC when Host publication failed terminally.");
        Assert.IsFalse(coordinator.CanStartAttempt(3, now: 999f));
    }

    [Test]
    public void NonRetryableError_FailsImmediately_WithoutWastingRetries()
    {
        var coordinator = new ExtractionAuthorityPublishCoordinator(maxRounds: 4);
        coordinator.MarkAttemptStarted(5);
        Assert.AreEqual(AuthorityPublishStatus.Failed, coordinator.MarkAttemptFailed(5, now: 0f, isRetryable: false));
        Assert.IsFalse(coordinator.ShouldDispatchCommitRpc(5));
        Assert.AreEqual(1, coordinator.RoundsAttempted);
    }
}

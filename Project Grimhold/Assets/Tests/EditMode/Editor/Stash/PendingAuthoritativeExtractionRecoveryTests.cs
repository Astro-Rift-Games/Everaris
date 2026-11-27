using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// FIX-09: When a client disconnects after extraction before receiving the commit ACK RPC, login
/// detects the unconsumed server-side AuthoritativeExtractionResult, commits it idempotently,
/// and reconciles the local profile without discarding the reservation as a loss.
/// </summary>
[TestFixture]
public sealed class PendingAuthoritativeExtractionRecoveryTests
{
    private Func<int, Task> _originalDelay;
    private int _originalBackoff;

    [SetUp]
    public void SetUp()
    {
        _originalDelay = RemoteIdempotentRetryPolicy.DelayAsync;
        _originalBackoff = RemoteIdempotentRetryPolicy.BackoffMs;
        RemoteIdempotentRetryPolicy.DelayAsync = _ => Task.CompletedTask;
        RemoteIdempotentRetryPolicy.BackoffMs = 0;
    }

    [TearDown]
    public void TearDown()
    {
        RemoteIdempotentRetryPolicy.DelayAsync = _originalDelay;
        RemoteIdempotentRetryPolicy.BackoffMs = _originalBackoff;
    }

    [Test]
    public void HasPendingServerAuthoritativeExtraction_DetectsValidUnconsumedReceipt()
    {
        var inventoryWithPending = new InventoryData
        {
            pendingAuthoritativeExtraction = new ExtractionReceiptData
            {
                raidId = "RAID-FIX09",
                resultSequence = 2
            }
        };

        Assert.IsTrue(ApplicationStashServiceBootstrapper.HasPendingServerAuthoritativeExtraction(
            inventoryWithPending, out ExtractionReceiptData receipt));
        Assert.AreEqual("RAID-FIX09", receipt.raidId);
        Assert.AreEqual(2, receipt.resultSequence);

        var inventoryWithoutPending = new InventoryData();
        Assert.IsFalse(ApplicationStashServiceBootstrapper.HasPendingServerAuthoritativeExtraction(
            inventoryWithoutPending, out _));
    }

    [Test]
    public void RecoverPendingAuthoritativeExtraction_RetriesTransientFailure_AndReconcilesAuthoritativeState()
    {
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("NETWORK_ERROR"));

        var sentRequests = new List<CommitExtractionUnifiedRequest>();
        bool reconciled = false;

        var receipt = new ExtractionReceiptData { raidId = "RAID-FIX09", resultSequence = 1 };
        bool ok = ApplicationStashServiceBootstrapper.RecoverPendingAuthoritativeExtractionAsync(
            receipt,
            request =>
            {
                sentRequests.Add(request);
                if (sentRequests.Count == 1)
                {
                    return Task.FromResult<(bool, CommitExtractionUnifiedResult, BackendError)>(
                        (false, default, new BackendError { error = "NETWORK_ERROR", message = "Transient drop" }));
                }

                return Task.FromResult((true, new CommitExtractionUnifiedResult
                {
                    alreadySecured = false,
                    revision = 7
                }, default(BackendError)));
            },
            () =>
            {
                reconciled = true;
                return Task.FromResult(true);
            }).GetAwaiter().GetResult();

        Assert.IsTrue(ok);
        Assert.AreEqual(2, sentRequests.Count);
        Assert.AreEqual("RAID-FIX09", sentRequests[0].raidId);
        Assert.AreEqual(1, sentRequests[0].resultSequence);
        Assert.AreEqual("RAID-FIX09", sentRequests[1].raidId);
        Assert.AreEqual(1, sentRequests[1].resultSequence);
        Assert.IsTrue(reconciled, "Local profile must reconcile authoritative inventory after recovery commit.");
    }
}

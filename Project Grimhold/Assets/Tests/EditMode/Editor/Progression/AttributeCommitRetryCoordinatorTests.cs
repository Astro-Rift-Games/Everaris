using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class AttributeCommitRetryCoordinatorTests
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
    public void LostResponseRetry_SendsSameTransactionIdAndRevision_AndConvergesWithoutDoubleSpend()
    {
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("NETWORK_ERROR"));

        var sent = new List<CommitProgressionRequest>();
        int serverStrength = 10;
        int serverAvailable = 3;
        int serverRevision = 5;
        string appliedTxId = null;

        var task = AttributeCommitRetryCoordinator.ExecuteAsync(
            "Strength",
            expectedRevision: 5,
            request =>
            {
                sent.Add(request);
                if (sent.Count == 1)
                {
                    // Simulate backend applying the spend, then losing the HTTP response in transit.
                    serverStrength += 1;
                    serverAvailable -= 1;
                    serverRevision += 1;
                    appliedTxId = request.transactionId;
                    return Task.FromResult<(bool, CommitProgressionResult, BackendError)>(
                        (false, default, new BackendError { error = "NETWORK_ERROR", message = "Connection dropped" }));
                }

                // Retry with the exact same transactionId replays the already-secured result without double spending.
                Assert.That(request.transactionId, Is.EqualTo(appliedTxId));
                return Task.FromResult((true, new CommitProgressionResult
                {
                    alreadySecured = true,
                    transactionId = request.transactionId,
                    characterAttributes = new CharacterAttributesData
                    {
                        strength = serverStrength,
                        availablePoints = serverAvailable
                    },
                    revision = serverRevision
                }, default(BackendError)));
            },
            createTransactionId: () => "1730000000000_0123456789abcdef0123456789abcdef");

        var (success, result, error) = task.GetAwaiter().GetResult();

        Assert.That(success, Is.True);
        Assert.That(error.error, Is.Null.Or.Empty);
        Assert.That(sent.Count, Is.EqualTo(2));
        Assert.That(sent[0].transactionId, Is.EqualTo(sent[1].transactionId));
        Assert.That(sent[0].expectedRevision, Is.EqualTo(5));
        Assert.That(sent[1].expectedRevision, Is.EqualTo(5));
        Assert.That(result.alreadySecured, Is.True);
        Assert.That(result.revision, Is.EqualTo(6));
        Assert.That(result.characterAttributes.strength, Is.EqualTo(11));
        Assert.That(result.characterAttributes.availablePoints, Is.EqualTo(2));
        Assert.That(serverStrength, Is.EqualTo(11), "Point must only be applied once on the backend.");
    }

    [Test]
    public void NonRetryableRevisionConflict_DoesNotRetry_AndReturnsConflictError()
    {
        int calls = 0;
        var task = AttributeCommitRetryCoordinator.ExecuteAsync(
            "Vitality",
            expectedRevision: 2,
            request =>
            {
                calls++;
                return Task.FromResult<(bool, CommitProgressionResult, BackendError)>(
                    (false, default, new BackendError { error = "REVISION_CONFLICT", message = "Revision mismatch" }));
            },
            createTransactionId: () => "1730000000001_0123456789abcdef0123456789abcdef");

        var (success, _, error) = task.GetAwaiter().GetResult();

        Assert.That(success, Is.False);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(error.error, Is.EqualTo("REVISION_CONFLICT"));
    }

    [Test]
    public void FirstAttemptSuccess_SendsTimestampedShopTransactionId_Once()
    {
        CommitProgressionRequest captured = default;
        int calls = 0;

        var task = AttributeCommitRetryCoordinator.ExecuteAsync(
            "Luck",
            expectedRevision: 9,
            request =>
            {
                calls++;
                captured = request;
                return Task.FromResult((true, new CommitProgressionResult
                {
                    alreadySecured = false,
                    transactionId = request.transactionId,
                    characterAttributes = new CharacterAttributesData { luck = 4, availablePoints = 1 },
                    revision = 10
                }, default(BackendError)));
            });

        var (success, result, _) = task.GetAwaiter().GetResult();

        Assert.That(success, Is.True);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(System.Text.RegularExpressions.Regex.IsMatch(captured.transactionId ?? "", @"^\d{1,15}_[0-9a-f]{32}$"), Is.True,
            "Generated transactionId must use ShopTransactionId timestamped format for backend retention.");
        Assert.That(result.revision, Is.EqualTo(10));
    }
}

using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// FIX-07: Party continuation restoration uses strict backoff intervals (ACK does not trigger immediate
/// re-send storms) and expires after a bounded window so the player transitions to a usable Solo Party.
/// </summary>
[TestFixture]
public sealed class TownPartyContinuationRetryPolicyTests
{
    private static readonly ProfileId Host = new ProfileId("host_fix07");
    private static readonly ProfileId Client = new ProfileId("client_fix07");

    private TownPartyContinuationContext _duo;

    [SetUp]
    public void SetUp()
    {
        Assert.IsTrue(TownPartyContinuationContext.TryCreate(Host, new[] { Host, Client }, out _duo));
    }

    [Test]
    public void AcknowledgedPending_EnforcesBackoff_AndNeverTriggersImmediateResendLoop()
    {
        var policy = new TownPartyContinuationRetryPolicy(
            baseIntervalSeconds: 1.0f,
            maxIntervalSeconds: 5.0f,
            expirationSeconds: 30.0f);

        // First request sent immediately on returning to Town at t=0.
        Assert.AreEqual(TownPartyContinuationRetryPolicy.PollAction.SendRestoreRequest, policy.Evaluate(_duo, now: 0f));

        // Authority responds with ACK while partner is still in Dungeon at t=0.05.
        policy.OnAcknowledgedPending(now: 0.05f);

        // Polling every frame between t=0.06 and t=1.0 MUST wait (no RTT ping-pong flood!).
        Assert.AreEqual(TownPartyContinuationRetryPolicy.PollAction.Wait, policy.Evaluate(_duo, now: 0.06f));
        Assert.AreEqual(TownPartyContinuationRetryPolicy.PollAction.Wait, policy.Evaluate(_duo, now: 0.50f));
        Assert.AreEqual(TownPartyContinuationRetryPolicy.PollAction.Wait, policy.Evaluate(_duo, now: 1.04f));

        // After the 1.0s interval from ACK (t=1.05): at most one retry is emitted.
        Assert.AreEqual(TownPartyContinuationRetryPolicy.PollAction.SendRestoreRequest, policy.Evaluate(_duo, now: 1.06f));
        Assert.AreEqual(2, policy.Attempts);

        // Next backoff doubles to 2.0s.
        policy.OnAcknowledgedPending(now: 1.10f);
        Assert.AreEqual(TownPartyContinuationRetryPolicy.PollAction.Wait, policy.Evaluate(_duo, now: 2.50f));
        Assert.AreEqual(TownPartyContinuationRetryPolicy.PollAction.SendRestoreRequest, policy.Evaluate(_duo, now: 3.15f));
    }

    [Test]
    public void ExpirationDeadline_TransitionsToExpired_SoPlayerObtainsUsableSoloParty()
    {
        var policy = new TownPartyContinuationRetryPolicy(
            baseIntervalSeconds: 1.0f,
            maxIntervalSeconds: 4.0f,
            expirationSeconds: 10.0f);

        Assert.AreEqual(TownPartyContinuationRetryPolicy.PollAction.SendRestoreRequest, policy.Evaluate(_duo, now: 0f));
        Assert.AreEqual(TownPartyContinuationRetryPolicy.PollAction.Wait, policy.Evaluate(_duo, now: 0.5f));

        // Past 10.0s window -> Expired!
        Assert.AreEqual(TownPartyContinuationRetryPolicy.PollAction.Expired, policy.Evaluate(_duo, now: 10.0f));
        Assert.IsFalse(policy.HasActiveWait);
    }

    [Test]
    public void BothMembersReturnWithinWindow_RestoresDuoWithoutExpiration()
    {
        var policy = new TownPartyContinuationRetryPolicy(baseIntervalSeconds: 1.0f, maxIntervalSeconds: 5.0f, expirationSeconds: 30.0f);
        var claims = new TownPartyContinuationClaimRegistry();

        // Client returns at t=0 and submits claim.
        Assert.AreEqual(TownPartyContinuationRetryPolicy.PollAction.SendRestoreRequest, policy.Evaluate(_duo, now: 0f));
        Assert.AreEqual(TownPartyContinuationClaimResult.Pending, claims.Submit(Client, _duo));
        policy.OnAcknowledgedPending(now: 0.1f);

        // Host returns at t=5s (well inside 30s expiration) -> Duo restores!
        Assert.AreEqual(TownPartyContinuationClaimResult.ReadyToRestore, claims.Submit(Host, _duo));
        Assert.IsTrue(claims.MarkRestored(_duo));
        policy.Reset();
        Assert.IsFalse(policy.HasActiveWait);
    }
}

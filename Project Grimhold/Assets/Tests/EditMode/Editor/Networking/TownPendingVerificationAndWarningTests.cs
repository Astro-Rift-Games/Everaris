using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;
using LogType = UnityEngine.LogType;

/// <summary>
/// FIX-59: Town RPCs arriving while ticket verification is still in flight must be queued and
/// executed once verification finishes (or report an explicit error if verification fails/times out),
/// and repeated pending/missing-ticket probes must NOT spam warnings per tick.
/// </summary>
[TestFixture]
public sealed class TownPendingVerificationAndWarningTests
{
    private static readonly PlayerRef Sender = PlayerRef.FromIndex(4);
    private static readonly ProfileId SenderProfile = new ProfileId("sender_profile_fix59");
    private static readonly Func<PlayerRef, bool> EveryoneActive = _ => true;

    [Test]
    public void PendingVerification_QueuesInviteAction_AndProcessesOnceVerifierCompletes()
    {
        var verifier = new PendingControllableVerifier();
        var registry = new TownSenderIdentityRegistry();
        string ticket = TownSessionTicketCodec.CreateTestTicket(SenderProfile, "Fix59Player");
        registry.Register(Sender, ticket, verifier, EveryoneActive);

        var queue = new TownPendingRpcQueue(timeoutSeconds: 5f);
        ProfileId executedFor = default;
        bool rejected = false;

        // First probe while verification is still pending -> status is PendingVerification, warning logged ONCE.
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[SECURITY\].*pending verification"));
        Assert.IsFalse(registry.TryResolveWithStatus(Sender, verifier, out _, out TownSenderResolutionStatus status));
        Assert.AreEqual(TownSenderResolutionStatus.PendingVerification, status);

        Assert.IsTrue(queue.Enqueue(Sender, now: 0f, profile => executedFor = profile, () => rejected = true));

        // Multiple ticks pass while still pending -> NO additional warnings, action stays queued.
        queue.Drain(now: 0.5f, (PlayerRef s, out ProfileId p, out TownSenderResolutionStatus st) =>
            registry.TryResolveWithStatus(s, verifier, out p, out st));
        queue.Drain(now: 1.0f, (PlayerRef s, out ProfileId p, out TownSenderResolutionStatus st) =>
            registry.TryResolveWithStatus(s, verifier, out p, out st));

        Assert.AreEqual(default(ProfileId), executedFor);
        Assert.IsFalse(rejected);
        Assert.AreEqual(1, queue.Count);

        // Verification completes successfully -> queued invite executes immediately!
        verifier.Complete(ticket, valid: true, SenderProfile);
        queue.Drain(now: 1.2f, (PlayerRef s, out ProfileId p, out TownSenderResolutionStatus st) =>
            registry.TryResolveWithStatus(s, verifier, out p, out st));

        Assert.AreEqual(SenderProfile, executedFor, "Queued Town RPC must execute as soon as verification completes.");
        Assert.IsFalse(rejected);
        Assert.AreEqual(0, queue.Count);
    }

    [Test]
    public void PendingVerification_RejectedByBackend_InvokesExplicitErrorCallback_NeverSilentlyDrops()
    {
        var verifier = new PendingControllableVerifier();
        var registry = new TownSenderIdentityRegistry();
        string forged = TownSessionTicketCodec.CreateTestTicket(SenderProfile, "ForgedFix59");
        registry.Register(Sender, forged, verifier, EveryoneActive);

        var queue = new TownPendingRpcQueue(timeoutSeconds: 5f);
        bool executed = false;
        bool rejected = false;
        queue.Enqueue(Sender, now: 0f, _ => executed = true, () => rejected = true);

        verifier.Complete(forged, valid: false, default, error: "INVALID_SIGNATURE");
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[SECURITY\].*Verifier rejected"));

        queue.Drain(now: 0.5f, (PlayerRef s, out ProfileId p, out TownSenderResolutionStatus st) =>
            registry.TryResolveWithStatus(s, verifier, out p, out st));

        Assert.IsFalse(executed);
        Assert.IsTrue(rejected, "A failed verification must invoke the rejection feedback callback, never silently drop.");
        Assert.AreEqual(0, queue.Count);
    }

    [Test]
    public void PendingVerification_TimedOut_InvokesExplicitErrorCallback()
    {
        var verifier = new PendingControllableVerifier();
        var registry = new TownSenderIdentityRegistry();
        string ticket = TownSessionTicketCodec.CreateTestTicket(SenderProfile, "SlowFix59");
        registry.Register(Sender, ticket, verifier, EveryoneActive);

        var queue = new TownPendingRpcQueue(timeoutSeconds: 3f);
        bool executed = false;
        bool rejected = false;
        queue.Enqueue(Sender, now: 10f, _ => executed = true, () => rejected = true);

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[SECURITY\].*pending verification"));
        queue.Drain(now: 12.9f, (PlayerRef s, out ProfileId p, out TownSenderResolutionStatus st) =>
            registry.TryResolveWithStatus(s, verifier, out p, out st));
        Assert.IsFalse(rejected);
        Assert.AreEqual(1, queue.Count);

        // Past timeout (10 + 3 = 13) -> cleanly rejects with feedback and no extra warning spam.
        queue.Drain(now: 13.1f, (PlayerRef s, out ProfileId p, out TownSenderResolutionStatus st) =>
            registry.TryResolveWithStatus(s, verifier, out p, out st));
        Assert.IsFalse(executed);
        Assert.IsTrue(rejected);
        Assert.AreEqual(0, queue.Count);
    }

    [Test]
    public void RepeatedMissingOrPendingProbes_LogOnlyOneConsolidatedWarningPerEpisode()
    {
        var gate = new TownSecurityWarningGate();
        var verifier = new PendingControllableVerifier();
        string ticket = TownSessionTicketCodec.CreateTestTicket(SenderProfile, "NoSpam");

        // Expect EXACTLY ONE warning even across 10 consecutive probes while pending.
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[SECURITY\].*pending verification"));
        for (int i = 0; i < 10; i++)
        {
            bool ok = TownSenderIdentityValidator.TryValidateSenderIdentity(
                SenderProfile.Value,
                ticket,
                verifier,
                "PlayerRef 4",
                out _,
                out TownSenderResolutionStatus status,
                gate);
            Assert.IsFalse(ok);
            Assert.AreEqual(TownSenderResolutionStatus.PendingVerification, status);
        }

        // Expect EXACTLY ONE warning across 10 consecutive probes with no ticket.
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[SECURITY\].*no SessionTicket"));
        for (int i = 0; i < 10; i++)
        {
            bool ok = TownSenderIdentityValidator.TryValidateSenderIdentity(
                SenderProfile.Value,
                string.Empty,
                verifier,
                "PlayerRef 4",
                out _,
                out TownSenderResolutionStatus status,
                gate);
            Assert.IsFalse(ok);
            Assert.AreEqual(TownSenderResolutionStatus.RejectedNoTicket, status);
        }
    }

    private sealed class PendingControllableVerifier : ITownSessionTicketVerifier
    {
        private readonly Dictionary<string, TaskCompletionSource<(bool valid, ProfileId profileId, string characterName, string error)>> _tasks =
            new Dictionary<string, TaskCompletionSource<(bool, ProfileId, string, string)>>(StringComparer.Ordinal);

        public void Complete(string rawTicket, bool valid, ProfileId profileId, string error = null) =>
            Source(rawTicket).TrySetResult((valid, valid ? profileId : default, string.Empty, valid ? null : (error ?? "REJECTED")));

        public Task<(bool valid, ProfileId profileId, string characterName, string error)> VerifyTownTicketAsync(string rawTicket) =>
            Source(rawTicket).Task;

        private TaskCompletionSource<(bool, ProfileId, string, string)> Source(string rawTicket)
        {
            if (!_tasks.TryGetValue(rawTicket, out var source))
            {
                source = new TaskCompletionSource<(bool, ProfileId, string, string)>();
                _tasks[rawTicket] = source;
            }
            return source;
        }
    }
}

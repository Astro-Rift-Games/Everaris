using System;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// FIX-94: A peer that loses and regains Town authority must discard residual bindings from prior
/// authority terms, and a client whose registration was rejected or unconfirmed must retry with
/// bounded backoff without changing ticket or reconnecting.
/// </summary>
[TestFixture]
public sealed class TownSenderAuthorityHardeningTests
{
    private static readonly PlayerRef PeerA = PlayerRef.FromIndex(1);
    private static readonly PlayerRef PeerB = PlayerRef.FromIndex(2);
    private static readonly PlayerRef Client = PlayerRef.FromIndex(3);
    private static readonly ProfileId ClientProfile = new ProfileId("client_profile_fix94");
    private static readonly Func<PlayerRef, bool> EveryoneActive = _ => true;

    private GameObject _runnerObject;
    private TownSenderIdentityContext _context;
    private MockTownSessionTicketVerifier _verifier;
    private string _clientTicket;

    [SetUp]
    public void SetUp()
    {
        _runnerObject = new GameObject("Fix94TownSenderIdentityContext");
        _context = _runnerObject.AddComponent<TownSenderIdentityContext>();
        _clientTicket = TownSessionTicketCodec.CreateTestTicket(ClientProfile, "ClientFix94");
        _verifier = new MockTownSessionTicketVerifier();
        _verifier.RegisterTicket(_clientTicket, ClientProfile, "ClientFix94");
        _context.TicketVerifier = _verifier;
    }

    [TearDown]
    public void TearDown()
    {
        if (_runnerObject != null)
        {
            UnityEngine.Object.DestroyImmediate(_runnerObject);
        }
    }

    [Test]
    public void PeerLosingAndRegainingTownAuthority_DoesNotResolvePreviousBindings_UntilReRegistered()
    {
        // Peer A starts as authority and receives Client's registration.
        _context.UpdateAuthorityOwnership(true);
        Assert.AreEqual(TownSenderRegistrationResult.Accepted,
            _context.AcceptRegistration(Client, _clientTicket, EveryoneActive));
        Assert.IsTrue(_context.Registry.TryResolve(Client, _verifier, out ProfileId resolvedBefore));
        Assert.AreEqual(ClientProfile, resolvedBefore);

        // Peer A loses Town authority -> residual bindings MUST be discarded.
        _context.UpdateAuthorityOwnership(false);
        Assert.IsFalse(_context.Registry.TryResolve(Client, _verifier, out _),
            "Bindings must be discarded when losing Town authority.");

        // Peer A regains Town authority later -> must NOT resolve Client until Client re-registers.
        _context.UpdateAuthorityOwnership(true);
        Assert.IsFalse(_context.Registry.TryResolve(Client, _verifier, out _),
            "A regained Town authority must not trust bindings from a prior term.");

        // Once Client re-registers under the new term, resolution succeeds again.
        Assert.AreEqual(TownSenderRegistrationResult.Accepted,
            _context.AcceptRegistration(Client, _clientTicket, EveryoneActive));
        Assert.IsTrue(_context.Registry.TryResolve(Client, _verifier, out ProfileId resolvedAfter));
        Assert.AreEqual(ClientProfile, resolvedAfter);
    }

    [Test]
    public void ClientRejectedRegistration_RetriesWithBoundedBackoff_AndRecoversWithoutChangingTicket()
    {
        var tracker = new TownTicketDeliveryTracker(maxAttempts: 4, baseRetryDelaySeconds: 0.5f, unconfirmedTimeoutSeconds: 1.5f);
        var authorities = new List<PlayerRef> { PeerA };
        var due = new List<PlayerRef>();

        // Attempt 1 sent immediately at t=0.
        Assert.IsFalse(tracker.CollectDueDeliveries(_clientTicket, authorities, now: 0f, due, out _));
        Assert.AreEqual(1, due.Count);
        Assert.AreEqual(PeerA, due[0]);
        Assert.AreEqual(TownTicketDeliveryTracker.DeliveryStatus.Pending, tracker.GetStatus(PeerA));

        // Authority rejects attempt 1 at t=0.1 -> scheduled for retry at t=0.6 (backoff 0.5s).
        Assert.IsFalse(tracker.RecordResponse(PeerA, TownSenderRegistrationResult.RejectedProfileBoundToAnotherSender, now: 0.1f));
        Assert.AreEqual(TownTicketDeliveryTracker.DeliveryStatus.RetryScheduled, tracker.GetStatus(PeerA));

        // Before backoff elapses (t=0.4): must NOT spam network.
        Assert.IsFalse(tracker.CollectDueDeliveries(_clientTicket, authorities, now: 0.4f, due, out _));
        Assert.AreEqual(0, due.Count, "Client must respect backoff delay before retrying.");

        // Once backoff elapses (t=0.6): retries with the SAME ticket.
        Assert.IsFalse(tracker.CollectDueDeliveries(_clientTicket, authorities, now: 0.6f, due, out _));
        Assert.AreEqual(1, due.Count);
        Assert.AreEqual(PeerA, due[0]);
        Assert.AreEqual(2, tracker.GetAttempts(PeerA));

        // Authority accepts attempt 2 -> confirmed, no further deliveries.
        Assert.IsFalse(tracker.RecordResponse(PeerA, TownSenderRegistrationResult.Accepted, now: 0.7f));
        Assert.AreEqual(TownTicketDeliveryTracker.DeliveryStatus.Confirmed, tracker.GetStatus(PeerA));

        Assert.IsFalse(tracker.CollectDueDeliveries(_clientTicket, authorities, now: 100f, due, out _));
        Assert.AreEqual(0, due.Count, "Confirmed delivery must not resend for the same ticket and authority.");
    }

    [Test]
    public void ClientAuthorityRoundTrip_AToBToA_RedeliversTicketToRegainedAuthority()
    {
        var tracker = new TownTicketDeliveryTracker();
        var due = new List<PlayerRef>();

        // Term 1: PeerA is authority and confirms.
        tracker.CollectDueDeliveries(_clientTicket, new[] { PeerA }, now: 0f, due, out _);
        Assert.AreEqual(1, due.Count);
        tracker.RecordResponse(PeerA, TownSenderRegistrationResult.Accepted, now: 0.1f);

        // Term 2: Authority migrates to PeerB. PeerA is dropped from tracker, PeerB receives ticket.
        tracker.CollectDueDeliveries(_clientTicket, new[] { PeerB }, now: 1f, due, out _);
        Assert.AreEqual(1, due.Count);
        Assert.AreEqual(PeerB, due[0]);
        tracker.RecordResponse(PeerB, TownSenderRegistrationResult.Accepted, now: 1.1f);

        // Term 3: Authority returns to PeerA -> Client MUST re-deliver ticket to PeerA!
        tracker.CollectDueDeliveries(_clientTicket, new[] { PeerA }, now: 2f, due, out _);
        Assert.AreEqual(1, due.Count, "Client must re-register when PeerA regains authority after migrating to PeerB.");
        Assert.AreEqual(PeerA, due[0]);
    }

    [Test]
    public void ClientExhaustedRetries_StopsRetrying_WithoutSpamming()
    {
        var tracker = new TownTicketDeliveryTracker(maxAttempts: 3, baseRetryDelaySeconds: 0.5f, unconfirmedTimeoutSeconds: 1.0f);
        var authorities = new[] { PeerA };
        var due = new List<PlayerRef>();

        // Attempt 1
        tracker.CollectDueDeliveries(_clientTicket, authorities, now: 0f, due, out _);
        Assert.AreEqual(1, due.Count);
        Assert.IsFalse(tracker.RecordResponse(PeerA, TownSenderRegistrationResult.RejectedNotAuthority, now: 0.1f));

        // Attempt 2 (after 0.5s)
        tracker.CollectDueDeliveries(_clientTicket, authorities, now: 0.6f, due, out _);
        Assert.AreEqual(1, due.Count);
        Assert.IsFalse(tracker.RecordResponse(PeerA, TownSenderRegistrationResult.RejectedNotAuthority, now: 0.7f));

        // Attempt 3 (after 1.0s) -> final attempt rejected -> transitions to Exhausted ONCE.
        tracker.CollectDueDeliveries(_clientTicket, authorities, now: 1.7f, due, out _);
        Assert.AreEqual(1, due.Count);
        Assert.IsTrue(tracker.RecordResponse(PeerA, TownSenderRegistrationResult.RejectedNotAuthority, now: 1.8f));
        Assert.AreEqual(TownTicketDeliveryTracker.DeliveryStatus.Exhausted, tracker.GetStatus(PeerA));

        // No further sends at any future time.
        Assert.IsFalse(tracker.CollectDueDeliveries(_clientTicket, authorities, now: 999f, due, out _));
        Assert.AreEqual(0, due.Count);
    }

    [Test]
    public void AcknowledgeRegistrationRpc_ExistsAndIsStaticTargeted()
    {
        MethodInfo method = typeof(SocialPlayerIdentity).GetMethod(
            "RPC_AcknowledgeTownSenderRegistration", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(method, "SocialPlayerIdentity must expose RPC_AcknowledgeTownSenderRegistration.");
        Assert.IsNotNull(method.GetCustomAttribute<RpcAttribute>());

        bool hasTarget = false;
        foreach (ParameterInfo parameter in method.GetParameters())
        {
            if (parameter.GetCustomAttribute<RpcTargetAttribute>() != null && parameter.ParameterType == typeof(PlayerRef))
            {
                hasTarget = true;
            }
        }

        Assert.IsTrue(hasTarget, "Registration acknowledgement must target the registering client PlayerRef.");
    }
}

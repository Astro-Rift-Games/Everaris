using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Runner-scoped Town sender binding (FIX-54, FIX-94). The Town State Authority binds each PlayerRef
/// to the ticket that PlayerRef physically delivered by RPC; tickets are never replicated to other
/// clients. Bindings received during an earlier authority term are discarded whenever this peer
/// loses or regains Town authority, and clients retry rejected or unconfirmed registrations with
/// bounded backoff.
/// </summary>
public sealed class TownSenderIdentityContext : NetworkRunnerCallbacksAdapter
{
    private readonly List<NetworkObject> _authorityObjects = new();
    private readonly List<PlayerRef> _currentAuthoritiesScratch = new();
    private readonly List<PlayerRef> _dueAuthoritiesScratch = new();
    private readonly TownTicketDeliveryTracker _deliveryTracker = new();
    private bool _holdsTownAuthority;

    public TownSenderIdentityRegistry Registry { get; } = new();
    public TownTicketDeliveryTracker DeliveryTracker => _deliveryTracker;

    /// <summary>Optional verifier override (tests); defaults to the application Town ticket verifier.</summary>
    public ITownSessionTicketVerifier TicketVerifier { get; set; }

    public bool HasRegisteredAuthorityObjects => _authorityObjects.Count > 0;

    public bool HasAnyStateAuthority()
    {
        for (int index = _authorityObjects.Count - 1; index >= 0; index--)
        {
            NetworkObject authorityObject = _authorityObjects[index];
            if (authorityObject == null)
            {
                _authorityObjects.RemoveAt(index);
                continue;
            }

            if (authorityObject.IsValid && authorityObject.HasStateAuthority)
            {
                return true;
            }
        }

        return false;
    }

    public void RegisterAuthorityObject(NetworkObject authorityObject)
    {
        if (authorityObject == null || _authorityObjects.Contains(authorityObject))
        {
            return;
        }

        _authorityObjects.Add(authorityObject);
        UpdateAuthorityOwnership(HasAnyStateAuthority());
    }

    public void UnregisterAuthorityObject(NetworkObject authorityObject)
    {
        if (_authorityObjects.Remove(authorityObject))
        {
            UpdateAuthorityOwnership(HasAnyStateAuthority());
        }
    }

    /// <summary>
    /// Called by Town authority NetworkBehaviours in <c>StateAuthorityChanged</c> (FIX-94).
    /// Discards all bindings from any previous authority term so a peer that loses and later regains
    /// authority never trusts stale pre-migration bindings until clients re-register.
    /// </summary>
    public void NotifyAuthorityChanged()
    {
        UpdateAuthorityOwnership(HasAnyStateAuthority());
    }

    /// <summary>
    /// Transitions authority ownership explicitly (also used by EditMode tests without a live runner).
    /// Any transition out of or into authority clears residual bindings from prior terms.
    /// </summary>
    public void UpdateAuthorityOwnership(bool isCurrentlyAuthority)
    {
        if (_holdsTownAuthority == isCurrentlyAuthority)
        {
            return;
        }

        _holdsTownAuthority = isCurrentlyAuthority;
        Registry.Clear();
    }

    public TownSenderRegistrationResult AcceptRegistration(
        PlayerRef sender,
        string rawTicket,
        Func<PlayerRef, bool> isSenderActive)
    {
        if (HasRegisteredAuthorityObjects && !HasAnyStateAuthority())
        {
            return TownSenderRegistrationResult.RejectedNotAuthority;
        }

        ITownSessionTicketVerifier verifier = TicketVerifier ?? ApplicationAuthContext.Instance?.TownTicketVerifier;
        TownSenderRegistrationResult result = Registry.Register(sender, rawTicket, verifier, isSenderActive);
        if (result == TownSenderRegistrationResult.Accepted)
        {
            verifier?.VerifyTownTicketAsync(rawTicket);
        }

        return result;
    }

    /// <summary>
    /// Records the authority's acknowledgement/rejection of our ticket registration (FIX-94).
    /// Rejected deliveries are scheduled for bounded retry without changing ticket or reconnecting.
    /// </summary>
    public void OnRegistrationAcknowledged(PlayerRef authority, TownSenderRegistrationResult result)
    {
        if (_deliveryTracker.RecordResponse(authority, result, Time.unscaledTime))
        {
            Debug.LogWarning(
                $"[SECURITY] Town sender registration with authority {authority} exhausted retries (last result: {result}).");
        }
    }

    /// <summary>
    /// Delivers the local ticket to every distinct Town State Authority, retrying rejected or
    /// unconfirmed deliveries with bounded backoff and re-delivering after authority round-trips (FIX-94).
    /// </summary>
    public void PublishLocalTicket(NetworkRunner runner, string rawTicket)
    {
        if (runner == null || string.IsNullOrWhiteSpace(rawTicket))
        {
            return;
        }

        _currentAuthoritiesScratch.Clear();
        for (int index = _authorityObjects.Count - 1; index >= 0; index--)
        {
            NetworkObject authorityObject = _authorityObjects[index];
            if (authorityObject == null)
            {
                _authorityObjects.RemoveAt(index);
                continue;
            }

            if (!authorityObject.IsValid)
            {
                continue;
            }

            // Single mode reports StateAuthority as None while HasStateAuthority is true.
            PlayerRef authority = authorityObject.HasStateAuthority ? runner.LocalPlayer : authorityObject.StateAuthority;
            if (!authority.IsNone && !_currentAuthoritiesScratch.Contains(authority))
            {
                _currentAuthoritiesScratch.Add(authority);
            }
        }

        bool anyExhausted = _deliveryTracker.CollectDueDeliveries(
            rawTicket,
            _currentAuthoritiesScratch,
            Time.unscaledTime,
            _dueAuthoritiesScratch,
            out PlayerRef exhaustedAuthority);

        if (anyExhausted)
        {
            Debug.LogWarning(
                $"[SECURITY] Town sender registration with authority {exhaustedAuthority} was not confirmed after bounded retries.");
        }

        for (int i = 0; i < _dueAuthoritiesScratch.Count; i++)
        {
            PlayerRef authority = _dueAuthoritiesScratch[i];
            if (authority == runner.LocalPlayer)
            {
                TownSenderRegistrationResult localResult = AcceptRegistration(
                    runner.LocalPlayer,
                    rawTicket,
                    player => IsActive(runner, player));
                OnRegistrationAcknowledged(authority, localResult);
                continue;
            }

            SocialPlayerIdentity.RPC_RegisterTownSender(runner, authority, rawTicket);
        }
    }

    public static bool IsActive(NetworkRunner runner, PlayerRef player)
    {
        if (runner == null)
        {
            return false;
        }

        foreach (PlayerRef active in runner.ActivePlayers)
        {
            if (active == player)
            {
                return true;
            }
        }

        return false;
    }

    public override void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Registry.Remove(player);
        _deliveryTracker.RemoveAuthority(player);
    }

    public override void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Registry.Clear();
        _authorityObjects.Clear();
        _deliveryTracker.Clear();
        _holdsTownAuthority = false;
    }
}

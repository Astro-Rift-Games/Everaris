using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>Authoritative Town source of truth for Solo/Duo Parties, invitations and restoration.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class TownPartyDirectory : NetworkBehaviour, IPlayerLeft, IStateAuthorityChanged
{
    private const float InvitationTimeoutSeconds = 20f;
    private const float InvitationCooldownSeconds = 5f;
    private const float LocalEnsurePartyRetryIntervalSeconds = 0.5f;
    public const string MissingTownTicketMessage = "No se pudo verificar tu sesión en Town. Volvé a iniciar sesión.";

    [Networked] private int NextPartyId { get; set; }
    [Networked] private int NextInvitationId { get; set; }
    [Networked] public int StateEpoch { get; private set; }
    [Networked, Capacity(RaidSessionRules.MaxParticipants)] private NetworkArray<TownPartyNetworkEntry> Parties => default;
    [Networked, Capacity(RaidSessionRules.MaxParticipants)] private NetworkArray<TownPartyInvitationNetworkEntry> Invitations => default;
    [Networked, Capacity(RaidSessionRules.MaxParticipants)] private NetworkArray<TownPartyInvitationCooldownEntry> InvitationCooldowns => default;

    private readonly Dictionary<PlayerRef, ProfileId> _profileByPlayer = new();
    private readonly Dictionary<ProfileId, string> _knownDisplayNames = new();
    private readonly TownPreparationDisconnectResolver _disconnectResolver = new();
    private readonly Queue<TownPartyInvitationResultEvent> _resultEvents = new();
    private readonly Queue<TownPartyLeaveResult> _leaveResults = new();
    private readonly Queue<string> _identityErrors = new();
    private readonly TownPartyContinuationClaimRegistry _continuationClaims = new();
    private readonly TownPartyContinuationRetryPolicy _continuationRetryPolicy = new();
    private readonly TownPendingRpcQueue _pendingRpcs = new();
    private int _observedEpoch = -1;
    private bool _localRequestPending;
    private float _nextLocalEnsureRetryTime;
    private bool _localMissingTicketReported;
    private bool _localEnsureStoppedByTerminalError;
    private string _observedLocalTicket;

    public event Action<TownPartyInvitationResultEvent> InvitationResultReceived;
    public event Action<TownPartyLeaveResult> LeaveResultReceived;
    /// <summary>Raised on the local client once per episode when Town identity verification fails or no ticket exists (FIX-59).</summary>
    public event Action<string> IdentityErrorReceived;
    public int PartyCount { get { int count = 0; for (int i = 0; i < RaidSessionRules.MaxParticipants; i++) if (Parties[i].IsOccupied) count++; return count; } }

    public override void Spawned()
    {
        Runner.GetComponent<TownPartyDirectoryContext>()?.Register(this);
        Runner.GetComponent<TownSenderIdentityContext>()?.RegisterAuthorityObject(Object);
        if (HasStateAuthority) AdvanceEpoch();
        _localRequestPending = true;
        _nextLocalEnsureRetryTime = 0f;
    }

    public override void Render()
    {
        if (_observedEpoch != StateEpoch)
        {
            _observedEpoch = StateEpoch;
            _localRequestPending = true;
            _nextLocalEnsureRetryTime = 0f;
            _continuationRetryPolicy.OnAuthorityEpochChanged();
        }
        ObserveLocalParty();
        // Dequeue first: `?.` skips argument evaluation when nothing is subscribed.
        while (_resultEvents.Count > 0)
        {
            TownPartyInvitationResultEvent resultEvent = _resultEvents.Dequeue();
            InvitationResultReceived?.Invoke(resultEvent);
        }

        while (_leaveResults.Count > 0)
        {
            TownPartyLeaveResult leaveResult = _leaveResults.Dequeue();
            LeaveResultReceived?.Invoke(leaveResult);
        }

        while (_identityErrors.Count > 0)
        {
            string identityError = _identityErrors.Dequeue();
            IdentityErrorReceived?.Invoke(identityError);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        ObserveActivePlayers();
        _pendingRpcs.Drain(Time.unscaledTime, TryResolveSenderWithStatus);
        for (int index = 0; index < RaidSessionRules.MaxParticipants; index++)
        {
            TownPartyInvitationNetworkEntry invitation = Invitations[index];
            if (invitation.IsPending && invitation.ExpiresAt.Expired(Runner))
                ResolveInvitation(index, invitation, TownPartyInvitationResult.Expired);
            TownPartyInvitationCooldownEntry cooldown = InvitationCooldowns[index];
            if (cooldown.IsActive && cooldown.ExpiresAt.Expired(Runner)) InvitationCooldowns.Set(index, default);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        runner.GetComponent<TownPartyDirectoryContext>()?.Unregister(this);
        runner.GetComponent<TownSenderIdentityContext>()?.UnregisterAuthorityObject(Object);
        _profileByPlayer.Clear();
        _knownDisplayNames.Clear();
        _disconnectResolver.ClearAll();
        _resultEvents.Clear();
        _leaveResults.Clear();
        _identityErrors.Clear();
        _continuationClaims.Clear();
        _continuationRetryPolicy.Reset();
        _pendingRpcs.Clear();
        _localRequestPending = false;
        _nextLocalEnsureRetryTime = 0f;
        _localMissingTicketReported = false;
        _localEnsureStoppedByTerminalError = false;
        _observedLocalTicket = null;
    }

    public void StateAuthorityChanged()
    {
        Runner?.GetComponent<TownSenderIdentityContext>()?.NotifyAuthorityChanged();
        _profileByPlayer.Clear();
        _disconnectResolver.ClearVerified();
        _pendingRpcs.Clear();
        _localEnsureStoppedByTerminalError = false;
        if (!HasStateAuthority) return;
        ObserveActivePlayers();
        _continuationClaims.Clear();
        AdvanceEpoch();
    }

    /// <summary>
    /// FIX-46: Removes a disconnected player from their Town Party and cancels their pending invitations,
    /// leaving a Duo partner in a valid Solo Party on the next authoritative tick — unless the player is
    /// leaving for an active launching expedition of that Party, in which case the Party preserves them.
    /// </summary>
    public void PlayerLeft(PlayerRef player)
    {
        _pendingRpcs.RemoveSender(player);
        _profileByPlayer.Remove(player);
        if (!HasStateAuthority) return;

        bool resolved = _disconnectResolver.TryResolveDisconnectedProfile(
            player,
            Runner != null ? Runner.ActivePlayers : null,
            TryResolveVerifiedProfileOrDefault,
            TryReadDeclaredProfileOrDefault,
            out ProfileId disconnectedProfile);
        if (!resolved) return;

        bool isInLaunchingExpedition = IsProfileInLaunchingPreparation(disconnectedProfile);
        TownPartyDisconnectPolicy.DisconnectDisposition disposition =
            TownPartyDisconnectPolicy.Evaluate(disconnectedProfile, isInLaunchingExpedition);
        if (disposition != TownPartyDisconnectPolicy.DisconnectDisposition.LeavePartyAndCancelInvitations)
        {
            return;
        }

        CancelOtherInvitations(disconnectedProfile, default, 0);

        if (!TryGetPartyEntry(disconnectedProfile, out int partyIndex, out TownPartyNetworkEntry party))
        {
            return;
        }

        TownPartySnapshot snapshot = ToSnapshot(party);
        if (TownPartyDisconnectPolicy.TryResolveRemainingSoloMember(snapshot, disconnectedProfile, out ProfileId remaining))
        {
            party.HostProfileId = remaining.Value;
            party.FirstMemberProfileId = remaining.Value;
            party.SecondMemberProfileId = default;
            party.MemberCount = 1;
            party.Revision++;
            Parties.Set(partyIndex, party);
        }
        else
        {
            Parties.Set(partyIndex, default);
        }
    }

    public bool RequestInvite(ProfileId recipient) { if (!CanSend || !recipient.IsValid) return false; RPC_RequestInvite(recipient.Value); return true; }
    public bool RequestRespondToInvitation(int invitationId, bool accept) { if (!CanSend || invitationId <= 0) return false; RPC_RequestInvitationResponse(invitationId, accept); return true; }
    public bool RequestLeaveParty() { if (!CanSend) return false; RPC_RequestLeaveParty(); return true; }

    public bool TryGetParty(ProfileId profileId, out TownPartySnapshot snapshot)
    {
        if (profileId.IsValid)
        {
            for (int index = 0; index < RaidSessionRules.MaxParticipants; index++)
            {
                TownPartyNetworkEntry entry = Parties[index];
                if (entry.IsOccupied && EntryContains(entry, profileId))
                {
                    snapshot = ToSnapshot(entry);
                    return TownPartyRules.IsValid(snapshot);
                }
            }
        }
        snapshot = default;
        return false;
    }

    public bool TryGetPendingInvitation(ProfileId profileId, out TownPartyInvitationSnapshot invitation)
    {
        for (int index = 0; index < RaidSessionRules.MaxParticipants; index++)
        {
            TownPartyInvitationNetworkEntry entry = Invitations[index];
            if (entry.IsPending && (entry.InviterProfileId.ToString() == profileId.Value || entry.RecipientProfileId.ToString() == profileId.Value))
            {
                invitation = new TownPartyInvitationSnapshot(entry);
                return true;
            }
        }
        invitation = default;
        return false;
    }

    public bool TryGetDisplayName(ProfileId profileId, out string displayName)
    {
        displayName = null;
        if (TryResolvePlayer(profileId, out PlayerRef player))
        {
            NetworkObject playerObject = Runner.GetPlayerObject(player);
            if (playerObject != null && playerObject.TryGetBehaviour(out SocialPlayerIdentity identity))
            {
                displayName = identity.DisplayName.ToString();
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    _knownDisplayNames[profileId] = displayName;
                    return true;
                }
            }
        }

        if (profileId.IsValid && _knownDisplayNames.TryGetValue(profileId, out string cached) && !string.IsNullOrWhiteSpace(cached))
        {
            displayName = cached;
            return true;
        }

        return false;
    }

    /// <summary>True when <paramref name="profileId"/> is currently part of a Launching preparation (FIX-46).</summary>
    public bool IsProfileInLaunchingPreparation(ProfileId profileId)
    {
        if (!profileId.IsValid || Runner == null) return false;
        TownRaidPreparationDirectory prepDir = Runner.GetComponent<TownRaidPreparationDirectoryContext>()?.Directory;
        return prepDir != null &&
               prepDir.TryGetPreparation(profileId, out TownRaidPreparationNetworkController preparation) &&
               preparation != null &&
               preparation.State == TownRaidPreparationState.Launching;
    }

    public bool TryGetPreparedEquipment(ProfileId profileId, out IPreparedEquipmentReader reader)
    {
        reader = null;
        if (!TryResolvePlayer(profileId, out PlayerRef player)) return false;
        NetworkObject playerObject = Runner.GetPlayerObject(player);
        if (playerObject == null ||
            !playerObject.TryGetBehaviour(out SocialPlayerArmorEquipmentNetworkSource source)) return false;
        reader = source;
        return true;
    }

    /// <summary>
    /// Whether this Town player's Duo partner is still in the active Dungeon, as published by the
    /// player's own replicated identity. While true the player may not prepare a new expedition.
    /// </summary>
    public bool IsAwaitingPartyCompletion(PlayerRef player)
    {
        NetworkObject playerObject = Runner != null ? Runner.GetPlayerObject(player) : null;
        return playerObject != null && playerObject.TryGetBehaviour(out SocialPlayerIdentity identity) &&
               identity.IsAwaitingPartyCompletion;
    }

    public bool TryResolvePlayer(ProfileId profileId, out PlayerRef player)
    {
        player = PlayerRef.None;
        if (Runner == null) return false;
        if (!HasStateAuthority)
        {
            // Presentation only: the replicated claim is not authorization.
            foreach (PlayerRef active in Runner.ActivePlayers)
            {
                if (TownSenderIdentityValidator.TryResolveDeclaredProfile(Runner, active, out ProfileId declared) && declared == profileId)
                { player = active; return true; }
            }
            return false;
        }
        foreach (KeyValuePair<PlayerRef, ProfileId> pair in _profileByPlayer)
        {
            if (pair.Value == profileId) { player = pair.Key; return true; }
        }
        foreach (PlayerRef candidate in Runner.ActivePlayers)
        {
            if (!TryResolveSender(candidate, out ProfileId candidateProfile)) continue;
            _profileByPlayer[candidate] = candidateProfile;
            if (candidateProfile == profileId) { player = candidate; return true; }
        }
        return false;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private RpcInvokeInfo RPC_RequestEnsureParty(RpcInfo info = default)
    {
        if (!CanMutate) return default;
        if (TryResolveSenderWithStatus(info.Source, out ProfileId profileId, out TownSenderResolutionStatus status))
        {
            ExecuteEnsureParty(info.Source, profileId);
            return default;
        }

        if (status == TownSenderResolutionStatus.PendingVerification)
        {
            PlayerRef source = info.Source;
            _pendingRpcs.Enqueue(
                source,
                Time.unscaledTime,
                verified => ExecuteEnsureParty(source, verified),
                () => SendIdentityError(source));
            return default;
        }

        SendIdentityError(info.Source);
        return default;
    }

    private void ExecuteEnsureParty(PlayerRef source, ProfileId profileId)
    {
        if (!CanMutate || TryGetParty(profileId, out _)) return;
        NetworkObject playerObject = Runner.GetPlayerObject(source);
        if (playerObject != null && playerObject.TryGetBehaviour(out SocialPlayerIdentity identity) && identity.HasPendingPartyContinuation) return;
        _profileByPlayer[source] = profileId;
        TryCreateSoloParty(profileId);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private RpcInvokeInfo RPC_RequestInvite(NetworkString<_32> requestedRecipient, RpcInfo info = default)
    {
        if (!CanMutate) { SendInvitationResult(info.Source, TownPartyInvitationResult.Unavailable); return default; }
        ProfileId recipient = new(requestedRecipient.ToString());
        if (TryResolveSenderWithStatus(info.Source, out ProfileId inviter, out TownSenderResolutionStatus status))
        {
            ExecuteInvite(info.Source, inviter, recipient);
            return default;
        }

        if (status == TownSenderResolutionStatus.PendingVerification)
        {
            PlayerRef source = info.Source;
            if (!_pendingRpcs.Enqueue(
                    source,
                    Time.unscaledTime,
                    verified => ExecuteInvite(source, verified, recipient),
                    () => SendInvitationResult(source, TownPartyInvitationResult.Unavailable)))
            {
                SendInvitationResult(source, TownPartyInvitationResult.Unavailable);
            }
            return default;
        }

        SendInvitationResult(info.Source, TownPartyInvitationResult.Unavailable);
        return default;
    }

    private void ExecuteInvite(PlayerRef source, ProfileId inviter, ProfileId recipient)
    {
        if (!CanMutate || !recipient.IsValid || inviter == recipient || !TryResolvePlayer(recipient, out _))
        { SendInvitationResult(source, TownPartyInvitationResult.Unavailable); return; }
        if (HasPendingInvitation(inviter))
        { SendInvitationResult(source, TownPartyInvitationResult.InviterAlreadyHasPending); return; }
        if (HasPendingInvitation(recipient))
        { SendInvitationResult(source, TownPartyInvitationResult.RecipientHasPending); return; }

        if (!TryGetPartyEntry(inviter, out _, out TownPartyNetworkEntry inviterParty) ||
            !TryGetPartyEntry(recipient, out _, out TownPartyNetworkEntry recipientParty))
        { SendInvitationResult(source, TownPartyInvitationResult.Busy); return; }

        TownPartySnapshot inviterSnapshot = ToSnapshot(inviterParty);
        TownPartySnapshot recipientSnapshot = ToSnapshot(recipientParty);
        if (!TownPartyRules.IsSolo(inviterSnapshot)) { SendInvitationResult(source, TownPartyInvitationResult.PartyFull); return; }
        if (!TownPartyRules.IsSolo(recipientSnapshot)) { SendInvitationResult(source, TownPartyInvitationResult.AlreadyGrouped); return; }
        if (HasActiveCooldown(inviter, recipient) || !HasReservedCooldownCapacity())
        { SendInvitationResult(source, TownPartyInvitationResult.Cooldown); return; }

        for (int index = 0; index < RaidSessionRules.MaxParticipants; index++)
        {
            if (Invitations[index].IsPending) continue;
            int id = NextInvitationId == int.MaxValue ? 1 : NextInvitationId + 1;
            NextInvitationId = id;
            Invitations.Set(index, new TownPartyInvitationNetworkEntry
            {
                InvitationId = id,
                InviterProfileId = inviter.Value,
                RecipientProfileId = recipient.Value,
                InviterPartyId = inviterParty.PartyId,
                InviterPartyRevision = inviterParty.Revision,
                RecipientPartyId = recipientParty.PartyId,
                RecipientPartyRevision = recipientParty.Revision,
                ExpiresAt = TickTimer.CreateFromSeconds(Runner, InvitationTimeoutSeconds)
            });
            return;
        }
        SendInvitationResult(source, TownPartyInvitationResult.Busy);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private RpcInvokeInfo RPC_RequestInvitationResponse(int invitationId, NetworkBool accept, RpcInfo info = default)
    {
        if (!CanMutate) { SendInvitationResult(info.Source, TownPartyInvitationResult.Unavailable); return default; }
        if (TryResolveSenderWithStatus(info.Source, out ProfileId recipient, out TownSenderResolutionStatus status))
        {
            ExecuteInvitationResponse(info.Source, recipient, invitationId, accept);
            return default;
        }

        if (status == TownSenderResolutionStatus.PendingVerification)
        {
            PlayerRef source = info.Source;
            bool accepted = accept;
            if (!_pendingRpcs.Enqueue(
                    source,
                    Time.unscaledTime,
                    verified => ExecuteInvitationResponse(source, verified, invitationId, accepted),
                    () => SendInvitationResult(source, TownPartyInvitationResult.Unavailable)))
            {
                SendInvitationResult(source, TownPartyInvitationResult.Unavailable);
            }
            return default;
        }

        SendInvitationResult(info.Source, TownPartyInvitationResult.Unavailable);
        return default;
    }

    private void ExecuteInvitationResponse(PlayerRef source, ProfileId recipient, int invitationId, bool accept)
    {
        if (!CanMutate ||
            !TryFindInvitation(invitationId, out int invitationIndex, out TownPartyInvitationNetworkEntry invitation) ||
            invitation.RecipientProfileId.ToString() != recipient.Value)
        { SendInvitationResult(source, TownPartyInvitationResult.Unavailable); return; }
        if (invitation.ExpiresAt.Expired(Runner)) { ResolveInvitation(invitationIndex, invitation, TownPartyInvitationResult.Expired); return; }
        if (!accept) { ResolveInvitation(invitationIndex, invitation, TownPartyInvitationResult.Rejected); return; }

        if (!TryGetPartyEntry(new ProfileId(invitation.InviterProfileId.ToString()), out int inviterIndex, out TownPartyNetworkEntry inviterParty) ||
            !TryGetPartyEntry(recipient, out int recipientIndex, out TownPartyNetworkEntry recipientParty) ||
            inviterParty.PartyId != invitation.InviterPartyId || inviterParty.Revision != invitation.InviterPartyRevision ||
            recipientParty.PartyId != invitation.RecipientPartyId || recipientParty.Revision != invitation.RecipientPartyRevision ||
            !TownPartyRules.CanMerge(ToSnapshot(inviterParty), ToSnapshot(recipientParty)))
        { ResolveInvitation(invitationIndex, invitation, TownPartyInvitationResult.CancelledByPartyChange); return; }

        inviterParty.SecondMemberProfileId = recipient.Value;
        inviterParty.MemberCount = 2;
        inviterParty.Revision++;
        Parties.Set(inviterIndex, inviterParty);
        Parties.Set(recipientIndex, default);
        CancelOtherInvitations(new ProfileId(invitation.InviterProfileId.ToString()), recipient, invitationId);
        ResolveInvitation(invitationIndex, invitation, TownPartyInvitationResult.Accepted);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private RpcInvokeInfo RPC_RequestLeaveParty(RpcInfo info = default)
    {
        if (!CanMutate) { SendLeaveResult(info.Source, TownPartyLeaveResult.Unavailable); return default; }
        if (TryResolveSenderWithStatus(info.Source, out ProfileId leaver, out TownSenderResolutionStatus status))
        {
            ExecuteLeaveParty(info.Source, leaver);
            return default;
        }

        if (status == TownSenderResolutionStatus.PendingVerification)
        {
            PlayerRef source = info.Source;
            if (!_pendingRpcs.Enqueue(
                    source,
                    Time.unscaledTime,
                    verified => ExecuteLeaveParty(source, verified),
                    () => SendLeaveResult(source, TownPartyLeaveResult.Unavailable)))
            {
                SendLeaveResult(source, TownPartyLeaveResult.Unavailable);
            }
            return default;
        }

        SendLeaveResult(info.Source, TownPartyLeaveResult.Unavailable);
        return default;
    }

    private void ExecuteLeaveParty(PlayerRef source, ProfileId leaver)
    {
        if (!CanMutate) { SendLeaveResult(source, TownPartyLeaveResult.Unavailable); return; }
        if (!TryGetPartyEntry(leaver, out int partyIndex, out TownPartyNetworkEntry party) || party.MemberCount != 2)
        { SendLeaveResult(source, TownPartyLeaveResult.NotInDuo); return; }
        if (!TryFindFreePartySlot(out int freeIndex))
        { SendLeaveResult(source, TownPartyLeaveResult.Unavailable); return; }

        ProfileId remaining = new(party.FirstMemberProfileId.ToString() == leaver.Value
            ? party.SecondMemberProfileId.ToString()
            : party.FirstMemberProfileId.ToString());
        party.HostProfileId = remaining.Value;
        party.FirstMemberProfileId = remaining.Value;
        party.SecondMemberProfileId = default;
        party.MemberCount = 1;
        party.Revision++;
        Parties.Set(partyIndex, party);
        Parties.Set(freeIndex, CreateEntry(NextPartyIdentifier(), leaver, leaver));
        CancelOtherInvitations(leaver, remaining, 0);
        SendLeaveResult(source, TownPartyLeaveResult.Left);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private RpcInvokeInfo RPC_RequestRestoreContinuation(
        NetworkString<_32> hostValue, int memberCount, NetworkString<_32> firstValue, NetworkString<_32> secondValue,
        RpcInfo info = default)
    {
        if (!CanMutate || !TryDecodeContext(hostValue, memberCount, firstValue, secondValue, out TownPartyContinuationContext context)) return default;
        if (TryResolveSenderWithStatus(info.Source, out ProfileId claimant, out TownSenderResolutionStatus status))
        {
            ExecuteRestoreContinuation(info.Source, claimant, hostValue, memberCount, firstValue, secondValue, context);
            return default;
        }

        if (status == TownSenderResolutionStatus.PendingVerification)
        {
            PlayerRef source = info.Source;
            _pendingRpcs.Enqueue(
                source,
                Time.unscaledTime,
                verified => ExecuteRestoreContinuation(source, verified, hostValue, memberCount, firstValue, secondValue, context),
                () => SendIdentityError(source));
            return default;
        }

        SendIdentityError(info.Source);
        return default;
    }

    private void ExecuteRestoreContinuation(
        PlayerRef source,
        ProfileId claimant,
        NetworkString<_32> hostValue,
        int memberCount,
        NetworkString<_32> firstValue,
        NetworkString<_32> secondValue,
        TownPartyContinuationContext context)
    {
        if (!CanMutate) return;

        if (TryGetParty(claimant, out TownPartySnapshot existing))
        {
            if (context.Matches(existing)) RPC_AcknowledgeContinuation(source, hostValue, memberCount, firstValue, secondValue);
            else RPC_InvalidateContinuation(source, hostValue, memberCount, firstValue, secondValue);
            return;
        }

        if (TownPartyRules.IsContinuationSuperseded(context, claimant, member => TryGetParty(member, out _)))
        {
            _continuationClaims.Withdraw(claimant, context);
            RPC_InvalidateContinuation(source, hostValue, memberCount, firstValue, secondValue);
            return;
        }

        TownPartyContinuationClaimResult result = _continuationClaims.Submit(claimant, context);
        if (result == TownPartyContinuationClaimResult.Rejected)
        { RPC_InvalidateContinuation(source, hostValue, memberCount, firstValue, secondValue); return; }
        if (result == TownPartyContinuationClaimResult.ReadyToRestore && TryRestoreParty(context)) _continuationClaims.MarkRestored(context);
        RPC_AcknowledgeContinuation(source, hostValue, memberCount, firstValue, secondValue);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_AcknowledgeContinuation([RpcTarget] PlayerRef target, NetworkString<_32> host, int count, NetworkString<_32> first, NetworkString<_32> second)
    {
        SessionConnectionCoordinator coordinator = SessionConnectionCoordinator.Instance;
        if (coordinator != null && coordinator.TryGetPendingPartyContinuation(out TownPartyContinuationContext pending) &&
            TryDecodeContext(host, count, first, second, out TownPartyContinuationContext received) && pending.Equals(received) &&
            TryGetParty(LocalProfileProvider.GetOrCreateLocalProfile(), out TownPartySnapshot party) && pending.Matches(party))
        {
            coordinator.ConfirmPartyContinuationRestored(party);
            _continuationRetryPolicy.Reset();
            _localRequestPending = false;
            return;
        }

        // FIX-07: Do NOT set _localRequestPending = true on a still-pending ACK; enforce backoff so
        // the returning player sends at most one restore request per configured backoff interval.
        _continuationRetryPolicy.OnAcknowledgedPending(Time.unscaledTime);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_InvalidateContinuation([RpcTarget] PlayerRef target, NetworkString<_32> host, int count, NetworkString<_32> first, NetworkString<_32> second)
    {
        SessionConnectionCoordinator coordinator = SessionConnectionCoordinator.Instance;
        if (coordinator != null && coordinator.TryGetPendingPartyContinuation(out TownPartyContinuationContext pending) &&
            TryDecodeContext(host, count, first, second, out TownPartyContinuationContext received) && pending.Equals(received))
            coordinator.AbandonPartyContinuation(pending);
        _continuationRetryPolicy.Reset();
        _localRequestPending = true;
        _nextLocalEnsureRetryTime = 0f;
    }

    private void ObserveLocalParty()
    {
        if (Runner == null || Runner.GetPlayerObject(Runner.LocalPlayer) == null) return;
        ProfileId local = LocalProfileProvider.GetOrCreateLocalProfile();
        if (TryGetParty(local, out _))
        {
            _localRequestPending = false;
            _localEnsureStoppedByTerminalError = false;
            _localMissingTicketReported = false;
        }

        // FIX-59: Without a valid TownSessionTicket, surface one consolidated error and do not
        // spam RPC_RequestEnsureParty every 0.5 s. If a new ticket arrives later, resume automatically.
        ApplicationAuthContext auth = ApplicationAuthContext.Instance;
        string activeRawTicket = auth != null && auth.ActiveTownTicket.IsValid ? auth.ActiveTownTicket.RawTicket : null;
        if (!string.Equals(_observedLocalTicket, activeRawTicket, StringComparison.Ordinal))
        {
            _observedLocalTicket = activeRawTicket;
            if (!string.IsNullOrEmpty(activeRawTicket))
            {
                _localMissingTicketReported = false;
                _localEnsureStoppedByTerminalError = false;
                _localRequestPending = true;
                _nextLocalEnsureRetryTime = 0f;
            }
        }

        if (string.IsNullOrEmpty(activeRawTicket))
        {
            if (!_localMissingTicketReported)
            {
                _localMissingTicketReported = true;
                Debug.LogWarning($"[{nameof(TownPartyDirectory)}] Local player has no valid TownSessionTicket; skipping party ensure until authenticated.");
                _identityErrors.Enqueue(MissingTownTicketMessage);
            }
            return;
        }

        if (_localEnsureStoppedByTerminalError) return;

        SessionConnectionCoordinator coordinator = SessionConnectionCoordinator.Instance;
        if (coordinator != null && coordinator.TryGetPendingPartyContinuation(out TownPartyContinuationContext context))
        {
            if (TryGetParty(local, out TownPartySnapshot existing))
            {
                if (context.Matches(existing)) coordinator.ConfirmPartyContinuationRestored(existing);
                else coordinator.AbandonPartyContinuation(context);
                _continuationRetryPolicy.Reset();
                _localRequestPending = true;
                return;
            }

            TownPartyContinuationRetryPolicy.PollAction action = _continuationRetryPolicy.Evaluate(context, Time.unscaledTime);
            if (action == TownPartyContinuationRetryPolicy.PollAction.Wait)
            {
                return;
            }

            if (action == TownPartyContinuationRetryPolicy.PollAction.Expired)
            {
                coordinator.ExpirePartyContinuation(context);
                _localRequestPending = true;
                _nextLocalEnsureRetryTime = 0f;
            }
            else
            {
                RPC_RequestRestoreContinuation(context.HostProfileId.Value, context.Members.Count, context.Members[0].Value,
                    context.Members.Count > 1 ? context.Members[1].Value : string.Empty);
                return;
            }
        }
        else
        {
            _continuationRetryPolicy.Reset();
        }
        if (TryGetParty(local, out _)) { _localRequestPending = false; return; }
        if (!_localRequestPending && Time.unscaledTime < _nextLocalEnsureRetryTime) return;
        _localRequestPending = false;
        _nextLocalEnsureRetryTime = Time.unscaledTime + LocalEnsurePartyRetryIntervalSeconds;
        RPC_RequestEnsureParty();
    }

    private bool TryRestoreParty(TownPartyContinuationContext context)
    {
        for (int index = 0; index < context.Members.Count; index++)
            if (!TryResolvePlayer(context.Members[index], out _) || TryGetParty(context.Members[index], out _)) return false;
        if (!TryFindFreePartySlot(out int slot)) return false;
        TownPartyNetworkEntry entry = CreateEntry(NextPartyIdentifier(), context.HostProfileId, context.Members[0]);
        if (context.Members.Count == 2) { entry.SecondMemberProfileId = context.Members[1].Value; entry.MemberCount = 2; }
        Parties.Set(slot, entry);
        return true;
    }

    private bool TryCreateSoloParty(ProfileId profile)
    {
        if (!TryFindFreePartySlot(out int slot)) return false;
        Parties.Set(slot, CreateEntry(NextPartyIdentifier(), profile, profile));
        return true;
    }

    private static TownPartyNetworkEntry CreateEntry(int partyId, ProfileId host, ProfileId first) => new()
    { PartyId = partyId, HostProfileId = host.Value, FirstMemberProfileId = first.Value, MemberCount = 1, Revision = 1 };

    private int NextPartyIdentifier() { NextPartyId = NextPartyId == int.MaxValue ? 1 : NextPartyId + 1; return NextPartyId; }
    private void AdvanceEpoch() { StateEpoch = StateEpoch == int.MaxValue ? 1 : StateEpoch + 1; }

    private bool TryGetPartyEntry(ProfileId profile, out int entryIndex, out TownPartyNetworkEntry entry)
    {
        for (int index = 0; index < RaidSessionRules.MaxParticipants; index++)
        { TownPartyNetworkEntry candidate = Parties[index]; if (candidate.IsOccupied && EntryContains(candidate, profile)) { entryIndex = index; entry = candidate; return true; } }
        entryIndex = -1; entry = default; return false;
    }

    private bool TryFindFreePartySlot(out int slot)
    { for (int index = 0; index < RaidSessionRules.MaxParticipants; index++) if (!Parties[index].IsOccupied) { slot = index; return true; } slot = -1; return false; }

    private static bool EntryContains(in TownPartyNetworkEntry entry, ProfileId profile) =>
        entry.FirstMemberProfileId.ToString() == profile.Value || (entry.MemberCount == 2 && entry.SecondMemberProfileId.ToString() == profile.Value);

    private static TownPartySnapshot ToSnapshot(in TownPartyNetworkEntry entry)
    {
        var members = new ProfileId[entry.MemberCount];
        if (entry.MemberCount > 0) members[0] = new ProfileId(entry.FirstMemberProfileId.ToString());
        if (entry.MemberCount > 1) members[1] = new ProfileId(entry.SecondMemberProfileId.ToString());
        return new TownPartySnapshot(entry.PartyId, new ProfileId(entry.HostProfileId.ToString()), members, entry.Revision);
    }

    public ITownSessionTicketVerifier TicketVerifier { get; set; }

    private void ObserveActivePlayers()
    {
        if (Runner == null) return;
        _disconnectResolver.ObserveActiveDeclaredProfiles(Runner.ActivePlayers, TryReadDeclaredProfileOrDefault);
        foreach (PlayerRef active in Runner.ActivePlayers)
        {
            if (!_disconnectResolver.TryGetVerifiedProfile(active, out _) && TryResolveSender(active, out ProfileId verified))
            {
                _disconnectResolver.RecordVerified(active, verified);
                _profileByPlayer[active] = verified;
            }

            NetworkObject playerObject = Runner.GetPlayerObject(active);
            if (playerObject != null && playerObject.TryGetBehaviour(out SocialPlayerIdentity identity))
            {
                string profStr = identity.ProfileId.ToString();
                string nameStr = identity.DisplayName.ToString();
                if (!string.IsNullOrWhiteSpace(profStr) && !string.IsNullOrWhiteSpace(nameStr))
                {
                    _knownDisplayNames[new ProfileId(profStr)] = nameStr;
                }
            }
        }
    }

    private ProfileId TryResolveVerifiedProfileOrDefault(PlayerRef player) =>
        TryResolveSender(player, out ProfileId profile) ? profile : default;

    private ProfileId TryReadDeclaredProfileOrDefault(PlayerRef player) =>
        TownSenderIdentityValidator.TryResolveDeclaredProfile(Runner, player, out ProfileId profile) ? profile : default;

    private bool TryResolveSender(PlayerRef player, out ProfileId profile)
    {
        return TryResolveSenderWithStatus(player, out profile, out _);
    }

    private bool TryResolveSenderWithStatus(PlayerRef player, out ProfileId profile, out TownSenderResolutionStatus status)
    {
        ITownSessionTicketVerifier verifier = TicketVerifier ?? ApplicationAuthContext.Instance?.TownTicketVerifier;
        return TownSenderIdentityValidator.TryResolveVerifiedSender(Runner, player, verifier, out profile, out status);
    }

    private bool TryFindInvitation(int id, out int invitationIndex, out TownPartyInvitationNetworkEntry invitation)
    { for (int index = 0; index < RaidSessionRules.MaxParticipants; index++) { TownPartyInvitationNetworkEntry candidate = Invitations[index]; if (candidate.IsPending && candidate.InvitationId == id) { invitationIndex = index; invitation = candidate; return true; } } invitationIndex = -1; invitation = default; return false; }
    private bool HasPendingInvitation(ProfileId profile)
    { for (int index = 0; index < RaidSessionRules.MaxParticipants; index++) { TownPartyInvitationNetworkEntry entry = Invitations[index]; if (entry.IsPending && (entry.InviterProfileId.ToString() == profile.Value || entry.RecipientProfileId.ToString() == profile.Value)) return true; } return false; }

    private void CancelOtherInvitations(ProfileId first, ProfileId second, int except)
    {
        for (int index = 0; index < RaidSessionRules.MaxParticipants; index++)
        { TownPartyInvitationNetworkEntry entry = Invitations[index]; if (entry.IsPending && entry.InvitationId != except &&
            (entry.InviterProfileId.ToString() == first.Value || entry.RecipientProfileId.ToString() == first.Value || entry.InviterProfileId.ToString() == second.Value || entry.RecipientProfileId.ToString() == second.Value))
            ResolveInvitation(index, entry, TownPartyInvitationResult.CancelledByPartyChange); }
    }

    private void ResolveInvitation(int index, in TownPartyInvitationNetworkEntry invitation, TownPartyInvitationResult result)
    {
        Invitations.Set(index, default);
        ProfileId inviter = new(invitation.InviterProfileId.ToString());
        ProfileId recipient = new(invitation.RecipientProfileId.ToString());
        AddCooldown(inviter, recipient);
        SendInvitationResult(inviter, result);
        SendInvitationResult(recipient, result);
    }

    private void AddCooldown(ProfileId first, ProfileId second)
    {
        NormalizePair(first, second, out first, out second);
        for (int index = 0; index < RaidSessionRules.MaxParticipants; index++)
        {
            TownPartyInvitationCooldownEntry entry = InvitationCooldowns[index];
            if (entry.IsActive && !entry.ExpiresAt.Expired(Runner)) continue;
            InvitationCooldowns.Set(index, new TownPartyInvitationCooldownEntry { FirstProfileId = first.Value, SecondProfileId = second.Value, ExpiresAt = TickTimer.CreateFromSeconds(Runner, InvitationCooldownSeconds) });
            return;
        }
    }

    private bool HasActiveCooldown(ProfileId first, ProfileId second)
    {
        NormalizePair(first, second, out first, out second);
        for (int index = 0; index < RaidSessionRules.MaxParticipants; index++)
        { TownPartyInvitationCooldownEntry entry = InvitationCooldowns[index]; if (entry.IsActive && !entry.ExpiresAt.Expired(Runner) && entry.FirstProfileId.ToString() == first.Value && entry.SecondProfileId.ToString() == second.Value) return true; }
        return false;
    }

    private bool HasReservedCooldownCapacity()
    { for (int index = 0; index < RaidSessionRules.MaxParticipants; index++) { TownPartyInvitationCooldownEntry entry = InvitationCooldowns[index]; if (!entry.IsActive || entry.ExpiresAt.Expired(Runner)) return true; } return false; }

    private static void NormalizePair(ProfileId first, ProfileId second, out ProfileId normalizedFirst, out ProfileId normalizedSecond)
    { if (string.CompareOrdinal(first.Value, second.Value) <= 0) { normalizedFirst = first; normalizedSecond = second; } else { normalizedFirst = second; normalizedSecond = first; } }

    private void SendInvitationResult(ProfileId profile, TownPartyInvitationResult result) { if (TryResolvePlayer(profile, out PlayerRef player)) SendInvitationResult(player, result); }
    private void SendInvitationResult(PlayerRef player, TownPartyInvitationResult result) { if (!player.IsNone) RPC_ReceiveInvitationResult(player, result); }
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)] private void RPC_ReceiveInvitationResult([RpcTarget] PlayerRef target, TownPartyInvitationResult result) => _resultEvents.Enqueue(new TownPartyInvitationResultEvent(result));
    private void SendLeaveResult(PlayerRef player, TownPartyLeaveResult result) { if (!player.IsNone) RPC_ReceiveLeaveResult(player, result); }
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)] private void RPC_ReceiveLeaveResult([RpcTarget] PlayerRef target, TownPartyLeaveResult result) => _leaveResults.Enqueue(result);

    private void SendIdentityError(PlayerRef player)
    {
        if (!player.IsNone) RPC_ReceiveIdentityError(player);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ReceiveIdentityError([RpcTarget] PlayerRef target)
    {
        if (_localEnsureStoppedByTerminalError) return;
        _localEnsureStoppedByTerminalError = true;
        _identityErrors.Enqueue(MissingTownTicketMessage);
    }

    private static bool TryDecodeContext(NetworkString<_32> host, int count, NetworkString<_32> first, NetworkString<_32> second, out TownPartyContinuationContext context)
    {
        if (count < 1 || count > TownPartyRules.MaxMembers) { context = null; return false; }
        var members = new ProfileId[count]; members[0] = new ProfileId(first.ToString()); if (count == 2) members[1] = new ProfileId(second.ToString());
        return TownPartyContinuationContext.TryCreate(new ProfileId(host.ToString()), members, out context);
    }

    private bool CanSend => Runner != null && Object != null && Object.IsValid && SessionConnectionCoordinator.Instance?.State == SessionConnectionState.Town;
    private bool CanMutate => HasStateAuthority && Runner != null && Object != null && Object.IsValid;
}

using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>Authoritative Town boundary for concrete Raid preparations only.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class TownRaidPreparationDirectory : NetworkBehaviour, IPlayerLeft, IStateAuthorityChanged
{
    private const int AuthorityRebuildDelayTicks = 2;
    private const int RandomCodeAttempts = 128;

    [SerializeField] private NetworkPrefabRef _preparationPrefab;

    private readonly List<TownRaidPreparationNetworkController> _preparations = new();
    private readonly TownRaidPreparationDirectoryCache<TownRaidPreparationNetworkController> _cache = new();
    private readonly TownPreparationDisconnectResolver _disconnectResolver = new();
    private readonly TownPendingRpcQueue _pendingRpcs = new();
    private readonly Queue<TownRaidStartResult> _startResults = new();
    private readonly Queue<TownRaidPreparationActionResult> _actionResults = new();
    private readonly Queue<TownRaidPreparationCancelReason> _cancelReasons = new();
    private bool _interactionRequested;
    private bool _indexReady;
    private bool _conflictLogged;
    private int _rebuildTicksRemaining;

    public event Action PreparationInteractionRequested;
    /// <summary>Raised on the requesting Host when the State Authority refuses its Start request.</summary>
    public event Action<TownRaidStartResult> StartResultReceived;
    /// <summary>Raised on the requesting client when a Create, Join, Ready or Start action is rejected.</summary>
    public event Action<TownRaidPreparationActionResult> ActionResultReceived;
    /// <summary>Raised on affected members when their preparation dissolves or a launch is cancelled.</summary>
    public event Action<TownRaidPreparationCancelReason> CancellationReceived;
    public bool IsIndexReady => _indexReady;
    public int PreparationCount => _preparations.Count;

    public bool RequestCreate() { if (!CanSendRequest) return false; RPC_RequestCreate(); return true; }
    public bool RequestJoin(string code) { if (!CanSendRequest || !RaidCode.TryParse(code, out _)) return false; RPC_RequestJoin(code); return true; }
    public bool RequestLeave() { if (!CanSendRequest) return false; RPC_RequestLeave(); return true; }
    public bool RequestSetReady(bool ready) { if (!CanSendRequest) return false; RPC_RequestSetReady(ready); return true; }
    public bool RequestSetFriendlyFire(bool enabled) { if (!CanSendRequest) return false; RPC_RequestSetFriendlyFire(enabled); return true; }
    public bool RequestStart() { if (!CanSendRequest) return false; RPC_RequestStart(); return true; }

    public override void Spawned()
    {
        Runner.GetComponent<TownRaidPreparationDirectoryContext>()?.Register(this);
        Runner.GetComponent<TownSenderIdentityContext>()?.RegisterAuthorityObject(Object);
        _indexReady = !HasStateAuthority;
        if (HasStateAuthority) BeginAuthorityRebuild();
    }

    public override void Render()
    {
        // Dequeue first: `?.` skips argument evaluation when nothing is subscribed.
        while (_startResults.Count > 0)
        {
            TownRaidStartResult result = _startResults.Dequeue();
            StartResultReceived?.Invoke(result);
        }

        while (_actionResults.Count > 0)
        {
            TownRaidPreparationActionResult actionResult = _actionResults.Dequeue();
            ActionResultReceived?.Invoke(actionResult);
        }

        while (_cancelReasons.Count > 0)
        {
            TownRaidPreparationCancelReason cancelReason = _cancelReasons.Dequeue();
            CancellationReceived?.Invoke(cancelReason);
        }

        if (!_interactionRequested) return;
        _interactionRequested = false;
        PreparationInteractionRequested?.Invoke();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        ObserveActivePlayers();
        _pendingRpcs.Drain(Time.unscaledTime, TryResolveSenderWithStatus);
        if (_rebuildTicksRemaining <= 0) return;
        _rebuildTicksRemaining--;
        if (_rebuildTicksRemaining == 0) RebuildAuthorityIndices();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        runner.GetComponent<TownRaidPreparationDirectoryContext>()?.Unregister(this);
        runner.GetComponent<TownSenderIdentityContext>()?.UnregisterAuthorityObject(Object);
        _interactionRequested = false;
        _startResults.Clear();
        _actionResults.Clear();
        _cancelReasons.Clear();
        _pendingRpcs.Clear();
        _indexReady = false;
        _preparations.Clear();
        _disconnectResolver.ClearAll();
    }

    public void StateAuthorityChanged()
    {
        Runner?.GetComponent<TownSenderIdentityContext>()?.NotifyAuthorityChanged();
        _disconnectResolver.ClearVerified();
        _pendingRpcs.Clear();
        if (HasStateAuthority)
        {
            ObserveActivePlayers();
            BeginAuthorityRebuild();
        }
        else { _indexReady = true; _rebuildTicksRemaining = 0; }
    }

    public void PlayerLeft(PlayerRef player)
    {
        _pendingRpcs.RemoveSender(player);
        if (!HasStateAuthority) return;
        bool resolved = _disconnectResolver.TryResolveDisconnectedProfile(
            player,
            Runner != null ? Runner.ActivePlayers : null,
            TryResolveVerifiedProfileOrDefault,
            TryReadDeclaredProfileOrDefault,
            out ProfileId profile);
        if (!resolved || !_cache.TryResolve(profile, out TownRaidPreparationNetworkController preparation)) return;
        if (preparation.State == TownRaidPreparationState.Launching) { preparation.AuthorityHandlePlayerLeft(profile); return; }
        if (profile == preparation.HostProfileId)
        {
            NotifyMembersCancelled(preparation, TownRaidPreparationCancelReason.HostDisconnected, profile);
            AuthorityDissolvePreparation(preparation);
        }
        else preparation.AuthorityTryRemoveMember(profile);
    }

    public void NotifyLocalInteractionRequested() => _interactionRequested = true;
    public bool TryGetPreparation(ProfileId profile, out TownRaidPreparationNetworkController preparation)
    { preparation = null; return profile.IsValid && _cache.TryResolve(profile, out preparation); }
    public bool TryGetPreparation(RaidCode code, out TownRaidPreparationNetworkController preparation)
    { preparation = null; return code.IsValid && _cache.TryResolve(code, out preparation); }

    /// <summary>
    /// Resolves a player's <see cref="PlayerRef"/> strictly from identities verified by THIS authority peer
    /// (FIX-93). Never consults <see cref="TownPartyDirectory"/> (which may have a different State Authority
    /// during migration) and never falls back to unverified replicated ProfileId claims.
    /// </summary>
    public bool TryResolveVerifiedPlayer(ProfileId profile, out PlayerRef player)
    {
        player = PlayerRef.None;
        if (!HasStateAuthority || Runner == null || !profile.IsValid)
        {
            return false;
        }

        if (_disconnectResolver.TryFindVerifiedPlayer(profile, out PlayerRef cached))
        {
            if (TownSenderIdentityContext.IsActive(Runner, cached) &&
                TryResolveSender(cached, out ProfileId confirmed) && confirmed == profile)
            {
                player = cached;
                return true;
            }

            _disconnectResolver.RemoveVerified(cached);
        }

        foreach (PlayerRef candidate in Runner.ActivePlayers)
        {
            if (!TryResolveSender(candidate, out ProfileId resolved)) continue;
            _disconnectResolver.RecordVerified(candidate, resolved);
            if (resolved == profile) { player = candidate; return true; }
        }

        return false;
    }

    /// <summary>
    /// Resolves the recipient of a launch release notice strictly from verified identities, distinguishing
    /// a member still awaiting post-migration verification from a genuinely disconnected member (FIX-93).
    /// </summary>
    public TownLaunchRecipientStatus ResolveLaunchRecipient(ProfileId profile, out PlayerRef verifiedPlayer)
    {
        verifiedPlayer = PlayerRef.None;
        if (!HasStateAuthority || Runner == null || !profile.IsValid)
        {
            return TownLaunchRecipientStatus.Disconnected;
        }

        return TownLaunchRecipientResolver.Resolve(
            profile,
            Runner.ActivePlayers,
            TryResolveVerifiedPlayer,
            TryResolveDeclaredProfileForActivePlayer,
            TryResolveSender,
            out verifiedPlayer);
    }

    public bool TryResolvePlayer(ProfileId profile, out PlayerRef player)
    {
        TownPartyDirectory parties = Runner?.GetComponent<TownPartyDirectoryContext>()?.Directory;
        // FIX-93: When this directory is State Authority, only consult TownPartyDirectory if it is
        // ALSO State Authority on this peer; otherwise TownPartyDirectory would fall back to unverified
        // replicated ProfileId claims.
        if (parties != null && (!HasStateAuthority || parties.HasStateAuthority) && parties.TryResolvePlayer(profile, out player))
        {
            return true;
        }

        player = PlayerRef.None;
        if (Runner == null) return false;
        if (!HasStateAuthority)
        {
            // Presentation only: the replicated claim is not authorization.
            foreach (PlayerRef active in Runner.ActivePlayers)
            {
                if (TownSenderIdentityValidator.TryResolveDeclaredProfile(Runner, active, out ProfileId declared) && declared == profile)
                { player = active; return true; }
            }
            return false;
        }

        return TryResolveVerifiedPlayer(profile, out player);
    }

    public bool TryGetLocalPartyContinuation(ProfileId localProfile, out TownPartyContinuationContext context)
    {
        context = null;
        TownPartyDirectory parties = Runner?.GetComponent<TownPartyDirectoryContext>()?.Directory;
        return parties != null && parties.TryGetParty(localProfile, out TownPartySnapshot party) &&
            TownPartyContinuationContext.TryCreate(party, out context);
    }

    public void RegisterPreparation(TownRaidPreparationNetworkController preparation)
    {
        if (preparation == null) return;
        if (!_preparations.Contains(preparation)) _preparations.Add(preparation);
        UpdateCache(preparation);
    }

    public void NotifyPreparationChanged(TownRaidPreparationNetworkController preparation)
    { if (preparation != null && _preparations.Contains(preparation)) UpdateCache(preparation); }

    public void UnregisterPreparation(TownRaidPreparationNetworkController preparation)
    {
        if (preparation == null) return;
        _preparations.Remove(preparation);
        _cache.Unregister(preparation);
        RefreshConflictState();
    }

    public bool AuthorityDissolvePreparation(TownRaidPreparationNetworkController preparation)
    {
        if (!CanMutate || preparation == null || preparation.Object == null || !preparation.Object.IsValid || !preparation.HasStateAuthority) return false;
        UnregisterPreparation(preparation);
        Runner.Despawn(preparation.Object);
        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private RpcInvokeInfo RPC_RequestCreate(RpcInfo info = default)
    {
        if (!CanMutate || !_preparationPrefab.IsValid)
        {
            SendActionResult(info.Source, TownRaidPreparationActionResult.DirectoryUnavailable);
            return default;
        }
        if (TryResolveSenderWithStatus(info.Source, out ProfileId creator, out TownSenderResolutionStatus status))
        {
            ExecuteCreate(info.Source, creator);
            return default;
        }
        if (status == TownSenderResolutionStatus.PendingVerification)
        {
            PlayerRef source = info.Source;
            if (!_pendingRpcs.Enqueue(source, Time.unscaledTime,
                    verified => ExecuteCreate(source, verified),
                    () => SendActionResult(source, TownRaidPreparationActionResult.Failed)))
            {
                SendActionResult(source, TownRaidPreparationActionResult.Failed);
            }
            return default;
        }
        SendActionResult(info.Source, TownRaidPreparationActionResult.Failed);
        return default;
    }

    private void ExecuteCreate(PlayerRef source, ProfileId creator)
    {
        if (!CanMutate || !_preparationPrefab.IsValid)
        {
            SendActionResult(source, TownRaidPreparationActionResult.DirectoryUnavailable);
            return;
        }
        if (_cache.TryResolve(creator, out _))
        {
            SendActionResult(source, TownRaidPreparationActionResult.AlreadyInPreparation);
            return;
        }
        if (IsAwaitingPartyCompletion(source))
        {
            SendActionResult(source, TownRaidPreparationActionResult.AwaitingPartyCompletion);
            return;
        }

        TownPartyDirectory partyDirectory = Runner.GetComponent<TownPartyDirectoryContext>()?.Directory;
        if (partyDirectory == null || !partyDirectory.TryGetParty(creator, out TownPartySnapshot party) ||
            !TownPartyRules.TryCreateRaidCreatorRoster(party, creator, out IReadOnlyList<ProfileId> roster))
        {
            SendActionResult(source, TownRaidPreparationActionResult.PartyUnavailable);
            return;
        }

        if (!TryGenerateUniqueRaidCode(out RaidCode code))
        {
            SendActionResult(source, TownRaidPreparationActionResult.Failed);
            return;
        }

        _disconnectResolver.RecordVerified(source, creator);
        if (!TrySpawnPreparation(code, creator, roster))
        {
            SendActionResult(source, TownRaidPreparationActionResult.Failed);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private RpcInvokeInfo RPC_RequestJoin(NetworkString<_8> requestedCode, RpcInfo info = default)
    {
        if (!CanMutate)
        {
            SendActionResult(info.Source, TownRaidPreparationActionResult.DirectoryUnavailable);
            return default;
        }
        if (!RaidCode.TryParse(requestedCode.ToString(), out RaidCode code))
        {
            SendActionResult(info.Source, TownRaidPreparationActionResult.InvalidCode);
            return default;
        }
        if (TryResolveSenderWithStatus(info.Source, out ProfileId profile, out TownSenderResolutionStatus status))
        {
            ExecuteJoin(info.Source, profile, code);
            return default;
        }
        if (status == TownSenderResolutionStatus.PendingVerification)
        {
            PlayerRef source = info.Source;
            if (!_pendingRpcs.Enqueue(source, Time.unscaledTime,
                    verified => ExecuteJoin(source, verified, code),
                    () => SendActionResult(source, TownRaidPreparationActionResult.Failed)))
            {
                SendActionResult(source, TownRaidPreparationActionResult.Failed);
            }
            return default;
        }
        SendActionResult(info.Source, TownRaidPreparationActionResult.Failed);
        return default;
    }

    private void ExecuteJoin(PlayerRef source, ProfileId profile, RaidCode code)
    {
        if (!CanMutate)
        {
            SendActionResult(source, TownRaidPreparationActionResult.DirectoryUnavailable);
            return;
        }
        if (_cache.TryResolve(profile, out _))
        {
            SendActionResult(source, TownRaidPreparationActionResult.AlreadyInPreparation);
            return;
        }
        if (IsAwaitingPartyCompletion(source))
        {
            SendActionResult(source, TownRaidPreparationActionResult.AwaitingPartyCompletion);
            return;
        }
        if (!_cache.TryResolve(code, out TownRaidPreparationNetworkController preparation))
        {
            SendActionResult(source, TownRaidPreparationActionResult.CodeNotFound);
            return;
        }
        if (!preparation.HasStateAuthority)
        {
            SendActionResult(source, TownRaidPreparationActionResult.DirectoryUnavailable);
            return;
        }
        if (preparation.State != TownRaidPreparationState.Waiting)
        {
            SendActionResult(source, TownRaidPreparationActionResult.PreparationLaunching);
            return;
        }
        if (preparation.MemberCount >= TownRaidPreparationRules.MaxMembers)
        {
            SendActionResult(source, TownRaidPreparationActionResult.PreparationFull);
            return;
        }
        if (!preparation.AuthorityTryAddMember(profile))
        {
            SendActionResult(source, TownRaidPreparationActionResult.Failed);
            return;
        }
        _disconnectResolver.RecordVerified(source, profile);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private RpcInvokeInfo RPC_RequestLeave(RpcInfo info = default)
    {
        if (!CanMutate) return default;
        if (TryResolveSenderWithStatus(info.Source, out ProfileId profile, out TownSenderResolutionStatus status))
        {
            ExecuteLeave(info.Source, profile);
            return default;
        }
        if (status == TownSenderResolutionStatus.PendingVerification)
        {
            PlayerRef source = info.Source;
            _pendingRpcs.Enqueue(source, Time.unscaledTime,
                verified => ExecuteLeave(source, verified),
                () => SendActionResult(source, TownRaidPreparationActionResult.Failed));
        }
        return default;
    }

    private void ExecuteLeave(PlayerRef source, ProfileId profile)
    {
        if (!CanMutate || !_cache.TryResolve(profile, out TownRaidPreparationNetworkController preparation)) return;
        if (preparation.State != TownRaidPreparationState.Waiting)
        {
            SendActionResult(source, TownRaidPreparationActionResult.PreparationLaunching);
            return;
        }
        _disconnectResolver.RecordVerified(source, profile);
        if (profile == preparation.HostProfileId)
        {
            NotifyMembersCancelled(preparation, TownRaidPreparationCancelReason.HostLeft, profile);
            AuthorityDissolvePreparation(preparation);
        }
        else preparation.AuthorityTryRemoveMember(profile);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private RpcInvokeInfo RPC_RequestSetReady(NetworkBool ready, RpcInfo info = default)
    {
        if (!CanMutate)
        {
            SendActionResult(info.Source, TownRaidPreparationActionResult.DirectoryUnavailable);
            return default;
        }
        if (TryResolveSenderWithStatus(info.Source, out ProfileId profile, out TownSenderResolutionStatus status))
        {
            ExecuteSetReady(info.Source, profile, ready);
            return default;
        }
        if (status == TownSenderResolutionStatus.PendingVerification)
        {
            PlayerRef source = info.Source;
            bool isReady = ready;
            if (!_pendingRpcs.Enqueue(source, Time.unscaledTime,
                    verified => ExecuteSetReady(source, verified, isReady),
                    () => SendActionResult(source, TownRaidPreparationActionResult.Failed)))
            {
                SendActionResult(source, TownRaidPreparationActionResult.Failed);
            }
            return default;
        }
        SendActionResult(info.Source, TownRaidPreparationActionResult.Failed);
        return default;
    }

    private void ExecuteSetReady(PlayerRef source, ProfileId profile, bool ready)
    {
        if (!CanMutate)
        {
            SendActionResult(source, TownRaidPreparationActionResult.DirectoryUnavailable);
            return;
        }
        if (!_cache.TryResolve(profile, out TownRaidPreparationNetworkController preparation))
        {
            SendActionResult(source, TownRaidPreparationActionResult.NotInPreparation);
            return;
        }
        if (!preparation.HasStateAuthority)
        {
            SendActionResult(source, TownRaidPreparationActionResult.DirectoryUnavailable);
            return;
        }
        _disconnectResolver.RecordVerified(source, profile);
        if (ready && IsAwaitingPartyCompletion(source))
        {
            SendActionResult(source, TownRaidPreparationActionResult.AwaitingPartyCompletion);
            return;
        }
        if (preparation.State != TownRaidPreparationState.Waiting)
        {
            SendActionResult(source, TownRaidPreparationActionResult.PreparationLaunching);
            return;
        }
        if (!preparation.AuthorityTrySetReady(profile, ready))
        {
            SendActionResult(source, TownRaidPreparationActionResult.Failed);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private RpcInvokeInfo RPC_RequestSetFriendlyFire(NetworkBool enabled, RpcInfo info = default)
    {
        if (!CanMutate) return default;
        if (TryResolveSenderWithStatus(info.Source, out ProfileId profile, out TownSenderResolutionStatus status))
        {
            ExecuteSetFriendlyFire(info.Source, profile, enabled);
            return default;
        }
        if (status == TownSenderResolutionStatus.PendingVerification)
        {
            PlayerRef source = info.Source;
            bool isEnabled = enabled;
            _pendingRpcs.Enqueue(source, Time.unscaledTime,
                verified => ExecuteSetFriendlyFire(source, verified, isEnabled),
                null);
        }
        return default;
    }

    private void ExecuteSetFriendlyFire(PlayerRef source, ProfileId profile, bool enabled)
    {
        if (!CanMutate || !_cache.TryResolve(profile, out TownRaidPreparationNetworkController preparation) || !preparation.HasStateAuthority) return;
        _disconnectResolver.RecordVerified(source, profile);
        preparation.AuthorityTrySetFriendlyFire(profile, enabled);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private RpcInvokeInfo RPC_RequestStart(RpcInfo info = default)
    {
        if (!CanMutate)
        {
            SendActionResult(info.Source, TownRaidPreparationActionResult.DirectoryUnavailable);
            return default;
        }
        if (TryResolveSenderWithStatus(info.Source, out ProfileId profile, out TownSenderResolutionStatus status))
        {
            ExecuteStart(info.Source, profile);
            return default;
        }
        if (status == TownSenderResolutionStatus.PendingVerification)
        {
            PlayerRef source = info.Source;
            if (!_pendingRpcs.Enqueue(source, Time.unscaledTime,
                    verified => ExecuteStart(source, verified),
                    () => SendActionResult(source, TownRaidPreparationActionResult.Failed)))
            {
                SendActionResult(source, TownRaidPreparationActionResult.Failed);
            }
            return default;
        }
        SendActionResult(info.Source, TownRaidPreparationActionResult.Failed);
        return default;
    }

    private void ExecuteStart(PlayerRef source, ProfileId profile)
    {
        if (!CanMutate)
        {
            SendActionResult(source, TownRaidPreparationActionResult.DirectoryUnavailable);
            return;
        }
        if (!_cache.TryResolve(profile, out TownRaidPreparationNetworkController preparation))
        {
            SendActionResult(source, TownRaidPreparationActionResult.NotInPreparation);
            return;
        }
        if (!preparation.HasStateAuthority)
        {
            SendActionResult(source, TownRaidPreparationActionResult.DirectoryUnavailable);
            return;
        }
        _disconnectResolver.RecordVerified(source, profile);
        if (IsAwaitingPartyCompletion(source))
        {
            SendActionResult(source, TownRaidPreparationActionResult.AwaitingPartyCompletion);
            return;
        }
        TownRaidStartResult result = preparation.AuthorityTryStart(profile);
        if (result == TownRaidStartResult.PartyUnavailable)
        {
            RPC_ReceiveStartResult(source, result);
        }
        else if (result != TownRaidStartResult.Started)
        {
            TownRaidPreparationSnapshot snapshot = preparation.Snapshot;
            TownRaidPreparationActionResult reason = snapshot.State != TownRaidPreparationState.Waiting
                ? TownRaidPreparationActionResult.PreparationLaunching
                : profile != snapshot.HostProfileId
                    ? TownRaidPreparationActionResult.NotHost
                    : !TownRaidPreparationRules.AreAllMembersReady(snapshot.Members)
                        ? TownRaidPreparationActionResult.NotAllMembersReady
                        : TownRaidPreparationActionResult.Failed;
            SendActionResult(source, reason);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ReceiveStartResult([RpcTarget] PlayerRef target, TownRaidStartResult result) => _startResults.Enqueue(result);

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ReceiveActionResult([RpcTarget] PlayerRef target, TownRaidPreparationActionResult result) => _actionResults.Enqueue(result);

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ReceiveCancellation([RpcTarget] PlayerRef target, TownRaidPreparationCancelReason reason) => _cancelReasons.Enqueue(reason);

    public void NotifyMemberCancelled(PlayerRef target, TownRaidPreparationCancelReason reason)
    {
        if (!HasStateAuthority || target.IsNone || reason == TownRaidPreparationCancelReason.None) return;
        RPC_ReceiveCancellation(target, reason);
    }

    public void NotifyMembersCancelled(
        TownRaidPreparationNetworkController preparation,
        TownRaidPreparationCancelReason reason,
        ProfileId excludeProfile = default)
    {
        if (!HasStateAuthority || preparation == null || reason == TownRaidPreparationCancelReason.None) return;
        TownRaidPreparationSnapshot snapshot = preparation.Snapshot;
        for (int index = 0; index < snapshot.Members.Count; index++)
        {
            ProfileId member = snapshot.Members[index].ProfileId;
            if (excludeProfile.IsValid && member == excludeProfile) continue;
            if (TryResolveVerifiedPlayer(member, out PlayerRef player))
            {
                RPC_ReceiveCancellation(player, reason);
            }
        }
    }

    private void SendActionResult(PlayerRef target, TownRaidPreparationActionResult result)
    {
        if (!target.IsNone && result != TownRaidPreparationActionResult.Succeeded)
        {
            RPC_ReceiveActionResult(target, result);
        }
    }

    private bool IsAwaitingPartyCompletion(PlayerRef player)
    {
        TownPartyDirectory partyDirectory = Runner.GetComponent<TownPartyDirectoryContext>()?.Directory;
        return partyDirectory != null && partyDirectory.IsAwaitingPartyCompletion(player);
    }

    private bool TrySpawnPreparation(RaidCode code, ProfileId host, IReadOnlyList<ProfileId> members)
    {
        bool initialized = false;
        NetworkObject spawned = Runner.Spawn(_preparationPrefab, Vector3.zero, Quaternion.identity, null,
            (callbackRunner, networkObject) =>
            {
                if (networkObject.TryGetBehaviour(out TownRaidPreparationNetworkController controller))
                    initialized = controller.TrySetSpawnInitialization(callbackRunner, networkObject, Object.Id, code, host, members);
            });
        if (spawned != null && initialized) return true;
        if (spawned != null && spawned.IsValid) Runner.Despawn(spawned);
        Debug.LogError($"{nameof(TownRaidPreparationDirectory)} failed to spawn a preparation.", this);
        return false;
    }

    private void BeginAuthorityRebuild() { _indexReady = false; _rebuildTicksRemaining = AuthorityRebuildDelayTicks; }

    private void RebuildAuthorityIndices()
    {
        var entries = new List<KeyValuePair<TownRaidPreparationNetworkController, TownRaidPreparationSnapshot>>(_preparations.Count);
        for (int index = 0; index < _preparations.Count; index++)
        { TownRaidPreparationNetworkController preparation = _preparations[index]; if (preparation != null && preparation.Object != null && preparation.Object.IsValid) entries.Add(new(preparation, preparation.Snapshot)); }
        _disconnectResolver.ClearVerified();
        ObserveActivePlayers();
        _indexReady = _cache.Rebuild(entries);
        RefreshConflictState();
    }

    private void ObserveActivePlayers()
    {
        if (Runner == null) return;
        _disconnectResolver.ObserveActiveDeclaredProfiles(Runner.ActivePlayers, TryReadDeclaredProfileOrDefault);
        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (!_disconnectResolver.TryGetVerifiedProfile(player, out _) && TryResolveSender(player, out ProfileId profile))
            {
                _disconnectResolver.RecordVerified(player, profile);
            }
        }
    }

    private ProfileId TryResolveVerifiedProfileOrDefault(PlayerRef player) =>
        TryResolveSender(player, out ProfileId profile) ? profile : default;

    private ProfileId TryReadDeclaredProfileOrDefault(PlayerRef player) =>
        TownSenderIdentityValidator.TryResolveDeclaredProfile(Runner, player, out ProfileId profile) ? profile : default;

    private bool TryResolveDeclaredProfileForActivePlayer(PlayerRef player, out ProfileId profile) =>
        TownSenderIdentityValidator.TryResolveDeclaredProfile(Runner, player, out profile);

    private void UpdateCache(TownRaidPreparationNetworkController preparation)
    { if (!_cache.RegisterOrUpdate(preparation, preparation.Snapshot)) _indexReady = !HasStateAuthority; RefreshConflictState(); }

    private void RefreshConflictState()
    {
        if (_cache.IsConsistent) { _conflictLogged = false; return; }
        if (_conflictLogged) return;
        _conflictLogged = true;
        Debug.LogError($"{nameof(TownRaidPreparationDirectory)} detected duplicate RaidCode/ProfileId claims.", this);
    }

    private bool TryGenerateUniqueRaidCode(out RaidCode code)
    {
        for (int attempt = 0; attempt < RandomCodeAttempts; attempt++)
        { string value = UnityEngine.Random.Range(0, 1_000_000).ToString("D6"); if (RaidCode.TryParse(value, out code) && !_cache.TryResolve(code, out _)) return true; }
        code = default;
        return false;
    }

    public ITownSessionTicketVerifier TicketVerifier { get; set; }

    private bool TryResolveSender(PlayerRef player, out ProfileId profile)
    {
        return TryResolveSenderWithStatus(player, out profile, out _);
    }

    private bool TryResolveSenderWithStatus(PlayerRef player, out ProfileId profile, out TownSenderResolutionStatus status)
    {
        ITownSessionTicketVerifier verifier = TicketVerifier ?? ApplicationAuthContext.Instance?.TownTicketVerifier;
        return TownSenderIdentityValidator.TryResolveVerifiedSender(Runner, player, verifier, out profile, out status);
    }

    private bool CanSendRequest => Runner != null && Object != null && Object.IsValid && _indexReady && SessionConnectionCoordinator.Instance?.State == SessionConnectionState.Town;
    private bool CanMutate => HasStateAuthority && Runner != null && Object != null && Object.IsValid && _indexReady && _cache.IsConsistent;
}

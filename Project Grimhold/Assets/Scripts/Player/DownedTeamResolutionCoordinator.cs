using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Host-only evaluator of Accelerated Resolution (GD 13 section 9, DownedAndReviveArchitecture
/// section 10). Each simulation tick it rebuilds one <see cref="DownedTeamMemberRouteSnapshot"/> per
/// member of every roster team from authoritative state, asks <see cref="DownedTeamRecoveryRouteRules"/>
/// whether the team still has a recovery route and, when it has none, calls
/// <see cref="PlayerDownedStateNetworkController.RequestAcceleratedResolution"/> on every Downed
/// <c>Raiding</c> member. That call latches by itself, so repeating it each tick is harmless and the
/// acceleration stays until the Downed cycle ends (section 17, Q2).
///
/// It keeps no gameplay state of its own and replicates nothing: after Host Migration it recomputes
/// from the restored avatar and participant state. Lives on <c>NetworkMatchController</c> after the
/// avatar controllers it reads. The tick performs no managed allocations: snapshot buffers are reused
/// and each member's component lookups are cached per avatar.
///
/// <b>Fail-safe evaluation.</b> Acceleration latches, so only known facts may lead to it. A member
/// whose state cannot be read yet (participant not resolvable, or still Raiding without a resolvable
/// avatar) yields a <c>default</c> (unknown) snapshot, which the rule treats as "do not accelerate this
/// tick"; the team is re-evaluated on later ticks. Members known to be gone are classified from
/// authoritative data instead of being skipped: a participant that exists is read from its own replicated
/// state (including retained disconnected ones, which leave the manager's tracked roster on disconnect),
/// and one that was resolved earlier and has since been despawned finished its Raid. Evaluation pauses
/// while Host Migration recovery is in progress, when restored participants are not bound yet.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(116)]
public sealed class DownedTeamResolutionCoordinator : NetworkBehaviour
{
    private const int RosterRetryIntervalTicks = 32;
    private const int ParticipantRetryIntervalTicks = 32;
    private const int MaxMembersPerTeam = 2;

    private struct MemberCache
    {
        /// <summary>The participant object was resolved at least once; if it later stops resolving it was despawned.</summary>
        public bool WasResolved;
        public int NextResolveTick;
        public NetworkRaidParticipant Participant;
        public RaidParticipantConnectivity Connectivity;
        public NetworkId AvatarId;
        public PlayerCharacter Character;
        public PlayerDownedStateNetworkController Downed;
        public PlayerDownedRecoveryNetworkController Recovery;
    }

    private readonly Dictionary<RaidParticipantId, MemberCache> _members = new(RaidSessionRules.MaxParticipants);
    private readonly List<DownedTeamMemberRouteSnapshot> _snapshots = new(MaxMembersPerTeam);
    private readonly List<PlayerDownedStateNetworkController> _acceleratable = new(MaxMembersPerTeam);
    private readonly List<NetworkRaidParticipant> _participantScratch = new(RaidSessionRules.MaxParticipants);

    private NetworkSpawnManager _spawnManager;
    private RaidTeamRoster _roster;
    private bool _isDisabled;

    public override void Spawned()
    {
        _members.Clear();
        _roster = null;
        _isDisabled = false;
        if (!HasStateAuthority)
        {
            return;
        }

        _spawnManager = Runner != null ? Runner.GetComponent<NetworkSpawnManager>() : null;
        if (_spawnManager == null)
        {
            _isDisabled = true;
            Debug.LogError(
                $"{nameof(DownedTeamResolutionCoordinator)} requires a {nameof(NetworkSpawnManager)} on its " +
                "runner; Accelerated Resolution is disabled.",
                this);
            return;
        }

        _spawnManager.TryGetRaidTeamRoster(out _roster);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        _members.Clear();
        _roster = null;
        _spawnManager = null;
    }

    public override void FixedUpdateNetwork()
    {
        if (_isDisabled || !HasStateAuthority || Runner == null || !Runner.IsSimulationUpdating)
        {
            return;
        }

        // Restored participants are not bound (and not resolvable) until the recovery window seals.
        if (_spawnManager.IsHostMigrationRecoveryInProgress)
        {
            return;
        }

        if (_roster == null && !TryResolveRoster())
        {
            return;
        }

        IReadOnlyList<RaidTeamId> teams = _roster.Teams;
        for (int i = 0; i < teams.Count; i++)
        {
            EvaluateTeam(teams[i]);
        }
    }

    private bool TryResolveRoster()
    {
        // The launch context can arrive after this object spawns; retrying is throttled because
        // building a roster allocates.
        if (_spawnManager == null || Runner.Tick.Raw % RosterRetryIntervalTicks != 0)
        {
            return false;
        }

        return _spawnManager.TryGetRaidTeamRoster(out _roster);
    }

    private void EvaluateTeam(RaidTeamId teamId)
    {
        if (!_roster.TryGetTeamMembers(teamId, out IReadOnlyList<RaidParticipantId> members))
        {
            return;
        }

        _snapshots.Clear();
        _acceleratable.Clear();
        for (int i = 0; i < members.Count; i++)
        {
            DownedTeamMemberRouteSnapshot snapshot = BuildSnapshot(members[i], out PlayerDownedStateNetworkController downed);
            _snapshots.Add(snapshot);
            _acceleratable.Add(DownedTeamRecoveryRouteRules.IsAcceleratable(snapshot) ? downed : null);
        }

        if (!DownedTeamRecoveryRouteRules.ShouldAccelerate(_snapshots))
        {
            return;
        }

        for (int i = 0; i < _acceleratable.Count; i++)
        {
            _acceleratable[i]?.RequestAcceleratedResolution();
        }
    }

    private DownedTeamMemberRouteSnapshot BuildSnapshot(
        RaidParticipantId participantId,
        out PlayerDownedStateNetworkController downed)
    {
        downed = null;
        _members.TryGetValue(participantId, out MemberCache cache);
        if (!IsUsable(cache.Participant))
        {
            if (cache.WasResolved)
            {
                // Seen before and despawned since: it finalized (extracted, aborted, definitive
                // disconnect), so it is known not to be Raiding and provides no route.
                return new DownedTeamMemberRouteSnapshot(false, false, false, false, false, false, RecoveryKind.None);
            }

            if (Runner.Tick.Raw < cache.NextResolveTick)
            {
                return default;
            }

            if (!TryResolveParticipant(participantId, out NetworkRaidParticipant participant))
            {
                // Unknown, not gone: it may not have joined yet or may be restored but unbound.
                cache.NextResolveTick = Runner.Tick.Raw + ParticipantRetryIntervalTicks;
                _members[participantId] = cache;
                return default;
            }

            cache = default;
            cache.WasResolved = true;
            cache.Participant = participant;
            participant.TryGetComponent(out cache.Connectivity);
        }

        bool isRaiding = cache.Participant.State == RaidParticipantState.Raiding;
        // A missing connectivity component means connected (TeamExtractionCoordinator, member snapshots).
        bool isConnected = cache.Connectivity == null || !cache.Connectivity.IsDisconnected;
        bool hasAvatar = cache.Participant.TryResolveCurrentAvatar(out NetworkObject avatar) &&
                         avatar.IsValid;
        if (hasAvatar && avatar.Id != cache.AvatarId)
        {
            cache.AvatarId = avatar.Id;
            avatar.TryGetComponent(out cache.Character);
            avatar.TryGetComponent(out cache.Downed);
            avatar.TryGetComponent(out cache.Recovery);
        }

        _members[participantId] = cache;
        if (!isRaiding)
        {
            // Extracted, Defeated or Aborted: known, no route and nothing to accelerate.
            return new DownedTeamMemberRouteSnapshot(false, hasAvatar, false, false, isConnected, false, RecoveryKind.None);
        }

        if (!hasAvatar || cache.Character == null)
        {
            // A Raiding participant always owns an avatar; until it resolves its state is unknown.
            return default;
        }

        downed = cache.Downed;
        bool canBeginSelf = cache.Recovery != null && cache.Recovery.CanBeginSelf();
        RecoveryKind sessionKind = cache.Recovery != null ? cache.Recovery.ActiveSessionKind : RecoveryKind.None;
        return new DownedTeamMemberRouteSnapshot(
            true,
            true,
            cache.Character.IsDowned,
            cache.Character.IsAlive,
            isConnected,
            canBeginSelf,
            sessionKind);
    }

    /// <summary>
    /// Resolves a participant by identity. The manager's tracked roster is the fast path, but a participant
    /// leaves it on disconnect (it is retained) and a Host-Migration-restored one is not in it until it
    /// rebinds, even though the object exists on the runner, so a throttled runner-scoped scan follows.
    /// </summary>
    private bool TryResolveParticipant(RaidParticipantId participantId, out NetworkRaidParticipant participant)
    {
        if (_spawnManager.TryGetRaidParticipant(participantId, out participant) && IsUsable(participant))
        {
            return true;
        }

        participant = null;
        _participantScratch.Clear();
        Runner.GetAllBehaviours(_participantScratch);
        for (int i = 0; i < _participantScratch.Count; i++)
        {
            NetworkRaidParticipant candidate = _participantScratch[i];
            if (IsUsable(candidate) && candidate.RaidParticipantId == participantId)
            {
                participant = candidate;
                break;
            }
        }

        _participantScratch.Clear();
        return participant != null;
    }

    private static bool IsUsable(NetworkRaidParticipant participant)
    {
        return participant != null && participant.Object != null && participant.Object.IsValid;
    }
}

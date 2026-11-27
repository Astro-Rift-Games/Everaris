using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Replicated state of every room Encounter of the raid (Doc 10 §9.2), hosted on the NetworkMatchController prefab.
/// State Authority is the only writer: it registers valid eliminations (an enemy death on State Authority; despawn and
/// raid cleanup never count) and advances the cooldown. Every peer reads the same state through <see cref="TryGetStatus"/>.
/// <para>
/// Layout: one <see cref="EncounterNetworkSlot"/> per room, in the order of the scene configuration's rooms, plus the
/// registry's own clock <see cref="ElapsedTicks"/>. The clock is replicated state, so it is restored by
/// <c>CopyStateFrom</c> after a Host Migration; unlike Runner ticks it does not depend on the new runner. It only advances
/// while an encounter is cooling down (so it is idle, and does not replicate, the rest of the raid) and is frozen while
/// the Host Migration recovery window is open, like <see cref="DungeonPressureController"/>.
/// </para>
/// <para>
/// Rules live in <see cref="EncounterLifecycle"/> through <see cref="EncounterRegistryModel"/>; this component only maps
/// them to Fusion. The model needs the scene's room parameters (cooldown, MaxReactivations), which the runner-scoped
/// <see cref="NetworkSpawnManager"/> supplies through <see cref="Configure"/> as soon as both the scene configuration and
/// this registry exist, in either order.
/// </para>
/// </summary>
[DisallowMultipleComponent]
public sealed class EncounterRegistry : NetworkBehaviour
{
    [Networked, Capacity(EncounterLimits.MaxRooms)]
    private NetworkArray<EncounterNetworkSlot> Slots => default;

    /// <summary>Registry clock in Fixed ticks. See the type remarks for why it survives Host Migration.</summary>
    [Networked]
    public int ElapsedTicks { get; private set; }

    private readonly List<int> _changedRooms = new List<int>();
    private readonly List<EncounterMemberObservation> _deferredEliminations = new List<EncounterMemberObservation>();
    private NetworkSpawnManager _spawnManager;
    private EncounterRegistryModel _model;
    private bool _isSpawned;
    private bool _restorePending;
    private bool _reportedUnboundDeath;

    // A restore can hold at most one death per member of every room.
    private const int MaxDeferredEliminations = EncounterLimits.MaxRooms * EncounterLimits.MaxMembersPerRoom;

    /// <summary>True while the networked state can be read.</summary>
    public bool IsReadable => _isSpawned && Object != null && Object.IsValid;

    /// <summary>State Authority only: the scene's rooms were bound and the registry applies the lifecycle rules.</summary>
    public bool IsConfigured => _model != null;

    /// <summary>Deaths held until the restored state is adopted. Always 0 outside a pending Host Migration restore.</summary>
    public int PendingEliminationCount => _deferredEliminations.Count;

    /// <summary>
    /// Test seam: puts the registry in the state <see cref="Spawned"/> leaves a Host Migration restore spawn in (replicated
    /// state present, no rooms bound, restore pending). <see cref="HostMigrationRestoreUtility.IsRestoreSpawn"/> needs
    /// <c>runner.IsResume</c>, which a single-peer runner can never be.
    /// </summary>
    public void Test_BeginRestoreAdoption()
    {
        _model = null;
        _deferredEliminations.Clear();
        _restorePending = HasStateAuthority;
    }

    /// <summary>Encounters with replicated state (rooms are packed from slot 0).</summary>
    public int RoomCount
    {
        get
        {
            if (!IsReadable)
            {
                return 0;
            }

            int count = 0;
            while (count < EncounterLimits.MaxRooms && EncounterSlotCodec.IsInUse(Slots.Get(count)))
            {
                count++;
            }

            return count;
        }
    }

    /// <summary>Queryable on every peer (TASK-330 S1). False when the encounter has no replicated state.</summary>
    public bool TryGetStatus(EncounterId id, out EncounterStatus status)
    {
        status = default;
        if (!id.IsValid || !IsReadable)
        {
            return false;
        }

        for (int i = 0; i < EncounterLimits.MaxRooms; i++)
        {
            EncounterNetworkSlot slot = Slots.Get(i);
            if (!EncounterSlotCodec.IsInUse(slot))
            {
                return false;
            }

            if (slot.EncounterId == id.Value)
            {
                return EncounterSlotCodec.TryGetStatus(slot, out status);
            }
        }

        return false;
    }

    public bool TryGetState(EncounterId id, out EncounterState state)
    {
        bool found = TryGetStatus(id, out EncounterStatus status);
        state = status.State;
        return found;
    }

    public override void Spawned()
    {
        _isSpawned = true;
        _spawnManager = Runner.GetComponent<NetworkSpawnManager>();

        // A fresh spawn starts empty (networked defaults) and needs no initialization. A restored spawn already carries
        // the copied state, which the first Configure adopts instead of resetting it.
        _restorePending = HasStateAuthority && HostMigrationRestoreUtility.IsRestoreSpawn(this);

        if (_spawnManager == null)
        {
            Debug.LogError($"[{nameof(EncounterRegistry)}] The runner has no {nameof(NetworkSpawnManager)}; encounter state cannot be bound to the scene.", this);
            return;
        }

        _spawnManager.BindEncounterRegistry(this);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        _isSpawned = false;
        if (_spawnManager != null)
        {
            _spawnManager.UnbindEncounterRegistry(this);
        }

        _spawnManager = null;
        _model = null;
    }

    private void OnDestroy()
    {
        _isSpawned = false;
    }

    /// <summary>
    /// Binds the scene's rooms (room index = position in <paramref name="rooms"/>). A fresh raid starts every room
    /// NotActivated; the first call on a Host Migration restore adopts the replicated state instead. Idempotent for the
    /// same rooms. State Authority only.
    /// </summary>
    public bool Configure(IReadOnlyList<EncounterRoomDefinition> rooms)
    {
        if (!HasStateAuthority || !IsReadable)
        {
            return false;
        }

        if (_model != null && _model.Matches(rooms))
        {
            return true;
        }

        if (!EncounterRegistryModel.TryCreate(rooms, Runner.DeltaTime, out EncounterRegistryModel model, out string failure))
        {
            string reason = $"Cannot bind the scene's Encounter rooms: {failure}";
            Debug.LogError($"[{nameof(EncounterRegistry)}] {reason}", this);
            DiscardUnadoptedRestore(reason);
            return false;
        }

        _model = model;
        if (_restorePending)
        {
            AdoptReplicatedState(model);
        }
        else
        {
            PublishFreshState(model);
        }

        _restorePending = false;
        _reportedUnboundDeath = false;
        ApplyDeferredEliminations();
        return true;
    }

    /// <summary>Moves a spawned room to Active with the member keys of its enemies. State Authority only.</summary>
    public bool TryActivate(EncounterId id, IReadOnlyList<uint> memberKeys)
    {
        if (!HasStateAuthority || !IsReadable || _model == null || !_model.TryActivate(id, memberKeys, out int roomIndex))
        {
            return false;
        }

        WriteRoom(roomIndex);
        return true;
    }

    /// <summary>
    /// Registers the death of an Encounter member on State Authority (a valid elimination). Idempotent. Only while a Host
    /// Migration restore is pending (before its first <see cref="Configure"/>) is a death kept and applied by that
    /// Configure; with no bound rooms and no restore pending (raid closed, scene without rooms) it is dropped with a
    /// single warning, so it can never reach a later raid's rooms. Returns true when it changed the encounter.
    /// </summary>
    public bool NotifyMemberDeath(EncounterId id, uint memberKey)
    {
        if (!HasStateAuthority || !IsReadable)
        {
            return false;
        }

        if (_model == null)
        {
            if (_restorePending && _deferredEliminations.Count < MaxDeferredEliminations)
            {
                _deferredEliminations.Add(new EncounterMemberObservation(id.Value, memberKey, false));
                return false;
            }

            if (!_reportedUnboundDeath)
            {
                _reportedUnboundDeath = true;
                Debug.LogWarning(
                    $"[{nameof(EncounterRegistry)}] The death of member {memberKey} of {id} was dropped: no Encounter rooms are bound " +
                    (_restorePending ? "and the restore queue is full." : "and no restore is pending (raid closed or scene without rooms). Further drops are not reported until rooms are bound."),
                    this);
            }

            return false;
        }

        return ApplyElimination(id, memberKey);
    }

    /// <summary>
    /// Compares the restored state with the restored enemies (call once after a Host Migration restore) and logs every
    /// discrepancy. The registry stays authoritative; only a member the registry still lists alive whose enemy was restored
    /// dead is eliminated, since an enemy is the source of truth for its own death. State Authority only.
    /// </summary>
    public void Reconcile(IReadOnlyList<EncounterMemberObservation> observations)
    {
        if (!HasStateAuthority || !IsReadable)
        {
            return;
        }

        var slots = new List<EncounterNetworkSlot>(EncounterLimits.MaxRooms);
        for (int i = 0; i < EncounterLimits.MaxRooms; i++)
        {
            slots.Add(Slots.Get(i));
        }

        EncounterReconcileResult result = EncounterReconciler.Compare(slots, observations);
        if (result.IsConsistent)
        {
            Debug.Log($"[{nameof(EncounterRegistry)}] {result.Log}", this);
            return;
        }

        Debug.LogWarning($"[{nameof(EncounterRegistry)}] {result.Log}", this);
        foreach (EncounterMemberObservation death in result.UnrecordedDeaths)
        {
            NotifyMemberDeath(new EncounterId(death.EncounterId), death.MemberKey);
        }
    }

    /// <summary>
    /// A pending Host Migration restore cannot be adopted (the restored scene has no rooms, its room set is invalid or
    /// <see cref="Configure"/> failed). The copied slots no longer describe any room: they are cleared, the held deaths
    /// are dropped and the restore is no longer pending, so scene changes and a later fresh Configure behave normally.
    /// Logs an error with the reason and what was dropped. No effect without a pending restore. State Authority only.
    /// </summary>
    public void DiscardUnadoptedRestore(string reason)
    {
        if (!_restorePending)
        {
            return;
        }

        int replicated = RoomCount;
        int held = _deferredEliminations.Count;
        _restorePending = false;
        ClearState();
        Debug.LogError(
            $"[{nameof(EncounterRegistry)}] Restored encounter state could not be adopted ({reason}); " +
            $"{replicated} replicated encounters and {held} held deaths were dropped.",
            this);
    }

    /// <summary>The scene is being replaced: no encounter state carries over. Ignored while a restore still has to be adopted.</summary>
    public void ResetForSceneChange()
    {
        if (_restorePending)
        {
            return;
        }

        ClearState();
    }

    /// <summary>The raid is closing: no encounter state carries over to the next raid.</summary>
    public void ResetForRaidClosure()
    {
        _restorePending = false;
        ClearState();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || _model == null || !_model.IsCooling)
        {
            return;
        }

        if (_spawnManager != null && _spawnManager.IsHostMigrationRecoveryInProgress)
        {
            return;
        }

        ElapsedTicks++;
        _changedRooms.Clear();
        _model.Advance(ElapsedTicks, _changedRooms);
        for (int i = 0; i < _changedRooms.Count; i++)
        {
            WriteRoom(_changedRooms[i]);
        }
    }

    private bool ApplyElimination(EncounterId id, uint memberKey)
    {
        if (!_model.RegisterValidElimination(id, memberKey, ElapsedTicks, out int roomIndex))
        {
            return false;
        }

        WriteRoom(roomIndex);
        if (EncounterSlotCodec.TryGetStatus(Slots.Get(roomIndex), out EncounterStatus status) && status.State == EncounterState.Resolved)
        {
            Debug.Log($"[{nameof(EncounterRegistry)}] {id} resolved at registry tick {ElapsedTicks}; its cooldown starts now.", this);
        }

        return true;
    }

    private void ApplyDeferredEliminations()
    {
        for (int i = 0; i < _deferredEliminations.Count; i++)
        {
            EncounterMemberObservation death = _deferredEliminations[i];
            ApplyElimination(new EncounterId(death.EncounterId), death.MemberKey);
        }

        _deferredEliminations.Clear();
    }

    private void PublishFreshState(EncounterRegistryModel model)
    {
        for (int i = 0; i < EncounterLimits.MaxRooms; i++)
        {
            Slots.Set(i, i < model.RoomCount && model.TryCapture(i, out EncounterNetworkSlot slot) ? slot : default);
        }

        ElapsedTicks = 0;
    }

    /// <summary>
    /// Host Migration: the replicated slots were copied with the object. A slot that is not the consistent state of the
    /// scene's room at that index cannot be trusted: it is reported and that room restarts NotActivated (logged, never silent).
    /// </summary>
    private void AdoptReplicatedState(EncounterRegistryModel model)
    {
        for (int i = 0; i < EncounterLimits.MaxRooms; i++)
        {
            EncounterNetworkSlot replicated = Slots.Get(i);
            if (i >= model.RoomCount)
            {
                if (EncounterSlotCodec.IsInUse(replicated))
                {
                    Debug.LogError($"[{nameof(EncounterRegistry)}] Restored state of Encounter#{replicated.EncounterId} has no room at index {i} in this scene; it was discarded.", this);
                    Slots.Set(i, default);
                }

                continue;
            }

            if (!model.TryRestoreRoom(i, replicated, out string failure))
            {
                Debug.LogError($"[{nameof(EncounterRegistry)}] Restored encounter state discarded: {failure} The room restarts NotActivated.", this);
                WriteRoom(i);
            }
        }
    }

    private void ClearState()
    {
        _model = null;
        _deferredEliminations.Clear();
        if (!HasStateAuthority || !IsReadable)
        {
            return;
        }

        for (int i = 0; i < EncounterLimits.MaxRooms; i++)
        {
            Slots.Set(i, default);
        }

        ElapsedTicks = 0;
    }

    private void WriteRoom(int roomIndex)
    {
        if (_model.TryCapture(roomIndex, out EncounterNetworkSlot slot))
        {
            Slots.Set(roomIndex, slot);
            return;
        }

        Debug.LogError($"[{nameof(EncounterRegistry)}] Encounter state of room index {roomIndex} cannot be replicated.", this);
    }
}

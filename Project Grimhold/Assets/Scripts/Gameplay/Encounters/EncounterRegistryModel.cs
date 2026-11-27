using System.Collections.Generic;

/// <summary>
/// Pure state-authority model of every room Encounter of a raid: one <see cref="EncounterLifecycle"/> per room, addressed by
/// room index (position in the scene configuration) or by EncounterId. It owns no Fusion state; the registry copies the
/// slot of every room it reports as changed. Time is a registry tick counter, converted with a fixed tick length.
/// </summary>
public sealed class EncounterRegistryModel
{
    private readonly EncounterRoomDefinition[] _definitions;
    private readonly EncounterLifecycle[] _lifecycles;
    private readonly Dictionary<int, int> _indexById;
    private readonly double _tickSeconds;

    private EncounterRegistryModel(EncounterRoomDefinition[] definitions, double tickSeconds)
    {
        _definitions = definitions;
        _tickSeconds = tickSeconds;
        _lifecycles = new EncounterLifecycle[definitions.Length];
        _indexById = new Dictionary<int, int>(definitions.Length);
        for (int i = 0; i < definitions.Length; i++)
        {
            _lifecycles[i] = new EncounterLifecycle(definitions[i].Id, definitions[i].Settings);
            _indexById.Add(definitions[i].Id.Value, i);
        }
    }

    public int RoomCount => _lifecycles.Length;

    /// <summary>True while any room is Resolved or Cooldown, that is while the registry clock must advance.</summary>
    public bool IsCooling
    {
        get
        {
            for (int i = 0; i < _lifecycles.Length; i++)
            {
                if (_lifecycles[i].State == EncounterState.Resolved || _lifecycles[i].State == EncounterState.Cooldown)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static bool TryCreate(IReadOnlyList<EncounterRoomDefinition> rooms, double tickSeconds, out EncounterRegistryModel model, out string failure)
    {
        model = null;
        if (rooms == null)
        {
            failure = "Encounter rooms are required.";
            return false;
        }

        if (!(tickSeconds > 0d) || double.IsInfinity(tickSeconds))
        {
            failure = $"The registry tick length must be positive, was {tickSeconds}.";
            return false;
        }

        if (rooms.Count > EncounterLimits.MaxRooms)
        {
            failure = $"{rooms.Count} Encounter rooms exceed the replicated capacity of {EncounterLimits.MaxRooms}.";
            return false;
        }

        var seen = new HashSet<int>();
        var definitions = new EncounterRoomDefinition[rooms.Count];
        for (int i = 0; i < rooms.Count; i++)
        {
            if (!rooms[i].Id.IsValid)
            {
                failure = $"Encounter room at index {i} has no valid EncounterId.";
                return false;
            }

            if (!seen.Add(rooms[i].Id.Value))
            {
                failure = $"Duplicate EncounterId {rooms[i].Id.Value} at room index {i}.";
                return false;
            }

            definitions[i] = rooms[i];
        }

        model = new EncounterRegistryModel(definitions, tickSeconds);
        failure = string.Empty;
        return true;
    }

    public bool Matches(IReadOnlyList<EncounterRoomDefinition> rooms)
    {
        if (rooms == null || rooms.Count != _definitions.Length)
        {
            return false;
        }

        for (int i = 0; i < _definitions.Length; i++)
        {
            if (!_definitions[i].Equals(rooms[i]))
            {
                return false;
            }
        }

        return true;
    }

    public EncounterId IdOf(int roomIndex) => _definitions[roomIndex].Id;

    public bool TryGetRoomIndex(EncounterId id, out int roomIndex)
    {
        roomIndex = -1;
        return id.IsValid && _indexById.TryGetValue(id.Value, out roomIndex);
    }

    public bool TryActivate(EncounterId id, IReadOnlyList<uint> memberKeys, out int roomIndex)
    {
        if (!TryGetRoomIndex(id, out roomIndex) || memberKeys == null)
        {
            return false;
        }

        // A key that cannot be replicated must never reach the lifecycle: the registry could not publish it.
        for (int i = 0; i < memberKeys.Count; i++)
        {
            if (!EncounterSlotCodec.TryKeyToBit(memberKeys[i], out _))
            {
                return false;
            }
        }

        return _lifecycles[roomIndex].TryActivate(memberKeys);
    }

    /// <summary>Registers a death on State Authority. Idempotent: false for an unknown room or member, a repeated death or an inactive room.</summary>
    public bool RegisterValidElimination(EncounterId id, uint memberKey, int nowTick, out int roomIndex) =>
        TryGetRoomIndex(id, out roomIndex) && _lifecycles[roomIndex].RegisterValidElimination(memberKey, nowTick * _tickSeconds);

    /// <summary>Advances every room to <paramref name="nowTick"/> and appends the indices of the rooms whose state changed.</summary>
    public void Advance(int nowTick, List<int> changedRooms)
    {
        double now = nowTick * _tickSeconds;
        for (int i = 0; i < _lifecycles.Length; i++)
        {
            EncounterState before = _lifecycles[i].State;
            _lifecycles[i].Advance(now);
            if (_lifecycles[i].State != before)
            {
                changedRooms.Add(i);
            }
        }
    }

    public bool TryCapture(int roomIndex, out EncounterNetworkSlot slot)
    {
        slot = default;
        return IsRoomIndex(roomIndex) &&
               EncounterSlotCodec.TryEncode(_definitions[roomIndex].Id, _lifecycles[roomIndex].CaptureSnapshot(), _tickSeconds, out slot);
    }

    /// <summary>Adopts a replicated slot into a room. False, with the room untouched, when it is not that room's consistent state.</summary>
    public bool TryRestoreRoom(int roomIndex, in EncounterNetworkSlot slot, out string failure)
    {
        if (!IsRoomIndex(roomIndex))
        {
            failure = $"Room index {roomIndex} is outside 0..{_lifecycles.Length - 1}.";
            return false;
        }

        EncounterId expected = _definitions[roomIndex].Id;
        if (!EncounterSlotCodec.TryDecode(slot, _tickSeconds, out EncounterId slotId, out EncounterLifecycleSnapshot snapshot))
        {
            failure = $"The replicated slot of room index {roomIndex} ({expected}) is unused or undecodable.";
            return false;
        }

        if (slotId != expected)
        {
            failure = $"The replicated slot of room index {roomIndex} belongs to {slotId}, expected {expected}.";
            return false;
        }

        if (!_lifecycles[roomIndex].TryRestore(snapshot))
        {
            failure = $"The replicated slot of {expected} is not a consistent Encounter state (state={snapshot.State}, " +
                      $"members={snapshot.MemberKeys.Count}, alive={snapshot.AliveMemberKeys.Count}, reactivations={snapshot.ReactivationCount}).";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private bool IsRoomIndex(int roomIndex) => roomIndex >= 0 && roomIndex < _lifecycles.Length;
}

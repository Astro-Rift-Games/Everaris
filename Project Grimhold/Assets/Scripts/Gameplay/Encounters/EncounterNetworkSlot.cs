using Fusion;

/// <summary>
/// Replicated state of one room Encounter (one element of <see cref="EncounterRegistry"/>'s array). Plain data only;
/// the rules live in <see cref="EncounterLifecycle"/> and the mapping in <see cref="EncounterSlotCodec"/>.
/// </summary>
public struct EncounterNetworkSlot : INetworkStruct
{
    /// <summary>EncounterId value of the room that owns this slot. 0 marks an unused slot.</summary>
    public int EncounterId;

    /// <summary><see cref="EncounterState"/> as a byte.</summary>
    public byte StateRaw;

    public int ReactivationCount;

    /// <summary>Bit (key - 1) is set for every member key of the current cycle, alive or eliminated.</summary>
    public uint MemberMask;

    /// <summary>Bit (key - 1) is set for every member of the current cycle that was not eliminated yet.</summary>
    public uint AliveMask;

    /// <summary>Registry clock tick at which the current cooldown started (the tick the encounter was resolved).</summary>
    public int CooldownStartTick;
}

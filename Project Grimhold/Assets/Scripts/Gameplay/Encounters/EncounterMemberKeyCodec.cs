/// <summary>
/// Cycle-aware Encounter member key (the <c>uint EncounterMemberKey</c> an enemy carries). A room reactivates: the dead bodies of
/// earlier cycles keep the same EncounterId, so the key must also name the cycle or a stale body could be mistaken for a member of
/// the current one. Layout: member index (1..<see cref="EncounterLimits.MaxMembersPerRoom"/>) in the low
/// <see cref="IndexBits"/> bits, cycle (the room's ReactivationCount when the member spawned) above them. Cycle 0 is the plain
/// index, so keys of the first activation are unchanged. Costs no extra networked word on the enemy.
/// </summary>
public static class EncounterMemberKeyCodec
{
    public const int IndexBits = 8;
    public const uint IndexMask = (1u << IndexBits) - 1u;

    /// <summary>Highest cycle a key can carry (24 bits). Room validation keeps MaxReactivations within it.</summary>
    public const int MaxCycle = (1 << (32 - IndexBits)) - 1;

    /// <summary>Builds the key of member <paramref name="index"/> for <paramref name="cycle"/>. False for an index or cycle out of range.</summary>
    public static bool TryCompose(int cycle, uint index, out uint key)
    {
        key = 0u;
        if (cycle < 0 || cycle > MaxCycle || index == 0u || index > EncounterLimits.MaxMembersPerRoom)
        {
            return false;
        }

        key = ((uint)cycle << IndexBits) | index;
        return true;
    }

    /// <summary>Splits a key. False when it names no member (index 0). The index is not range-checked against the replicable members.</summary>
    public static bool TryDecode(uint key, out int cycle, out uint index)
    {
        index = key & IndexMask;
        cycle = (int)(key >> IndexBits);
        if (index != 0u)
        {
            return true;
        }

        cycle = 0;
        return false;
    }
}

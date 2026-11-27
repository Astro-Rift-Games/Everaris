using System;
using System.Collections.Generic;

/// <summary>
/// Maps <see cref="EncounterLifecycleSnapshot"/> to and from the replicated <see cref="EncounterNetworkSlot"/>. Member
/// keys 1..<see cref="EncounterLimits.MaxMembersPerRoom"/> map to bit (key - 1) of the masks; anything outside that range
/// cannot be replicated and is rejected. The cooldown start is stored in registry ticks.
/// </summary>
public static class EncounterSlotCodec
{
    public static bool TryKeyToBit(uint memberKey, out uint bit)
    {
        if (memberKey == 0u || memberKey > EncounterLimits.MaxMembersPerRoom)
        {
            bit = 0u;
            return false;
        }

        bit = 1u << (int)(memberKey - 1u);
        return true;
    }

    /// <summary>A slot that belongs to <paramref name="id"/> and holds a NotActivated encounter.</summary>
    public static EncounterNetworkSlot Empty(EncounterId id) => new EncounterNetworkSlot
    {
        EncounterId = id.Value,
        StateRaw = (byte)EncounterState.NotActivated,
    };

    public static bool IsInUse(in EncounterNetworkSlot slot) => slot.EncounterId > 0;

    /// <summary>
    /// Encodes a snapshot. Returns false (slot default) for an invalid id, a non-positive tick length, an undefined state or
    /// a member key outside the replicable range.
    /// </summary>
    public static bool TryEncode(EncounterId id, in EncounterLifecycleSnapshot snapshot, double tickSeconds, out EncounterNetworkSlot slot)
    {
        slot = default;
        if (!id.IsValid || !IsValidTickLength(tickSeconds) || !Enum.IsDefined(typeof(EncounterState), snapshot.State))
        {
            return false;
        }

        if (!TryMask(snapshot.MemberKeys, out uint memberMask) || !TryMask(snapshot.AliveMemberKeys, out uint aliveMask))
        {
            return false;
        }

        slot = new EncounterNetworkSlot
        {
            EncounterId = id.Value,
            StateRaw = (byte)snapshot.State,
            ReactivationCount = snapshot.ReactivationCount,
            MemberMask = memberMask,
            AliveMask = aliveMask,
            CooldownStartTick = (int)Math.Round(snapshot.CooldownStartTime / tickSeconds),
        };
        return true;
    }

    /// <summary>
    /// Decodes a slot. Only maps data: whether the result is a consistent lifecycle state is decided by
    /// <see cref="EncounterLifecycle.TryRestore"/>. Returns false for an unused slot, an undefined state or a non-positive tick length.
    /// </summary>
    public static bool TryDecode(in EncounterNetworkSlot slot, double tickSeconds, out EncounterId id, out EncounterLifecycleSnapshot snapshot)
    {
        id = EncounterId.None;
        snapshot = default;
        if (!IsInUse(slot) || !IsValidTickLength(tickSeconds) || !Enum.IsDefined(typeof(EncounterState), (EncounterState)slot.StateRaw))
        {
            return false;
        }

        id = new EncounterId(slot.EncounterId);
        snapshot = new EncounterLifecycleSnapshot(
            (EncounterState)slot.StateRaw,
            slot.ReactivationCount,
            slot.CooldownStartTick * tickSeconds,
            Keys(slot.MemberMask),
            Keys(slot.AliveMask));
        return true;
    }

    public static bool TryGetStatus(in EncounterNetworkSlot slot, out EncounterStatus status)
    {
        status = default;
        if (!IsInUse(slot) || !Enum.IsDefined(typeof(EncounterState), (EncounterState)slot.StateRaw))
        {
            return false;
        }

        status = new EncounterStatus(
            new EncounterId(slot.EncounterId),
            (EncounterState)slot.StateRaw,
            slot.ReactivationCount,
            PopCount(slot.MemberMask),
            PopCount(slot.AliveMask));
        return true;
    }

    public static bool IsAliveMember(in EncounterNetworkSlot slot, uint memberKey) =>
        TryKeyToBit(memberKey, out uint bit) && (slot.AliveMask & bit) != 0u;

    private static int PopCount(uint mask)
    {
        int count = 0;
        for (; mask != 0u; mask &= mask - 1u)
        {
            count++;
        }

        return count;
    }

    private static bool IsValidTickLength(double tickSeconds) => tickSeconds > 0d && !double.IsInfinity(tickSeconds);

    private static bool TryMask(IReadOnlyList<uint> keys, out uint mask)
    {
        mask = 0u;
        for (int i = 0; i < keys.Count; i++)
        {
            if (!TryKeyToBit(keys[i], out uint bit))
            {
                mask = 0u;
                return false;
            }

            mask |= bit;
        }

        return true;
    }

    private static uint[] Keys(uint mask)
    {
        var keys = new uint[PopCount(mask)];
        int next = 0;
        for (uint key = 1; key <= EncounterLimits.MaxMembersPerRoom; key++)
        {
            if ((mask & (1u << (int)(key - 1u))) != 0u)
            {
                keys[next++] = key;
            }
        }

        return keys;
    }
}

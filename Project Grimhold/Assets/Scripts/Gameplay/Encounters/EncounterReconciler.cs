using System.Collections.Generic;
using System.Text;

/// <summary>
/// Compares the replicated Encounter state restored after a Host Migration with the enemies restored with it. The registry
/// is authoritative: it was snapshotted on the same tick as the enemies. The enemies are trusted only about their own
/// death (a registry-alive member restored dead is an unrecorded elimination); every other mismatch is reported, never
/// silently changed.
/// <para>
/// Enemy keys are cycle-aware (<see cref="EncounterMemberKeyCodec"/>): an observation is compared with the room only when it
/// belongs to the room's current cycle (its ReactivationCount). Bodies of earlier cycles share EncounterId and member indices with
/// the current members but are not members, so they are counted in <see cref="EncounterReconcileResult.IgnoredOtherCycle"/> and
/// otherwise ignored.
/// </para>
/// </summary>
public static class EncounterReconciler
{
    private const int MaxLoggedPerKind = 5;

    public static EncounterReconcileResult Compare(IReadOnlyList<EncounterNetworkSlot> slots, IReadOnlyList<EncounterMemberObservation> observations)
    {
        var result = new EncounterReconcileResult();
        var slotById = new Dictionary<int, EncounterNetworkSlot>();
        int livingMembers = 0;
        if (slots != null)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                EncounterNetworkSlot slot = slots[i];
                if (EncounterSlotCodec.TryGetStatus(slot, out _) && slotById.TryAdd(slot.EncounterId, slot))
                {
                    livingMembers += slot.StateRaw == (byte)EncounterState.Active ? CountBits(slot.AliveMask) : 0;
                }
            }
        }

        var observedKeys = new HashSet<long>();
        if (observations != null)
        {
            for (int i = 0; i < observations.Count; i++)
            {
                EncounterMemberObservation observation = observations[i];
                bool hasRoom = slotById.TryGetValue(observation.EncounterId, out EncounterNetworkSlot slot);
                bool decoded = EncounterMemberKeyCodec.TryDecode(observation.MemberKey, out int cycle, out uint memberIndex);
                if (hasRoom && decoded && cycle != slot.ReactivationCount)
                {
                    result.IgnoredOtherCycle++;
                    continue;
                }

                uint comparedKey = decoded ? memberIndex : observation.MemberKey;
                observedKeys.Add(KeyOf(observation.EncounterId, comparedKey));
                bool recordedAlive = hasRoom &&
                                     slot.StateRaw == (byte)EncounterState.Active &&
                                     EncounterSlotCodec.IsAliveMember(slot, comparedKey);
                if (observation.IsAlive && !recordedAlive)
                {
                    result.UnexpectedLiving.Add(observation);
                }
                else if (!observation.IsAlive && recordedAlive)
                {
                    result.UnrecordedDeaths.Add(observation);
                }
            }
        }

        if (slots != null)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                EncounterNetworkSlot slot = slots[i];
                if (!slotById.TryGetValue(slot.EncounterId, out EncounterNetworkSlot owner) || !owner.Equals(slot) ||
                    slot.StateRaw != (byte)EncounterState.Active)
                {
                    continue;
                }

                for (uint key = 1; key <= EncounterLimits.MaxMembersPerRoom; key++)
                {
                    if (EncounterSlotCodec.IsAliveMember(slot, key) && !observedKeys.Contains(KeyOf(slot.EncounterId, key)))
                    {
                        uint reportedKey = EncounterMemberKeyCodec.TryCompose(slot.ReactivationCount, key, out uint composed) ? composed : key;
                        result.MissingMembers.Add(new EncounterMemberObservation(slot.EncounterId, reportedKey, false));
                    }
                }
            }
        }

        result.Log = BuildLog(result, slotById.Count, livingMembers) + IgnoredNote(result.IgnoredOtherCycle);
        return result;
    }

    private static string IgnoredNote(int ignored) =>
        ignored > 0 ? $" Ignored {ignored} observations of earlier reactivation cycles." : string.Empty;

    private static long KeyOf(int encounterId, uint memberKey) => ((long)encounterId << 32) | memberKey;

    private static int CountBits(uint mask)
    {
        int count = 0;
        for (; mask != 0u; mask &= mask - 1u)
        {
            count++;
        }

        return count;
    }

    private static string BuildLog(EncounterReconcileResult result, int encounters, int livingMembers)
    {
        if (result.IsConsistent)
        {
            return $"Host Migration encounter reconciliation successful. Encounters: {encounters}, living members: {livingMembers}.";
        }

        var log = new StringBuilder(160);
        log.Append("Host Migration encounter reconciliation mismatch: ")
            .Append("unrecorded deaths=").Append(result.UnrecordedDeaths.Count)
            .Append(", missing members=").Append(result.MissingMembers.Count)
            .Append(", unexpected living=").Append(result.UnexpectedLiving.Count)
            .Append('.');
        AppendKind(log, "unrecorded", result.UnrecordedDeaths);
        AppendKind(log, "missing", result.MissingMembers);
        AppendKind(log, "unexpected", result.UnexpectedLiving);
        return log.ToString();
    }

    private static void AppendKind(StringBuilder log, string label, List<EncounterMemberObservation> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        log.Append(' ').Append(label).Append(':');
        int shown = entries.Count < MaxLoggedPerKind ? entries.Count : MaxLoggedPerKind;
        for (int i = 0; i < shown; i++)
        {
            log.Append(" Encounter#").Append(entries[i].EncounterId).Append('/').Append(entries[i].MemberKey);
        }

        if (entries.Count > shown)
        {
            log.Append(" (+").Append(entries.Count - shown).Append(" more)");
        }

        log.Append('.');
    }
}

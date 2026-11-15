/// <summary>
/// Absolute closure resolves every unresolved raider. Connectivity and avatar Downed state
/// do not exempt a participant; terminal outcomes must never be resolved a second time.
/// </summary>
public static class AbsoluteClosureRules
{
    public static bool ShouldDefeat(RaidParticipantState state) => state == RaidParticipantState.Raiding;

    public static bool TryResolveDefeat(bool hasStateAuthority, RaidParticipantState currentState,
        int currentSequence, bool disconnected, out RaidParticipantState state,
        out int sequence, out ExpeditionProgressionFinalizationCause cause)
    {
        state = currentState;
        sequence = currentSequence;
        cause = ExpeditionProgressionFinalizationCause.None;
        if (!hasStateAuthority || !ShouldDefeat(currentState)) return false;
        state = RaidParticipantState.Defeated;
        sequence++;
        cause = disconnected ? ExpeditionProgressionFinalizationCause.DefeatWhileDisconnectedConfirmed
            : ExpeditionProgressionFinalizationCause.DefeatConfirmed;
        return true;
    }
}

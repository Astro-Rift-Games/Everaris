/// <summary>Read model of one Encounter's replicated state, valid on every peer (TASK-330 S1).</summary>
public readonly struct EncounterStatus
{
    public EncounterStatus(EncounterId id, EncounterState state, int reactivationCount, int memberCount, int aliveMemberCount)
    {
        Id = id;
        State = state;
        ReactivationCount = reactivationCount;
        MemberCount = memberCount;
        AliveMemberCount = aliveMemberCount;
    }

    public EncounterId Id { get; }
    public EncounterState State { get; }
    public int ReactivationCount { get; }

    /// <summary>Members of the current cycle (alive or eliminated). 0 until the first activation.</summary>
    public int MemberCount { get; }

    public int AliveMemberCount { get; }
}

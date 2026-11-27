/// <summary>An enemy found after a Host Migration restore, with the Encounter identity it kept (EncounterIdValue/MemberKey).</summary>
public readonly struct EncounterMemberObservation
{
    public EncounterMemberObservation(int encounterId, uint memberKey, bool isAlive)
    {
        EncounterId = encounterId;
        MemberKey = memberKey;
        IsAlive = isAlive;
    }

    public int EncounterId { get; }
    public uint MemberKey { get; }
    public bool IsAlive { get; }
}

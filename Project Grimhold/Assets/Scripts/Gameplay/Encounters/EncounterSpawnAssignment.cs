/// <summary>One planned enemy bound to a room spawn point and an Encounter member key.</summary>
public readonly struct EncounterSpawnAssignment<T>
{
    public EncounterSpawnAssignment(Spawning.EnemySpawnCandidate<T> candidate, int spawnPointIndex, uint memberKey)
    {
        Candidate = candidate;
        SpawnPointIndex = spawnPointIndex;
        MemberKey = memberKey;
    }

    public Spawning.EnemySpawnCandidate<T> Candidate { get; }
    public int SpawnPointIndex { get; }
    public uint MemberKey { get; }
}

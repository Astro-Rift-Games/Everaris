/// <summary>World conditions for a reactivation request that the encounter itself cannot know (Doc 10 §9.2).</summary>
public readonly struct EncounterReactivationContext
{
    public EncounterReactivationContext(bool playersInGenerationZone, bool globalCapacityAvailable, bool dungeonPhaseAllowsGeneration)
    {
        PlayersInGenerationZone = playersInGenerationZone;
        GlobalCapacityAvailable = globalCapacityAvailable;
        DungeonPhaseAllowsGeneration = dungeonPhaseAllowsGeneration;
    }

    public bool PlayersInGenerationZone { get; }
    public bool GlobalCapacityAvailable { get; }
    public bool DungeonPhaseAllowsGeneration { get; }
}

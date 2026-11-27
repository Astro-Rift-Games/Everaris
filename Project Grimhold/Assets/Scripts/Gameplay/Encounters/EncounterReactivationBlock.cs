/// <summary>First Doc 10 §9.2 reactivation condition that is not met; None means the room may reactivate.</summary>
public enum EncounterReactivationBlock : byte
{
    None = 0,
    NotResolved = 1,
    CooldownPending = 2,
    MaxReactivationsReached = 3,
    PlayersInGenerationZone = 4,
    NoGlobalCapacity = 5,
    DungeonPhaseForbidsGeneration = 6
}

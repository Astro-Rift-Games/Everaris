/// <summary>Outcome of building one room Encounter composition (Doc 10 §9.1).</summary>
public enum EncounterPlanResult : byte
{
    /// <summary>The initial weighted validation decided the room has no Encounter.</summary>
    NoEncounter = 0,
    Planned = 1,
    /// <summary>No composition satisfies MinEnemies within the budget, allowed archetypes and spatial capacity.</summary>
    Invalid = 2
}

/// <summary>Builds the world half of a reactivation request (Doc 10 §9.2) from raw measurements.</summary>
public static class EncounterReactivationRules
{
    /// <summary>
    /// The planned composition must fit the Spawn Population Budget: <paramref name="availableGlobalCapacity"/> must cover every enemy
    /// of the plan (at least one).
    /// </summary>
    public static bool CapacityFits(int availableGlobalCapacity, int plannedCount) =>
        plannedCount > 0 && availableGlobalCapacity >= plannedCount;

    public static EncounterReactivationContext BuildContext(bool playersInGenerationZone, int availableGlobalCapacity, int plannedCount, bool dungeonPhaseAllowsGeneration) =>
        new EncounterReactivationContext(playersInGenerationZone, CapacityFits(availableGlobalCapacity, plannedCount), dungeonPhaseAllowsGeneration);
}

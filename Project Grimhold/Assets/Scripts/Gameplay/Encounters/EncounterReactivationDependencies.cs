/// <summary>
/// What the registry needs, beyond the rooms, to reactivate them: the Dungeon pressure config (source of MaxGlobalEnemies, the
/// Spawn Population Budget) and the pressure controller on the same GameObject (source of the Dungeon phase). Without them every
/// reactivation would be blocked silently, so the registry reports the problem once instead.
/// </summary>
public static class EncounterReactivationDependencies
{
    public static bool TryValidate(DungeonPressureConfig pressureConfig, bool hasPressureController, out string failure)
    {
        if (pressureConfig == null)
        {
            failure = "'_pressureConfig' is not assigned on the EncounterRegistry (assign the same DungeonPressureConfig as the DungeonPressureController). " +
                      "Without MaxGlobalEnemies the Spawn Population Budget is unknown and every Encounter reactivation is blocked.";
            return false;
        }

        if (pressureConfig.MaxGlobalEnemies <= 0)
        {
            failure = $"'{pressureConfig.name}' has MaxGlobalEnemies {pressureConfig.MaxGlobalEnemies}; it must be positive or every Encounter reactivation is blocked.";
            return false;
        }

        if (!hasPressureController)
        {
            failure = $"The EncounterRegistry's GameObject has no {nameof(DungeonPressureController)}, so the Dungeon phase is unknown and every Encounter reactivation is blocked.";
            return false;
        }

        failure = string.Empty;
        return true;
    }
}

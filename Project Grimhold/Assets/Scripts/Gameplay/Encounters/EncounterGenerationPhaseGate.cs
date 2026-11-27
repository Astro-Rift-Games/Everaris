/// <summary>The "Dungeon phase allows generating" condition of Doc 10 §9.2.</summary>
public static class EncounterGenerationPhaseGate
{
    public static EncounterGenerationPhases FlagOf(DungeonPressurePhase phase)
    {
        switch (phase)
        {
            case DungeonPressurePhase.Normal: return EncounterGenerationPhases.Normal;
            case DungeonPressurePhase.Reinforcements: return EncounterGenerationPhases.Reinforcements;
            case DungeonPressurePhase.CriticalPressure: return EncounterGenerationPhases.CriticalPressure;
            case DungeonPressurePhase.Collapse: return EncounterGenerationPhases.Collapse;
            default: return EncounterGenerationPhases.None;
        }
    }

    /// <summary>
    /// True when <paramref name="phase"/> is in <paramref name="allowed"/>. With the Dungeon pressure not running (match not in progress,
    /// timer not started or stopped) there is no phase to allow generating, so the gate is closed.
    /// </summary>
    public static bool Allows(EncounterGenerationPhases allowed, bool pressureRunning, DungeonPressurePhase phase) =>
        pressureRunning && (allowed & FlagOf(phase)) != EncounterGenerationPhases.None;
}

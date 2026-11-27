using System;

/// <summary>
/// Dungeon pressure phases in which a resolved room may regenerate its Encounter (Doc 10 §9.2: "la fase de Dungeon permite
/// generar"). Doc 10 does not say which phases those are: the default allows all of them (non-GD default, pending Game Design).
/// </summary>
[Flags]
public enum EncounterGenerationPhases : byte
{
    None = 0,
    Normal = 1 << 0,
    Reinforcements = 1 << 1,
    CriticalPressure = 1 << 2,
    Collapse = 1 << 3,
    All = Normal | Reinforcements | CriticalPressure | Collapse
}

using System;
using System.Collections.Generic;
using Spawning;

/// <summary>Plans one room's initial Encounter: seed, composition (Doc 10 §9.1) and spawn point assignment.</summary>
public static class EncounterBootstrapPlanner
{
    /// <summary>
    /// Plans the room composition with a deterministic per-room random (session seed + scene load generation +
    /// EncounterId) and spatial capacity = spawn point count. Assignments are filled only for <see cref="EncounterPlanResult.Planned"/>.
    /// </summary>
    public static EncounterPlanResult Plan<T>(
        IReadOnlyList<EnemySpawnCandidate<T>> candidates,
        string environment,
        EncounterCompositionSpec spec,
        int spawnPointCount,
        ulong sessionSeed,
        int sceneLoadGeneration,
        EncounterId encounterId,
        List<EncounterSpawnAssignment<T>> assignments)
    {
        if (spec == null) throw new ArgumentNullException(nameof(spec));
        if (assignments == null) throw new ArgumentNullException(nameof(assignments));

        assignments.Clear();
        Random random = EncounterSeedRules.CreateRandom(sessionSeed, sceneLoadGeneration, encounterId);
        var composition = new List<EnemySpawnCandidate<T>>();
        EncounterPlanResult result = EncounterCompositionPlanner.Plan(candidates, environment, spec, spawnPointCount, random, composition);
        if (result != EncounterPlanResult.Planned)
        {
            return result;
        }

        return EncounterSpawnAssigner.TryAssign(composition, spawnPointCount, random, assignments)
            ? EncounterPlanResult.Planned
            : EncounterPlanResult.Invalid;
    }
}

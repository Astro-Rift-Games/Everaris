using System;
using System.Collections.Generic;
using Spawning;

/// <summary>Plans one room's initial Encounter: seed, composition (Doc 10 §9.1) and spawn point assignment.</summary>
public static class EncounterBootstrapPlanner
{
    /// <summary>
    /// Plans the room composition with a deterministic per-room random (session seed + scene load generation +
    /// EncounterId, plus <paramref name="cycle"/>) and spatial capacity = spawn point count. Assignments are filled only for
    /// <see cref="EncounterPlanResult.Planned"/>. <paramref name="cycle"/> is the room's ReactivationCount for the cycle being
    /// spawned: 0 is the initial activation (bootstrap), every reactivation draws its own composition.
    /// </summary>
    public static EncounterPlanResult Plan<T>(
        IReadOnlyList<EnemySpawnCandidate<T>> candidates,
        string environment,
        EncounterCompositionSpec spec,
        int spawnPointCount,
        ulong sessionSeed,
        int sceneLoadGeneration,
        EncounterId encounterId,
        List<EncounterSpawnAssignment<T>> assignments,
        int cycle = 0)
    {
        if (spec == null) throw new ArgumentNullException(nameof(spec));
        if (assignments == null) throw new ArgumentNullException(nameof(assignments));

        assignments.Clear();
        Random random = EncounterSeedRules.CreateRandom(sessionSeed, sceneLoadGeneration, encounterId, cycle);
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

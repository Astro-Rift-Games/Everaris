using System;
using System.Collections.Generic;
using Spawning;

/// <summary>
/// Builds a room Encounter composition (Doc 10 §9.1): initial weighted validation, AllowedEnemies, profile weights,
/// local Threat Budget, MinEnemies/MaxEnemies and spatial validation against the room's spawn capacity.
/// </summary>
public static class EncounterCompositionPlanner
{
    public static EncounterPlanResult Plan<T>(
        IReadOnlyList<EnemySpawnCandidate<T>> candidates,
        string environment,
        EncounterCompositionSpec spec,
        int spatialCapacity,
        Random random,
        List<EnemySpawnCandidate<T>> result)
    {
        if (spec == null) throw new ArgumentNullException(nameof(spec));
        if (random == null) throw new ArgumentNullException(nameof(random));
        if (result == null) throw new ArgumentNullException(nameof(result));

        result.Clear();

        if (random.NextDouble() >= spec.EncounterChance)
        {
            return EncounterPlanResult.NoEncounter;
        }

        int maxCount = Math.Min(spec.MaxEnemies, Math.Max(0, spatialCapacity));
        if (spec.MinEnemies < 1 || spec.MinEnemies > maxCount || candidates == null)
        {
            return EncounterPlanResult.Invalid;
        }

        List<EnemySpawnCandidate<T>> eligible = BuildEligibleCandidates(candidates, spec);
        int threatBudget = spec.ThreatBudget > 0 ? spec.ThreatBudget : int.MaxValue;
        List<EnemySpawnCandidate<T>> composition = WeightedEnemySelector.GenerateEncounter(eligible, environment, threatBudget, maxCount, random);
        if (composition.Count < spec.MinEnemies)
        {
            return EncounterPlanResult.Invalid;
        }

        result.AddRange(composition);
        return EncounterPlanResult.Planned;
    }

    private static List<EnemySpawnCandidate<T>> BuildEligibleCandidates<T>(IReadOnlyList<EnemySpawnCandidate<T>> candidates, EncounterCompositionSpec spec)
    {
        var eligible = new List<EnemySpawnCandidate<T>>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
        {
            EnemySpawnCandidate<T> candidate = candidates[i];
            if (!spec.IsAllowed(candidate.EnemyId))
            {
                continue;
            }

            int weight = spec.Profile != null ? spec.Profile.ApplyWeight(candidate.EnemyId, candidate.Weight) : candidate.Weight;
            if (weight <= 0)
            {
                continue;
            }

            candidate.Weight = weight;
            eligible.Add(candidate);
        }

        return eligible;
    }
}

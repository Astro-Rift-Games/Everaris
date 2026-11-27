using System;
using System.Collections.Generic;
using Spawning;

/// <summary>Assigns a planned composition to distinct room spawn points with stable member keys.</summary>
public static class EncounterSpawnAssigner
{
    /// <summary>
    /// Binds each planned enemy to a distinct spawn point index in [0, spawnPointCount). Member keys are 1-based plan
    /// positions (0 means "no member"), unique within the Encounter cycle. Returns false, leaving the result empty,
    /// when the plan needs more points than the room has.
    /// </summary>
    public static bool TryAssign<T>(
        IReadOnlyList<EnemySpawnCandidate<T>> planned,
        int spawnPointCount,
        Random random,
        List<EncounterSpawnAssignment<T>> result)
    {
        if (planned == null) throw new ArgumentNullException(nameof(planned));
        if (random == null) throw new ArgumentNullException(nameof(random));
        if (result == null) throw new ArgumentNullException(nameof(result));

        result.Clear();
        if (spawnPointCount < 0 || planned.Count > spawnPointCount)
        {
            return false;
        }

        int[] pointIndices = new int[spawnPointCount];
        for (int i = 0; i < pointIndices.Length; i++)
        {
            pointIndices[i] = i;
        }

        for (int i = 0; i < planned.Count; i++)
        {
            int pick = i + random.Next(spawnPointCount - i);
            (pointIndices[i], pointIndices[pick]) = (pointIndices[pick], pointIndices[i]);
            result.Add(new EncounterSpawnAssignment<T>(planned[i], pointIndices[i], (uint)(i + 1)));
        }

        return true;
    }
}

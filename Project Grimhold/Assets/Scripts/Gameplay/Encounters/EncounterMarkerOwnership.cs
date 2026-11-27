using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Rules for markers owned by Encounter rooms versus the global Enemies group (hybrid, TASK-330 L9).</summary>
public static class EncounterMarkerOwnership
{
    /// <summary>Returns the group's points not owned by any room. Returns the same array when nothing is owned.</summary>
    public static Transform[] ExcludeOwned(Transform[] groupPoints, IReadOnlyList<EncounterRoom> rooms)
    {
        if (groupPoints == null || rooms == null || rooms.Count == 0)
        {
            return groupPoints;
        }

        var owned = new HashSet<Transform>();
        for (int i = 0; i < rooms.Count; i++)
        {
            EncounterRoom room = rooms[i];
            if (room == null)
            {
                continue;
            }

            IReadOnlyList<Transform> points = room.SpawnPoints;
            for (int j = 0; j < points.Count; j++)
            {
                if (points[j] != null)
                {
                    owned.Add(points[j]);
                }
            }
        }

        var remaining = new List<Transform>(groupPoints.Length);
        foreach (Transform point in groupPoints)
        {
            if (point == null || !owned.Contains(point))
            {
                remaining.Add(point);
            }
        }

        return remaining.Count == groupPoints.Length ? groupPoints : remaining.ToArray();
    }

    /// <summary>
    /// Amount the global group may spawn: unchanged unless rooms removed markers from it, in which case it is capped
    /// by the remaining markers so authored rooms never fail the raid bootstrap.
    /// </summary>
    public static int EffectiveGroupAmount(int amount, int totalPoints, int remainingPoints)
    {
        return remainingPoints >= totalPoints ? amount : Math.Min(amount, Math.Max(0, remainingPoints));
    }
}

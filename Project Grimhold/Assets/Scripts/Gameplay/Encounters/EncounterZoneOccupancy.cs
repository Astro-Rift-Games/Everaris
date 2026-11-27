using System.Collections.Generic;
using UnityEngine;

/// <summary>The "no players inside the generation zone" condition of Doc 10 §9.2.</summary>
public static class EncounterZoneOccupancy
{
    /// <summary>
    /// True when any present (living or downed, still raiding) player stands inside <paramref name="zone"/>. Dead avatars and participants that left the Raid do not
    /// count: they cannot be attacked, so they do not need protection from a respawn. Allocation free; a null or disabled zone is never occupied.
    /// </summary>
    public static bool HasPlayerInside(Collider2D zone, List<EncounterPlayerPresence> players)
    {
        if (zone == null || players == null || !zone.enabled)
        {
            return false;
        }

        for (int i = 0; i < players.Count; i++)
        {
            EncounterPlayerPresence player = players[i];
            if (player.IsPresent && zone.OverlapPoint(player.Position))
            {
                return true;
            }
        }

        return false;
    }
}

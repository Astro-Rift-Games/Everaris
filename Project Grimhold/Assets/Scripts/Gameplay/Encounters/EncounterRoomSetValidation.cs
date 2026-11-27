using System.Collections.Generic;
using UnityEngine;

/// <summary>Validates the set of Encounter rooms referenced by one scene configuration.</summary>
public static class EncounterRoomSetValidation
{
    /// <summary>Rejects more rooms than the replicated capacity, null or invalid rooms, duplicate EncounterIds and a marker owned by two rooms.</summary>
    public static bool Validate(IReadOnlyList<EncounterRoom> rooms, out string failure)
    {
        failure = string.Empty;
        if (rooms == null || rooms.Count == 0)
        {
            return true;
        }

        if (rooms.Count > EncounterLimits.MaxRooms)
        {
            failure = $"{rooms.Count} encounter rooms exceed the replicated capacity of {EncounterLimits.MaxRooms}.";
            return false;
        }

        var roomsById = new Dictionary<int, EncounterRoom>();
        var ownerByMarker = new Dictionary<Transform, EncounterRoom>();
        for (int i = 0; i < rooms.Count; i++)
        {
            EncounterRoom room = rooms[i];
            if (room == null)
            {
                failure = $"Encounter room at index {i} is null.";
                return false;
            }

            if (!room.Validate(out failure))
            {
                return false;
            }

            if (roomsById.TryGetValue(room.Id.Value, out EncounterRoom sameId))
            {
                failure = $"Duplicate EncounterId {room.Id.Value} on encounter rooms '{sameId.name}' and '{room.name}'.";
                return false;
            }

            roomsById.Add(room.Id.Value, room);
            foreach (Transform marker in room.SpawnPoints)
            {
                if (ownerByMarker.TryGetValue(marker, out EncounterRoom owner))
                {
                    failure = $"Spawn marker '{marker.name}' is owned by two encounter rooms: '{owner.name}' and '{room.name}'.";
                    return false;
                }

                ownerByMarker.Add(marker, room);
            }
        }

        return true;
    }
}

using UnityEngine;

/// <summary>A player avatar as the generation-zone check sees it: where it is and whether it still counts as a player of the Raid.</summary>
public readonly struct EncounterPlayerPresence
{
    public EncounterPlayerPresence(Vector2 position, bool isPresent)
    {
        Position = position;
        IsPresent = isPresent;
    }

    public Vector2 Position { get; }

    /// <summary>
    /// False for a dead avatar and for one whose participant already left the Raid (Extracted, Defeated, Aborted). A downed player is
    /// still alive and in the Raid, so it counts as present.
    /// </summary>
    public bool IsPresent { get; }
}

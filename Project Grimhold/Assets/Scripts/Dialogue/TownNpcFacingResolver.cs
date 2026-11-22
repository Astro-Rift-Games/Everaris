using UnityEngine;

/// <summary>
/// Chooses which of the six visual buckets a Town NPC shows while it looks at someone.
/// Town NPCs have no side-on pose, so the generic resolver's even split turns their back on anyone standing
/// level with them and a hair above. This one prefers the front-facing poses and keeps the pose already shown
/// near a limit, so small movements never flip the NPC back and forth.
/// </summary>
public static class TownNpcFacingResolver
{
    private const float MinimumDirectionSqrMagnitude = 0.0001f;

    // Degrees above the NPC's horizontal. Someone has to be clearly behind before the NPC turns its back,
    // and clearly back beside it before it faces front again.
    private const float TurnBackAboveElevation = 35f;
    private const float FaceFrontBelowElevation = 20f;

    // Degrees away from straight ahead or straight behind.
    private const float CentreWithinAngle = 15f;
    private const float DiagonalBeyondAngle = 30f;

    /// <summary>
    /// Resolves the facing for a target at <paramref name="direction"/> from the NPC, given the facing it
    /// currently shows. A missing or degenerate direction keeps the current facing.
    /// </summary>
    public static CharacterVisualDirection Resolve(Vector2 direction, CharacterVisualDirection current)
    {
        if (float.IsNaN(direction.x) || float.IsNaN(direction.y) ||
            float.IsInfinity(direction.x) || float.IsInfinity(direction.y) ||
            direction.sqrMagnitude < MinimumDirectionSqrMagnitude)
        {
            return current;
        }

        float elevation = Mathf.Atan2(direction.y, Mathf.Abs(direction.x)) * Mathf.Rad2Deg;
        float offCentre = 90f - Mathf.Abs(elevation);

        bool showsBack = !CharacterVisualDirectionResolver.IsFrontFacing(current);
        bool back = elevation > (showsBack ? FaceFrontBelowElevation : TurnBackAboveElevation);

        bool showsCentre = current == CharacterVisualDirection.South || current == CharacterVisualDirection.North;
        bool centre = showsCentre ? offCentre <= DiagonalBeyondAngle : offCentre < CentreWithinAngle;

        if (centre)
        {
            return back ? CharacterVisualDirection.North : CharacterVisualDirection.South;
        }

        if (direction.x >= 0f)
        {
            return back ? CharacterVisualDirection.NorthEast : CharacterVisualDirection.SouthEast;
        }

        return back ? CharacterVisualDirection.NorthWest : CharacterVisualDirection.SouthWest;
    }
}

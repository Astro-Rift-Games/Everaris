using UnityEngine;

/// <summary>
/// Identifies the six discrete visual direction buckets supported by the character
/// and weapon presentation systems.
/// </summary>
public enum CharacterVisualDirection
{
    South,
    SouthEast,
    NorthEast,
    North,
    NorthWest,
    SouthWest
}

/// <summary>
/// Pure static resolver providing unified conversion between continuous facing vectors
/// and discrete visual character direction buckets.
/// </summary>
public static class CharacterVisualDirectionResolver
{
    private const float MinimumDirectionSqrMagnitude = 0.0001f;

    public static readonly Vector2 CanonicalSouth = new Vector2(0f, -1f);
    public static readonly Vector2 CanonicalSouthEast = new Vector2(0.70710678f, -0.70710678f);
    public static readonly Vector2 CanonicalNorthEast = new Vector2(0.70710678f, 0.70710678f);
    public static readonly Vector2 CanonicalNorth = new Vector2(0f, 1f);
    public static readonly Vector2 CanonicalNorthWest = new Vector2(-0.70710678f, 0.70710678f);
    public static readonly Vector2 CanonicalSouthWest = new Vector2(-0.70710678f, -0.70710678f);

    /// <summary>
    /// Sanitizes an incoming facing vector against zero, NaN, or infinite components.
    /// Falls back to the provided fallback vector or <see cref="Vector2.down"/> if invalid.
    /// </summary>
    public static Vector2 SanitizeFacing(Vector2 facing, Vector2 fallback = default)
    {
        if (IsFiniteNonZero(facing))
        {
            return facing.normalized;
        }

        if (IsFiniteNonZero(fallback))
        {
            return fallback.normalized;
        }

        return Vector2.down;
    }

    /// <summary>
    /// Resolves the continuous facing vector into one of the six visual buckets
    /// using deterministic sector boundaries with frontal preference on ties.
    /// </summary>
    public static CharacterVisualDirection Resolve(Vector2 facing, Vector2 fallback = default)
    {
        Vector2 safe = SanitizeFacing(facing, fallback);
        float angle = Mathf.Atan2(safe.y, safe.x) * Mathf.Rad2Deg;

        if (angle > 67.5f && angle < 112.5f)
        {
            return CharacterVisualDirection.North;
        }

        if (angle >= 112.5f && angle < 180f)
        {
            return CharacterVisualDirection.NorthWest;
        }

        if (angle > 0f && angle <= 67.5f)
        {
            return CharacterVisualDirection.NorthEast;
        }

        if (angle <= 0f && angle >= -67.5f)
        {
            return CharacterVisualDirection.SouthEast;
        }

        if (angle < -67.5f && angle > -112.5f)
        {
            return CharacterVisualDirection.South;
        }

        return CharacterVisualDirection.SouthWest;
    }

    /// <summary>
    /// Resolves the continuous facing vector into one of the six visual buckets while applying
    /// an angular hysteresis margin around the previous bucket's boundaries to prevent
    /// frame-to-frame sprite flicker during diagonal locomotion (TASK-317).
    /// </summary>
    public static CharacterVisualDirection ResolveWithHysteresis(
        Vector2 facing,
        CharacterVisualDirection previousDirection,
        float hysteresisDegrees,
        Vector2 fallback = default)
    {
        if (hysteresisDegrees <= 0f)
        {
            return Resolve(facing, fallback);
        }

        Vector2 safe = SanitizeFacing(facing, fallback);
        float angle = Mathf.Atan2(safe.y, safe.x) * Mathf.Rad2Deg;
        float margin = Mathf.Clamp(hysteresisDegrees, 0f, 20f);

        if (IsWithinExpandedSector(angle, previousDirection, margin))
        {
            return previousDirection;
        }

        return Resolve(safe, fallback);
    }

    private static bool IsWithinExpandedSector(float angle, CharacterVisualDirection sector, float margin)
    {
        switch (sector)
        {
            case CharacterVisualDirection.North:
                return angle >= 67.5f - margin && angle <= 112.5f + margin;
            case CharacterVisualDirection.NorthWest:
                return (angle >= 112.5f - margin && angle <= 180f)
                    || (angle >= -180f && angle <= -180f + margin);
            case CharacterVisualDirection.NorthEast:
                return angle >= -margin && angle <= 67.5f + margin;
            case CharacterVisualDirection.SouthEast:
                return angle >= -67.5f - margin && angle <= margin;
            case CharacterVisualDirection.South:
                return angle >= -112.5f - margin && angle <= -67.5f + margin;
            case CharacterVisualDirection.SouthWest:
                return (angle >= -180f && angle <= -112.5f + margin)
                    || (angle >= 180f - margin && angle <= 180f);
            default:
                return false;
        }
    }

    /// <summary>
    /// Returns the normalized canonical vector representing the center of the specified visual bucket.
    /// </summary>
    public static Vector2 GetCanonicalVector(CharacterVisualDirection direction)
    {
        switch (direction)
        {
            case CharacterVisualDirection.South:
                return CanonicalSouth;
            case CharacterVisualDirection.SouthEast:
                return CanonicalSouthEast;
            case CharacterVisualDirection.NorthEast:
                return CanonicalNorthEast;
            case CharacterVisualDirection.North:
                return CanonicalNorth;
            case CharacterVisualDirection.NorthWest:
                return CanonicalNorthWest;
            case CharacterVisualDirection.SouthWest:
                return CanonicalSouthWest;
            default:
                return CanonicalSouth;
        }
    }

    /// <summary>
    /// Returns true if the visual direction represents a front-facing posture (South, SouthEast, SouthWest).
    /// </summary>
    public static bool IsFrontFacing(CharacterVisualDirection direction)
    {
        return direction == CharacterVisualDirection.South
            || direction == CharacterVisualDirection.SouthEast
            || direction == CharacterVisualDirection.SouthWest;
    }

    /// <summary>
    /// Computes the weapon sorting order based on whether the bucket is front-facing or back-facing.
    /// </summary>
    public static int CalculateSortingOrder(CharacterVisualDirection direction, int frontOrder, int backOrder)
    {
        return IsFrontFacing(direction) ? frontOrder : backOrder;
    }

    private static bool IsFiniteNonZero(Vector2 vector)
    {
        return !float.IsNaN(vector.x)
            && !float.IsNaN(vector.y)
            && !float.IsInfinity(vector.x)
            && !float.IsInfinity(vector.y)
            && vector.sqrMagnitude >= MinimumDirectionSqrMagnitude;
    }
}

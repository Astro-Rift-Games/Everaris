using UnityEngine;

/// <summary>
/// Pure deterministic Leash and Home Area rules for raid enemies (Doc 10 §10.1, TASK-314).
/// Prevents indefinite kiting across the Dungeon and avoids border oscillation during return.
/// </summary>
public static class EnemyLeashRules
{
    /// <summary>
    /// Returns true when <paramref name="currentPosition"/> has exceeded <paramref name="leashRadius"/>
    /// from <paramref name="homePosition"/>. A non-positive radius disables the leash.
    /// </summary>
    public static bool IsBeyondLeash(Vector2 currentPosition, Vector2 homePosition, float leashRadius)
    {
        if (leashRadius <= 0f)
        {
            return false;
        }

        return Vector2.SqrMagnitude(currentPosition - homePosition) > leashRadius * leashRadius;
    }

    /// <summary>
    /// Returns true when <paramref name="currentPosition"/> is within <paramref name="arrivalRadius"/>
    /// of <paramref name="homePosition"/>.
    /// </summary>
    public static bool HasArrivedAtHome(Vector2 currentPosition, Vector2 homePosition, float arrivalRadius)
    {
        float safeRadius = Mathf.Max(0.05f, arrivalRadius);
        return Vector2.SqrMagnitude(currentPosition - homePosition) <= safeRadius * safeRadius;
    }

    /// <summary>
    /// Returns true when the enemy is returning to its Home Area and should NOT re-engage
    /// <paramref name="candidateId"/> yet (either because the enemy is still outside its leash,
    /// or because the leashed target's anti-oscillation lockout is still active).
    /// </summary>
    public static bool ShouldBlockReengage(
        EntityId candidateId,
        EntityId leashedTargetId,
        bool isReturningHome,
        bool isLockoutTimerActive,
        Vector2 currentPosition,
        Vector2 homePosition,
        float leashRadius,
        float innerReengageFactor)
    {
        if (!isReturningHome)
        {
            return false;
        }

        // Never re-engage while still physically outside the leash radius (would immediately re-leash).
        if (IsBeyondLeash(currentPosition, homePosition, leashRadius))
        {
            return true;
        }

        // For the player that triggered the leash, block re-detection until arrival at Home Area
        // or until the configurable lockout expires AND the enemy has retreated inside the inner radius.
        if (leashedTargetId.Value != 0 && candidateId == leashedTargetId)
        {
            if (isLockoutTimerActive)
            {
                return true;
            }

            if (leashRadius > 0f)
            {
                float innerRadius = leashRadius * Mathf.Clamp(innerReengageFactor, 0.1f, 1f);
                if (Vector2.SqrMagnitude(currentPosition - homePosition) > innerRadius * innerRadius)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

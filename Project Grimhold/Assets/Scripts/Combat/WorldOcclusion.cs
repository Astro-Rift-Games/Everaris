using UnityEngine;

/// <summary>
/// Walls, closed doors, railings and pillars live on the WorldCollision layer. Nothing that deals damage or
/// applies an effect may reach a target through them: melee, ability areas, explosions, traps and orb contacts
/// test the line from their source to the target, and thrown objects stop at the first obstacle they cross.
/// </summary>
public static class WorldOcclusion
{
    private const int WorldCollisionLayer = 6;

    /// <summary>Backoff from the obstacle so a clamped landing stays on the near side of it.</summary>
    private const float LandingBackoff = 0.15f;

    public const int Mask = 1 << WorldCollisionLayer;

    /// <summary>True when a wall or closed door crosses the segment between two points.</summary>
    public static bool IsBlocked(Vector2 from, Vector2 to) =>
        (to - from).sqrMagnitude > 0f && Physics2D.Linecast(from, to, Mask).collider != null;

    /// <summary>
    /// Returns <paramref name="to"/> when the way is clear, otherwise a point just before the first wall or
    /// closed door on the segment, so a thrown object lands against it instead of passing through.
    /// </summary>
    public static Vector2 ClampToFirstObstacle(Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        float distance = delta.magnitude;
        if (distance <= 0f) return to;

        RaycastHit2D hit = Physics2D.Linecast(from, to, Mask);
        if (hit.collider == null) return to;

        Vector2 direction = delta / distance;
        return from + direction * Mathf.Max(0f, hit.distance - LandingBackoff);
    }
}

using UnityEngine;

/// <summary>
/// Pure 2D math for the local gameplay camera: cursor-driven look-ahead offset
/// and map-bounds clamping. Presentation only; never used by network simulation.
/// </summary>
public static class GameplayCameraMath
{
    private const float MaxDeadZone = 0.99f;

    /// <summary>Half width and half height of an orthographic view in world units.</summary>
    public static Vector2 OrthographicHalfExtents(float orthographicSize, float aspect)
    {
        return new Vector2(orthographicSize * aspect, orthographicSize);
    }

    /// <summary>
    /// Offset from the followed target toward the cursor.
    /// The cursor is measured from the screen center in viewport space, so the result does not
    /// depend on the camera's current position and cannot feed back into itself.
    /// The cursor is normalized per axis so the screen edge is 1; the magnitude is clamped to 1,
    /// a central dead zone (<paramref name="deadZone"/>, fraction of that normalized radius) maps to zero
    /// and the rest ramps linearly up to <paramref name="maxDistance"/> at the screen edge.
    /// The result never exceeds <paramref name="maxDistance"/> and, per axis, never moves the
    /// target closer to the screen edge than <paramref name="safeMargin"/> (fraction of the half extent).
    /// </summary>
    public static Vector2 CursorOffset(Vector2 cursorViewport, Vector2 viewHalfExtents, float maxDistance, float safeMargin, float deadZone)
    {
        if (maxDistance <= 0f)
        {
            return Vector2.zero;
        }

        Vector2 normalized = Vector2.ClampMagnitude((cursorViewport - new Vector2(0.5f, 0.5f)) * 2f, 1f);
        float radius = normalized.magnitude;
        float innerRadius = Mathf.Clamp(deadZone, 0f, MaxDeadZone);

        if (radius <= innerRadius)
        {
            return Vector2.zero;
        }

        float remapped = (radius - innerRadius) / (1f - innerRadius);
        Vector2 offset = normalized / radius * (remapped * maxDistance);

        float visibleFraction = 1f - Mathf.Clamp01(safeMargin);
        float maxX = viewHalfExtents.x * visibleFraction;
        float maxY = viewHalfExtents.y * visibleFraction;

        return new Vector2(
            Mathf.Clamp(offset.x, -maxX, maxX),
            Mathf.Clamp(offset.y, -maxY, maxY));
    }

    /// <summary>
    /// Clamps a camera center so the view never shows outside <paramref name="bounds"/>.
    /// On any axis where the map is not larger than the view, the camera centers on the map.
    /// </summary>
    public static Vector2 ClampToBounds(Vector2 cameraCenter, Vector2 viewHalfExtents, Rect bounds)
    {
        return new Vector2(
            ClampAxis(cameraCenter.x, viewHalfExtents.x, bounds.xMin, bounds.xMax),
            ClampAxis(cameraCenter.y, viewHalfExtents.y, bounds.yMin, bounds.yMax));
    }

    private static float ClampAxis(float center, float halfExtent, float min, float max)
    {
        float low = min + halfExtent;
        float high = max - halfExtent;

        return low >= high ? (min + max) * 0.5f : Mathf.Clamp(center, low, high);
    }
}

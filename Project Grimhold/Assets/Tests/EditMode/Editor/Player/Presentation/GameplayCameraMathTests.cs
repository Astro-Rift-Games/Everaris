using NUnit.Framework;
using UnityEngine;

public sealed class GameplayCameraMathTests
{
    private const float Tolerance = 0.0001f;
    private static readonly Vector2 HalfExtents = new(16f, 9f);

    [Test]
    public void OrthographicHalfExtents_UsesSizeVerticallyAndAspectHorizontally()
    {
        Vector2 halfExtents = GameplayCameraMath.OrthographicHalfExtents(10f, 16f / 9f);

        Assert.That(halfExtents.x, Is.EqualTo(10f * 16f / 9f).Within(Tolerance));
        Assert.That(halfExtents.y, Is.EqualTo(10f).Within(Tolerance));
    }

    [Test]
    public void CursorOffset_CursorAtScreenCenter_IsZero()
    {
        Vector2 offset = GameplayCameraMath.CursorOffset(new Vector2(0.5f, 0.5f), HalfExtents, 5f, 0.2f, 0.1f);

        Assert.That(offset, Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void CursorOffset_InsideDeadZone_IsZero()
    {
        // 0.55 viewport is 0.1 of the normalized screen radius; the dead zone covers 0.2.
        Vector2 offset = GameplayCameraMath.CursorOffset(new Vector2(0.55f, 0.52f), HalfExtents, 5f, 0f, 0.2f);

        Assert.That(offset, Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void CursorOffset_CursorAtHorizontalScreenEdge_IsMaxDistance()
    {
        Vector2 offset = GameplayCameraMath.CursorOffset(new Vector2(1f, 0.5f), HalfExtents, 5f, 0f, 0.2f);

        Assert.That(offset.x, Is.EqualTo(5f).Within(Tolerance));
        Assert.That(offset.y, Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void CursorOffset_HalfwayBetweenDeadZoneEdgeAndScreenEdge_IsHalfOfMax()
    {
        // Normalized radius 0.6 sits halfway between the dead zone edge (0.2) and the screen edge (1).
        Vector2 offset = GameplayCameraMath.CursorOffset(new Vector2(0.8f, 0.5f), HalfExtents, 5f, 0f, 0.2f);

        Assert.That(offset.x, Is.EqualTo(2.5f).Within(Tolerance));
        Assert.That(offset.y, Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void CursorOffset_NoDeadZone_RampsProportionallyAcrossTheScreen()
    {
        Vector2 offset = GameplayCameraMath.CursorOffset(new Vector2(0.25f, 0.5f), HalfExtents, 6f, 0f, 0f);

        Assert.That(offset.x, Is.EqualTo(-3f).Within(Tolerance));
    }

    [Test]
    public void CursorOffset_CornerOfTheScreen_ClampsMagnitudeAndKeepsDirection()
    {
        Vector2 offset = GameplayCameraMath.CursorOffset(new Vector2(1f, 1f), HalfExtents, 4f, 0f, 0.1f);

        Assert.That(offset.magnitude, Is.EqualTo(4f).Within(Tolerance));
        Assert.That(Vector2.Angle(offset, Vector2.one), Is.LessThan(0.01f));
    }

    [Test]
    public void CursorOffset_CursorOutsideViewport_NeverExceedsMaxDistance()
    {
        Vector2 offset = GameplayCameraMath.CursorOffset(new Vector2(-3f, 7f), HalfExtents, 2.5f, 0f, 0.1f);

        Assert.That(offset.magnitude, Is.LessThanOrEqualTo(2.5f + Tolerance));
    }

    [Test]
    public void CursorOffset_NonPositiveMaxDistance_IsZero()
    {
        Assert.That(GameplayCameraMath.CursorOffset(new Vector2(1f, 1f), HalfExtents, 0f, 0f, 0.1f), Is.EqualTo(Vector2.zero));
        Assert.That(GameplayCameraMath.CursorOffset(new Vector2(1f, 1f), HalfExtents, -1f, 0f, 0.1f), Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void CursorOffset_DeadZoneCoveringTheWholeScreen_StaysFiniteAndWithinMax()
    {
        Vector2 offset = GameplayCameraMath.CursorOffset(new Vector2(1f, 0.5f), HalfExtents, 5f, 0f, 1f);

        Assert.That(float.IsNaN(offset.x) || float.IsNaN(offset.y), Is.False);
        Assert.That(offset.magnitude, Is.LessThanOrEqualTo(5f + Tolerance));
    }

    [Test]
    public void CursorOffset_SafeMargin_KeepsTargetInsideSafeArea()
    {
        // Max distance would allow 20 units vertically; a 25% margin caps it at 9 * 0.75.
        Vector2 offset = GameplayCameraMath.CursorOffset(new Vector2(0.5f, 1f), new Vector2(8f, 9f), 20f, 0.25f, 0f);

        Assert.That(offset.x, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(offset.y, Is.EqualTo(6.75f).Within(Tolerance));
    }

    [Test]
    public void CursorOffset_FullSafeMargin_IsZero()
    {
        Vector2 offset = GameplayCameraMath.CursorOffset(new Vector2(1f, 0f), HalfExtents, 20f, 1f, 0.1f);

        Assert.That(offset, Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void ClampToBounds_ViewInsideBounds_IsUnchanged()
    {
        Rect bounds = new(0f, 0f, 100f, 60f);

        Vector2 clamped = GameplayCameraMath.ClampToBounds(new Vector2(50f, 30f), HalfExtents, bounds);

        Assert.That(clamped, Is.EqualTo(new Vector2(50f, 30f)));
    }

    [Test]
    public void ClampToBounds_NearMinEdges_KeepsViewInsideMap()
    {
        Rect bounds = new(0f, 0f, 100f, 60f);

        Vector2 clamped = GameplayCameraMath.ClampToBounds(new Vector2(2f, -5f), HalfExtents, bounds);

        Assert.That(clamped, Is.EqualTo(new Vector2(16f, 9f)));
    }

    [Test]
    public void ClampToBounds_NearMaxEdges_KeepsViewInsideMap()
    {
        Rect bounds = new(-10f, -10f, 100f, 60f);

        Vector2 clamped = GameplayCameraMath.ClampToBounds(new Vector2(200f, 49f), HalfExtents, bounds);

        Assert.That(clamped, Is.EqualTo(new Vector2(74f, 41f)));
    }

    [Test]
    public void ClampToBounds_MapNarrowerThanView_CentersOnThatAxisOnly()
    {
        Rect bounds = new(0f, 0f, 20f, 60f);

        Vector2 clamped = GameplayCameraMath.ClampToBounds(new Vector2(3f, 1f), HalfExtents, bounds);

        Assert.That(clamped, Is.EqualTo(new Vector2(10f, 9f)));
    }

    [Test]
    public void ClampToBounds_MapSmallerThanViewOnBothAxes_CentersOnMap()
    {
        Rect bounds = new(4f, 6f, 10f, 8f);

        Vector2 clamped = GameplayCameraMath.ClampToBounds(new Vector2(-40f, 70f), HalfExtents, bounds);

        Assert.That(clamped, Is.EqualTo(bounds.center));
    }

    [Test]
    public void ClampToBounds_MapExactlyViewSize_CentersOnMap()
    {
        Rect bounds = new(0f, 0f, 32f, 18f);

        Vector2 clamped = GameplayCameraMath.ClampToBounds(new Vector2(30f, 1f), HalfExtents, bounds);

        Assert.That(clamped, Is.EqualTo(bounds.center));
    }

    [Test]
    public void OffsetThenBounds_PlayerInsideMap_StaysInsideView()
    {
        Rect bounds = new(0f, 0f, 120f, 70f);
        Vector2[] players = { new(0f, 0f), new(120f, 70f), new(1f, 69f), new(60f, 35f), new(119f, 2f) };
        Vector2[] cursors = { new(0f, 0f), new(1f, 1f), new(0f, 1f), new(1f, 0f), new(-2f, 3f), new(0.5f, 0.5f) };

        foreach (Vector2 player in players)
        {
            foreach (Vector2 cursor in cursors)
            {
                Vector2 offset = GameplayCameraMath.CursorOffset(cursor, HalfExtents, 6f, 0.2f, 0.1f);
                Vector2 center = GameplayCameraMath.ClampToBounds(player + offset, HalfExtents, bounds);

                Assert.That(Mathf.Abs(center.x - player.x), Is.LessThanOrEqualTo(HalfExtents.x + Tolerance),
                    $"player {player} cursor {cursor}");
                Assert.That(Mathf.Abs(center.y - player.y), Is.LessThanOrEqualTo(HalfExtents.y + Tolerance),
                    $"player {player} cursor {cursor}");
            }
        }
    }
}

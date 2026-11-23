using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The decorative stairs of CatacombsMap block only their sides (railings, arch pillars, the diagonal
/// stair's solid side and open top edge); their steps stay walkable and the open arches stay passable.
/// Geometry is checked on the authored colliders, without stepping physics.
/// </summary>
public sealed class CatacombsStairCollisionTests
{
    private const string ScenePath = "Assets/Scenes/CatacombsMap.unity";
    private const string RootName = "StairCollision";

    private Scene _scene;
    private Collider2D[] _colliders;

    [OneTimeSetUp]
    public void OpenScene()
    {
        _scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        GameObject root = _scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject)
            .SingleOrDefault(go => go.name == RootName);
        Assert.That(root, Is.Not.Null, $"{ScenePath} needs a '{RootName}' object holding the stair side colliders.");
        _colliders = root.GetComponentsInChildren<Collider2D>(true);
    }

    [OneTimeTearDown]
    public void CloseScene() => EditorSceneManager.ClosePreviewScene(_scene);

    [Test]
    public void StairColliders_AreStaticSolidWorldCollision_UnderTheMapGrid()
    {
        Assert.That(_colliders, Is.Not.Empty);
        GameObject root = _colliders[0].transform.root.GetComponentsInChildren<Transform>(true)
            .Single(t => t.name == RootName).gameObject;
        Assert.That(root.GetComponentInParent<Grid>(), Is.Not.Null, "Stair collision lives with the map tilemaps.");
        Assert.That(root.GetComponentsInChildren<Renderer>(true), Is.Empty, "Collision only, no art.");
        Assert.That(root.GetComponentsInChildren<Rigidbody2D>(true), Is.Empty, "Static colliders.");
        foreach (Collider2D collider in _colliders)
        {
            Assert.That(collider.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("WorldCollision")), collider.name);
            Assert.That(collider.isTrigger, Is.False, collider.name);
            Assert.That(collider.enabled && collider.gameObject.activeInHierarchy, Is.True, collider.name);
        }
    }

    // Railed stairs: x, y = bottom-left cell, w x h cells; railings are the outer half of the edge columns.
    [TestCase(-56, -68, 4, 5)]
    [TestCase(47, -44, 3, 5)]
    [TestCase(17, -37, 4, 9)]
    [TestCase(-25, 24, 4, 6)]
    [TestCase(-74, -49, 3, 7)] // arch pillars act as railings
    public void RailedStair_BlocksBothRailings_AndKeepsEveryStepWalkable(int x, int y, int w, int h)
    {
        for (float row = y + 0.25f; row < y + h; row += 0.5f)
        {
            Assert.That(IsBlocked(new Vector2(x + 0.25f, row)), Is.True, $"left railing at y {row}");
            Assert.That(IsBlocked(new Vector2(x + w - 0.25f, row)), Is.True, $"right railing at y {row}");
            for (float column = x + 0.6f; column <= x + w - 0.6f; column += 0.2f)
                Assert.That(IsBlocked(new Vector2(column, row)), Is.False, $"step at ({column}, {row})");
        }
    }

    [Test]
    public void UnrailedStair_BlocksOnlyTheSidesOfItsRowBelowTheWall()
    {
        // Stair at x 72-74, y -52..-48; walls already flank rows -51..-48, row -52 sticks out of the wall.
        Assert.That(IsBlocked(new Vector2(72.05f, -51.5f)), Is.True);
        Assert.That(IsBlocked(new Vector2(74.95f, -51.5f)), Is.True);
        Assert.That(IsBlocked(new Vector2(72.3f, -51.5f)), Is.False);
        Assert.That(IsBlocked(new Vector2(73.5f, -51.5f)), Is.False);
        Assert.That(IsBlocked(new Vector2(74.7f, -51.5f)), Is.False);
        Assert.That(IsBlocked(new Vector2(73.5f, -49.5f)), Is.False);
    }

    [Test]
    public void DiagonalStair_BlocksItsSolidSideAndOpenTopEdge_AndKeepsTheStepBandWalkable()
    {
        // Step band runs from the wall at (-13, -49..-52) down to the bottom step at x -9 (y -52.75..-55.75).
        Assert.That(IsBlocked(new Vector2(-12.5f, -55f)), Is.True, "solid stair side under the band");
        Assert.That(IsBlocked(new Vector2(-11.5f, -54.6f)), Is.True, "solid stair side under the band");
        Assert.That(IsBlocked(new Vector2(-11f, -52.2f)), Is.False, "middle of the step band");
        Assert.That(IsBlocked(new Vector2(-9.5f, -54f)), Is.False, "bottom of the step band");
        Assert.That(IsBlocked(new Vector2(-12.5f, -50.5f)), Is.False, "top of the step band");

        EdgeCollider2D edge = _colliders.OfType<EdgeCollider2D>().SingleOrDefault();
        Assert.That(edge, Is.Not.Null, "The open top edge of the band is an edge collider.");
        Vector2[] points = edge.points.Select(p => (Vector2)edge.transform.TransformPoint(p + edge.offset)).ToArray();
        Assert.That(points, Has.Length.EqualTo(2));
        Vector2 top = points.OrderBy(p => p.x).First();
        Vector2 bottom = points.OrderBy(p => p.x).Last();
        Assert.That(Vector2.Distance(top, new Vector2(-12.8125f, -49f)), Is.LessThan(0.13f), "edge starts at the wall");
        Assert.That(Vector2.Distance(bottom, new Vector2(-9f, -52.75f)), Is.LessThan(0.13f), "edge ends at the bottom step");
    }

    [TestCase(-79.5f, -66.5f)]
    [TestCase(-26.5f, -63.5f)]
    public void OpenArches_StayPassable(float x, float y)
    {
        Assert.That(IsBlocked(new Vector2(x, y)), Is.False);
    }

    private bool IsBlocked(Vector2 point) => _colliders.Any(collider => Contains(collider, point));

    private static bool Contains(Collider2D collider, Vector2 point)
    {
        Vector2 local = collider.transform.InverseTransformPoint(point);
        switch (collider)
        {
            case BoxCollider2D box:
                Vector2 delta = local - box.offset;
                return Mathf.Abs(delta.x) <= box.size.x * 0.5f && Mathf.Abs(delta.y) <= box.size.y * 0.5f;
            case PolygonCollider2D polygon:
                return InsidePolygon(polygon.points.Select(p => p + polygon.offset).ToArray(), local);
            default:
                return false; // edges block crossings, not points
        }
    }

    private static bool InsidePolygon(Vector2[] polygon, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
                p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                inside = !inside;
        }
        return inside;
    }
}

using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Arches of CatacombsMap Y-sort with characters from the base line of their wall: a character in front
/// of the arch draws over it, a character passing through the arch draws behind its pillars and roof.
/// </summary>
public sealed class CatacombsArchRoofTests
{
    private const string ScenePath = "Assets/Scenes/CatacombsMap.unity";

    public sealed class Arch
    {
        public readonly int Left;
        public readonly int Base;
        public readonly Vector2Int[] Cells;

        public Arch(int left, int baseY, params (int x, int y)[] cells)
        {
            Left = left;
            Base = baseY;
            Cells = cells.Select(c => new Vector2Int(c.x, c.y)).ToArray();
        }

        public string Name => $"Access_ArchRoof_{Left}_{Base}";
        public override string ToString() => Name;
    }

    private static readonly Arch[] Arches =
    {
        new(-81, -68,
            (-81, -68), (-81, -67), (-81, -66), (-81, -65), (-80, -66), (-80, -65),
            (-79, -68), (-79, -67), (-79, -66), (-79, -65)),
        new(-28, -65,
            (-28, -65), (-28, -64), (-28, -63), (-27, -63), (-26, -65), (-26, -64), (-26, -63),
            (-28, -62), (-27, -62), (-26, -62)), // stone cap over the passage (was a wall row)
        new(-74, -49,
            (-74, -44), (-74, -43), (-73, -43), (-72, -44), (-72, -43)),
    };

    private Scene _scene;
    private Tilemap[] _tilemaps;

    [OneTimeSetUp]
    public void OpenScene()
    {
        _scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        _tilemaps = _scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Tilemap>(true)).ToArray();
    }

    [OneTimeTearDown]
    public void CloseScene() => EditorSceneManager.ClosePreviewScene(_scene);

    [TestCaseSource(nameof(Arches))]
    public void ArchRoof_YSortsWithCharactersFromItsWallBase(Arch arch)
    {
        Tilemap roof = _tilemaps.SingleOrDefault(t => t.name == arch.Name);
        Assert.That(roof, Is.Not.Null, $"{arch.Name} tilemap");
        Tilemap walls = _tilemaps.Single(t => t.name == "Walls");
        Assert.That(roof.layoutGrid, Is.SameAs(walls.layoutGrid), "Same grid as the map.");

        var group = roof.GetComponentInParent<SortingGroup>();
        Assert.That(group, Is.Not.Null, "The arch sorts as one unit.");
        Assert.That(group.sortingLayerName, Is.EqualTo("Characters"));
        Assert.That(group.sortingOrder, Is.Zero, "Same order as characters: Y decides.");
        Assert.That(group.sortAtRoot, Is.False);
        Assert.That(roof.transform.position.y, Is.EqualTo(arch.Base).Within(0.0001f), "Tiles stay where they were painted.");
        // Characters sort at their root, which sits above their drawn feet by the offset the doors use.
        Assert.That(group.transform.position.y - arch.Base, Is.EqualTo(DoorSortAnchorOffset()).Within(0.0001f),
            "The sort point is the wall base raised by the character root-to-feet offset.");
        Assert.That(roof.GetComponent<TilemapRenderer>().sortingLayerName, Is.EqualTo("Characters"));
        Assert.That(roof.GetComponent<Collider2D>(), Is.Null, "Sorting only; collision is unchanged.");
        Assert.That(roof.name.ToLowerInvariant(), Does.Contain("access"),
            "Collapse rifts keep treating the arch as a blocking overlay.");
    }

    [TestCaseSource(nameof(Arches))]
    public void CharacterFeetAcrossTheWallBase_SortOnTheCorrectSideOfTheArch(Arch arch)
    {
        SortingGroup group = _tilemaps.Single(t => t.name == arch.Name).GetComponentInParent<SortingGroup>();
        foreach ((float feetFromBase, bool inFront) in new[] { (-0.1f, true), (0.1f, false) })
        {
            float characterRootY = arch.Base + feetFromBase + DoorSortAnchorOffset();
            // Custom Axis (0,1,0): at equal layer and order the lower Y draws in front.
            Assert.That(characterRootY < group.transform.position.y, Is.EqualTo(inFront), $"feet {feetFromBase} from base");
        }
    }

    // Both open arches: pillars are 9 px wide on the outer edge cells through the 4-row wall; the opening stays free.
    [TestCase(-81, -68, 4)]
    [TestCase(-28, -65, 4)]
    public void ArchPillars_BlockMovement_AndTheOpeningStaysPassable(int left, int baseY, int rows)
    {
        Collider2D[] colliders = _scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Where(t => t.name == "ArchCollision")
            .SelectMany(t => t.GetComponentsInChildren<Collider2D>(true))
            .ToArray();
        Assert.That(colliders, Is.Not.Empty, "ArchCollision holds the pillar colliders.");
        foreach (Collider2D collider in colliders)
        {
            Assert.That(collider.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("WorldCollision")));
            Assert.That(collider.isTrigger, Is.False);
        }

        for (float y = baseY + 0.25f; y < baseY + rows; y += 0.5f)
        {
            Assert.That(Blocked(colliders, new Vector2(left + 0.25f, y)), Is.True, $"left pillar at y {y}");
            Assert.That(Blocked(colliders, new Vector2(left + 2.75f, y)), Is.True, $"right pillar at y {y}");
            for (float x = left + 0.6f; x <= left + 2.4f; x += 0.2f)
                Assert.That(Blocked(colliders, new Vector2(x, y)), Is.False, $"opening at ({x}, {y})");
        }
    }

    [TestCaseSource(nameof(Arches))]
    public void ArchRoof_OwnsExactlyTheArchTiles_AndAccessNoLongerDrawsThem(Arch arch)
    {
        Tilemap roof = _tilemaps.Single(t => t.name == arch.Name);
        Tilemap access = _tilemaps.Single(t => t.name == "Access");
        Grid grid = (Grid)roof.layoutGrid;

        Assert.That(roof.GetUsedTilesCount(), Is.GreaterThan(0));
        int painted = 0;
        foreach (Vector3Int local in roof.cellBounds.allPositionsWithin)
        {
            if (!roof.HasTile(local))
                continue;
            painted++;
            Vector3Int world = grid.WorldToCell(roof.GetCellCenterWorld(local));
            Assert.That(arch.Cells, Has.Member(new Vector2Int(world.x, world.y)), $"unexpected roof tile at {world}");
        }

        Assert.That(painted, Is.EqualTo(arch.Cells.Length));
        foreach (Vector2Int cell in arch.Cells)
        {
            var world = new Vector3Int(cell.x, cell.y, 0);
            Assert.That(access.HasTile(world), Is.False, $"Access still draws {world}");
            Vector3Int local = roof.WorldToCell(grid.GetCellCenterWorld(world));
            Assert.That(roof.HasTile(local), Is.True, $"roof misses {world}");
        }
    }

    private static float DoorSortAnchorOffset()
    {
        var door = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Doors/DungeonDoor.prefab");
        return door.transform.Find("SortAnchor").localPosition.y;
    }

    private static bool Blocked(Collider2D[] colliders, Vector2 point)
    {
        foreach (Collider2D collider in colliders)
        {
            if (collider is not BoxCollider2D box)
                continue;
            Vector2 local = (Vector2)box.transform.InverseTransformPoint(point) - box.offset;
            if (Mathf.Abs(local.x) <= box.size.x * 0.5f && Mathf.Abs(local.y) <= box.size.y * 0.5f)
                return true;
        }

        return false;
    }
}

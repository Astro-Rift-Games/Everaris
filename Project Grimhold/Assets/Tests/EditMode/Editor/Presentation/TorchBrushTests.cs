using NUnit.Framework;
using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class TorchBrushTests
{
    private const string BrushPath = "Assets/Art/Tilesets/Brushes/TorchBrush.asset";
    private const string TorchPrefabPath = "Assets/Prefabs/Lighting Props/Torch.prefab";
    private const string CatacombsScenePath = "Assets/Scenes/CatacombsMap.unity";
    private const string LightPropsTargetName = "LightProps";
    private const float PositionTolerance = 0.001f;

    private static readonly Vector3 ExpectedOffset = new(0f, 0.375f, 0f);

    private GameObject _gridObject;

    [TearDown]
    public void TearDown()
    {
        if (_gridObject != null)
            Object.DestroyImmediate(_gridObject);
    }

    [Test]
    public void TorchBrush_PaintsTheTorchPrefabFromTheCellCenter()
    {
        GameObjectBrush brush = LoadBrush();
        GameObject torchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TorchPrefabPath);
        Assert.That(torchPrefab, Is.Not.Null);

        Assert.That(brush.size, Is.EqualTo(Vector3Int.one));
        Assert.That(brush.cellCount, Is.EqualTo(1));
        // Unity equality (same native object): a prefab reimport can leave the brush holding an older managed wrapper.
        Assert.That(brush.cells[0].gameObject, Is.EqualTo(torchPrefab));
        Assert.That(brush.m_Anchor, Is.EqualTo(new Vector3(0.5f, 0.5f, 0f)));
        Assert.That((brush.cells[0].offset - ExpectedOffset).magnitude, Is.LessThan(PositionTolerance));
        Assert.That(brush.cells[0].scale, Is.EqualTo(Vector3.one));
        Assert.That(brush.cells[0].orientation, Is.EqualTo(Quaternion.identity));
    }

    [Test]
    public void TorchBrush_IsListedInTheTilePaletteBrushDropdown_WithTheGameObjectBrushInspector()
    {
        GameObjectBrush brush = LoadBrush();

        var paletteRule = (CustomGridBrushAttribute)System.Attribute.GetCustomAttribute(
            brush.GetType(), typeof(CustomGridBrushAttribute), inherit: false);
        Assert.That(paletteRule, Is.Not.Null, brush.GetType().Name);
        Assert.That(paletteRule.hideAssetInstances, Is.False,
            "Tile Palette must list the TorchBrush asset; plain GameObjectBrush assets are hidden by the package.");

        Editor brushEditor = Editor.CreateEditor(brush);
        try
        {
            Assert.That(brushEditor, Is.InstanceOf<GameObjectBrushEditor>(),
                "The brush must keep the package paint preview and cell inspector.");
        }
        finally
        {
            Object.DestroyImmediate(brushEditor);
        }
    }

    [Test]
    public void PaintedTorch_IsAPrefabInstanceWhoseSconceFillsExactlyOneCell()
    {
        GameObjectBrush brush = LoadBrush();
        Tilemap target = CreateGridWithTarget(out Grid grid);
        var cell = new Vector3Int(3, -2, 0);

        brush.Paint(grid, target.gameObject, cell);

        Assert.That(target.transform.childCount, Is.EqualTo(1));
        GameObject torch = target.transform.GetChild(0).gameObject;
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(torch), Is.EqualTo(TorchPrefabPath),
            "The brush must keep the prefab link so later Torch prefab edits reach every painted torch.");

        Vector3 cellCenter = grid.GetCellCenterWorld(cell);
        Bounds sconce = torch.GetComponent<SpriteRenderer>().bounds;
        Assert.That(Vector2.Distance(sconce.center, cellCenter), Is.LessThan(PositionTolerance),
            "The sconce sprite must be centered on the painted cell so every torch sits at the same height.");
        Assert.That(sconce.min.y, Is.GreaterThanOrEqualTo(cellCenter.y - 0.5f - PositionTolerance));
        Assert.That(sconce.max.y, Is.LessThanOrEqualTo(cellCenter.y + 0.5f + PositionTolerance));
    }

    [Test]
    public void PaintingAnOccupiedCell_DoesNotStackASecondTorch()
    {
        GameObjectBrush brush = LoadBrush();
        Tilemap target = CreateGridWithTarget(out Grid grid);
        var cell = new Vector3Int(-4, 7, 0);

        brush.Paint(grid, target.gameObject, cell);
        brush.Paint(grid, target.gameObject, cell);

        Assert.That(target.transform.childCount, Is.EqualTo(1));
    }

    [Test]
    public void CatacombsMap_HasAnEmptyLightPropsPaintTarget_ThatNoTilemapClassifierTreatsAsGeometry()
    {
        UnityEngine.SceneManagement.Scene scene =
            UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene(CatacombsScenePath);
        try
        {
            Tilemap target = null;
            Tilemap walls = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Tilemap tilemap in root.GetComponentsInChildren<Tilemap>(true))
                {
                    if (tilemap.name == LightPropsTargetName) target = tilemap;
                    if (tilemap.name == "Walls") walls = tilemap;
                }
            }

            Assert.That(target, Is.Not.Null, $"{CatacombsScenePath} needs a '{LightPropsTargetName}' Tilemap to paint torches into.");
            Assert.That(walls, Is.Not.Null);
            Assert.That(target.layoutGrid, Is.SameAs(walls.layoutGrid), "Torches must snap to the same Grid as the walls.");
            Assert.That(target.GetUsedTilesCount(), Is.Zero, "The paint target holds prefab instances, never tiles.");
            Assert.That(target.GetComponent<Collider2D>(), Is.Null);

            string lower = target.name.ToLowerInvariant();
            foreach (string geometryKeyword in new[] { "wall", "floor", "ground", "obstacle", "collision", "access", "stair" })
            {
                Assert.That(lower, Does.Not.Contain(geometryKeyword),
                    "Collapse rifts and ambient pressure classify tilemaps by name; the paint target must not read as geometry.");
            }
        }
        finally
        {
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static GameObjectBrush LoadBrush()
    {
        var brush = AssetDatabase.LoadAssetAtPath<GameObjectBrush>(BrushPath);
        Assert.That(brush, Is.Not.Null, $"Expected a GameObjectBrush asset at {BrushPath}.");
        return brush;
    }

    private Tilemap CreateGridWithTarget(out Grid grid)
    {
        _gridObject = new GameObject("TorchBrushTestGrid");
        grid = _gridObject.AddComponent<Grid>();
        grid.cellSize = new Vector3(1f, 1f, 0f);

        var targetObject = new GameObject("WallProps");
        targetObject.transform.SetParent(_gridObject.transform, false);
        return targetObject.AddComponent<Tilemap>();
    }
}

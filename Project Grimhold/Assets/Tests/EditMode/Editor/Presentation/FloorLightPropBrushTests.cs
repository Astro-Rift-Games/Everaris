using NUnit.Framework;
using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class FloorLightPropBrushTests
{
    private const string BrushFolder = "Assets/Art/Tilesets/Brushes/";
    private const string PrefabFolder = "Assets/Prefabs/Lighting Props/";
    private const float PositionTolerance = 0.001f;

    private GameObject _gridObject;

    [TearDown]
    public void TearDown()
    {
        if (_gridObject != null)
            Object.DestroyImmediate(_gridObject);
    }

    [TestCase("BrazierBrush", "Brazier", 0.34375f)]
    [TestCase("CandleBrush", "Candle", -0.03125f)]
    [TestCase("MultipleCandlesBrush", "Multiple Candles", 0.5f)]
    public void Brush_IsAPaletteVisiblePrefabBrushForItsProp(string brushName, string prefabName, float offsetY)
    {
        PrefabPaletteBrush brush = LoadBrush(brushName);
        GameObject prefab = LoadPrefab(prefabName);

        Assert.That(brush.size, Is.EqualTo(Vector3Int.one));
        Assert.That(brush.cellCount, Is.EqualTo(1));
        // Unity equality (same native object): a prefab reimport can leave the brush holding an older managed wrapper.
        Assert.That(brush.cells[0].gameObject, Is.EqualTo(prefab));
        Assert.That(brush.m_Anchor, Is.EqualTo(new Vector3(0.5f, 0.5f, 0f)));
        Assert.That((brush.cells[0].offset - new Vector3(0f, offsetY, 0f)).magnitude, Is.LessThan(PositionTolerance));
        Assert.That(brush.cells[0].scale, Is.EqualTo(Vector3.one));
        Assert.That(brush.cells[0].orientation, Is.EqualTo(Quaternion.identity));
    }

    [TestCase("BrazierBrush", "Brazier")]
    [TestCase("CandleBrush", "Candle")]
    [TestCase("MultipleCandlesBrush", "Multiple Candles")]
    public void PaintedProp_StandsOnTheBottomEdgeOfThePaintedCell(string brushName, string prefabName)
    {
        PrefabPaletteBrush brush = LoadBrush(brushName);
        Tilemap target = CreateGridWithTarget(out Grid grid);
        var cell = new Vector3Int(-3, 6, 0);

        brush.Paint(grid, target.gameObject, cell);

        Assert.That(target.transform.childCount, Is.EqualTo(1));
        GameObject prop = target.transform.GetChild(0).gameObject;
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prop),
            Is.EqualTo(PrefabFolder + prefabName + ".prefab"));

        Vector3 cellCenter = grid.GetCellCenterWorld(cell);
        Bounds body = prop.GetComponent<SpriteRenderer>().bounds;
        Assert.That(body.center.x, Is.EqualTo(cellCenter.x).Within(PositionTolerance),
            "The prop must be horizontally centered on the painted cell.");
        Assert.That(body.min.y, Is.EqualTo(cellCenter.y - 0.5f).Within(PositionTolerance),
            "Every floor light prop stands on the bottom edge of its cell so a row of props shares one base line.");
    }

    [TestCase("Brazier")]
    [TestCase("Candle")]
    [TestCase("Multiple Candles")]
    public void ExtinguishedSprite_SharesTheLitImageOrigin_SoExtinguishingDoesNotShiftTheProp(string prefabName)
    {
        DungeonLightProp prop = LoadPrefab(prefabName).GetComponent<DungeonLightProp>();
        Assert.That(prop, Is.Not.Null);
        Assert.That(prop.LitSprite, Is.Not.Null);
        Assert.That(prop.ExtinguishedSprite, Is.Not.Null);

        Vector2 litOrigin = prop.LitSprite.rect.position + prop.LitSprite.pivot;
        Vector2 extinguishedOrigin = prop.ExtinguishedSprite.rect.position + prop.ExtinguishedSprite.pivot;
        Assert.That((extinguishedOrigin - litOrigin).magnitude, Is.LessThan(PositionTolerance),
            $"{prop.ExtinguishedSprite.name} pivot must match {prop.LitSprite.name}.");
    }

    [Test]
    public void MultipleCandles_UsesTheCandleGroupSpritesAndStartsLit()
    {
        DungeonLightProp prop = LoadPrefab("Multiple Candles").GetComponent<DungeonLightProp>();

        Assert.That(prop.LitSprite.name, Is.EqualTo("Candles-Lit-Base_0"), "Lit body without the baked flames.");
        Assert.That(prop.ExtinguishedSprite.name, Is.EqualTo("Candles-Unlit_0"));
        Assert.That(prop.GetComponent<SpriteRenderer>().sprite, Is.EqualTo(prop.LitSprite));
    }

    private static PrefabPaletteBrush LoadBrush(string brushName)
    {
        string path = BrushFolder + brushName + ".asset";
        var brush = AssetDatabase.LoadAssetAtPath<PrefabPaletteBrush>(path);
        Assert.That(brush, Is.Not.Null, $"Expected a PrefabPaletteBrush asset at {path}.");
        return brush;
    }

    private static GameObject LoadPrefab(string prefabName)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + prefabName + ".prefab");
        Assert.That(prefab, Is.Not.Null, prefabName);
        return prefab;
    }

    private Tilemap CreateGridWithTarget(out Grid grid)
    {
        _gridObject = new GameObject("FloorLightPropBrushTestGrid");
        grid = _gridObject.AddComponent<Grid>();
        grid.cellSize = new Vector3(1f, 1f, 0f);

        var targetObject = new GameObject("LightProps");
        targetObject.transform.SetParent(_gridObject.transform, false);
        return targetObject.AddComponent<Tilemap>();
    }
}

using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class PrefabPaletteBrushTests
{
    private const string TorchPrefabPath = "Assets/Prefabs/Lighting Props/Torch.prefab";
    private const float PositionTolerance = 0.001f;

    private static readonly Vector3 AuthoredOffset = new(0f, 0.375f, 0f);

    private PrefabPaletteBrush _brush;
    private GameObject _torchPrefab;
    private GameObject _gridObject;
    private Grid _grid;
    private Tilemap _target;

    [SetUp]
    public void SetUp()
    {
        _torchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TorchPrefabPath);
        Assert.That(_torchPrefab, Is.Not.Null);

        _brush = ScriptableObject.CreateInstance<PrefabPaletteBrush>();
        _brush.Init(Vector3Int.one, Vector3Int.zero);
        _brush.SetGameObject(Vector3Int.zero, _torchPrefab);
        _brush.SetOffset(Vector3Int.zero, AuthoredOffset);

        _gridObject = new GameObject("PrefabPaletteBrushTestGrid");
        _grid = _gridObject.AddComponent<Grid>();
        var targetObject = new GameObject("LightProps");
        targetObject.transform.SetParent(_gridObject.transform, false);
        _target = targetObject.AddComponent<Tilemap>();
    }

    [TearDown]
    public void TearDown()
    {
        if (_gridObject != null) Object.DestroyImmediate(_gridObject);
        if (_brush != null) Object.DestroyImmediate(_brush);
    }

    [Test]
    public void PickingAnEmptyCell_KeepsTheAuthoredPrefabAndOffset()
    {
        _brush.Pick(_grid, _target.gameObject, new BoundsInt(5, 5, 0, 1, 1, 1), Vector3Int.zero);

        AssertAuthoredConfiguration();
    }

    [Test]
    public void PickingAPaintedPrefab_StillLoadsThatPrefabIntoTheBrush()
    {
        var cell = new Vector3Int(2, 1, 0);
        _brush.Paint(_grid, _target.gameObject, cell);

        _brush.Pick(_grid, _target.gameObject, new BoundsInt(cell, Vector3Int.one), Vector3Int.zero);

        Assert.That(_brush.cellCount, Is.EqualTo(1));
        Assert.That(_brush.cells[0].gameObject, Is.SameAs(_torchPrefab));
        Assert.That((_brush.cells[0].offset - AuthoredOffset).magnitude, Is.LessThan(PositionTolerance));
    }

    [Test]
    public void MovingPaintedPrefabs_MovesThemAndThenRestoresTheAuthoredBrush()
    {
        var from = new Vector3Int(0, 0, 0);
        var to = new Vector3Int(4, 0, 0);
        _brush.Paint(_grid, _target.gameObject, from);

        _brush.MoveStart(_grid, _target.gameObject, new BoundsInt(from, Vector3Int.one));
        _brush.MoveEnd(_grid, _target.gameObject, new BoundsInt(to, Vector3Int.one));

        Assert.That(_target.transform.childCount, Is.EqualTo(1));
        Vector2 expected = (Vector2)_grid.GetCellCenterWorld(to) + (Vector2)AuthoredOffset;
        Assert.That(Vector2.Distance(_target.transform.GetChild(0).position, expected), Is.LessThan(PositionTolerance));
        AssertAuthoredConfiguration();
    }

    private void AssertAuthoredConfiguration()
    {
        Assert.That(_brush.size, Is.EqualTo(Vector3Int.one));
        Assert.That(_brush.cellCount, Is.EqualTo(1));
        Assert.That(_brush.cells[0].gameObject, Is.SameAs(_torchPrefab),
            "A pick or move that finds no prefab must not leave the brush empty.");
        Assert.That((_brush.cells[0].offset - AuthoredOffset).magnitude, Is.LessThan(PositionTolerance));
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public sealed class CatacombsDoorTests
{
    private const string SheetPath = "Assets/Art/Props/Doors/Catacombs-Door-Sheet.png";
    private const string AtlasPath = "Assets/Art/Tilesets/Catacombs/Catacombs-Atlas.png";
    private const string BaseControllerPath = "Assets/Animations/Doors/DungeonDoor.controller";
    private const string OverridePath = "Assets/Animations/Doors/CatacombsDoor.overrideController";
    private const string ClipFolder = "Assets/Animations/Doors/";
    private const string BasePrefabPath = "Assets/Prefabs/Doors/DungeonDoor.prefab";
    private const string VariantPath = "Assets/Prefabs/Doors/CatacombsDoor.prefab";
    private const string BrushPath = "Assets/Art/Tilesets/Brushes/CatacombsDoorBrush.asset";
    private const string CatacombsScenePath = "Assets/Scenes/CatacombsMap.unity";
    private const int FrameCount = 4;
    private const float Tolerance = 0.001f;

    // Bottom-left cells of the 20 doors on the enabled floor (17 fully in Access, 3 with the cap row in Walls).
    private static readonly Vector3Int[] DoorCells =
    {
        new(-127, -56, 0), new(-87, 11, 0), new(-68, -9, 0), new(-55, -90, 0), new(-24, 35, 0),
        new(-11, -36, 0), new(-2, -36, 0), new(7, -36, 0), new(18, 23, 0), new(35, 14, 0),
        new(49, 35, 0), new(56, -66, 0), new(61, -44, 0), new(77, 59, 0), new(101, -37, 0),
        new(118, 61, 0), new(128, 12, 0), new(29, -36, 0), new(38, -36, 0), new(46, -36, 0),
    };

    private static readonly (string State, int[] Frames, bool Loop)[] Clips =
    {
        ("Door_Closed", new[] { 0 }, true),
        ("Door_Opening_North", new[] { 0, 1, 2, 3 }, false),
        ("Door_Open_North", new[] { 3 }, true),
        ("Door_Closing_North", new[] { 3, 2, 1, 0 }, false),
    };

    private GameObject _gridObject;

    [TearDown]
    public void TearDown()
    {
        if (_gridObject != null)
            Object.DestroyImmediate(_gridObject);
    }

    [Test]
    public void Sheet_UsesPixelArtImport_WithFourDoorFramesPivotedOnTheThreshold()
    {
        var importer = AssetImporter.GetAtPath(SheetPath) as TextureImporter;
        Assert.That(importer, Is.Not.Null, SheetPath);
        Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
        Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple));
        Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16f));
        Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
        Assert.That(importer.mipmapEnabled, Is.False);
        Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));

        Sprite[] frames = LoadFrames();
        for (int i = 0; i < FrameCount; i++)
        {
            Assert.That(frames[i].rect, Is.EqualTo(new Rect(i * 32f, 0f, 32f, 48f)), frames[i].name);
            Assert.That((frames[i].pivot - new Vector2(16f, 0f)).magnitude, Is.LessThan(Tolerance),
                "Bottom-centre pivot: the door threshold sits on the NetworkDoor root.");
        }
    }

    [Test]
    public void ClosedFrame_MatchesTheMapDoorTilesPixelExact()
    {
        Texture2D sheet = ReadPng(SheetPath);
        Texture2D atlas = ReadPng(AtlasPath);
        try
        {
            // Atlas door tiles: x 1616-1647, lower 3 tile rows y 1328-1375 (bottom origin).
            for (int y = 0; y < 48; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    Color32 expected = atlas.GetPixel(1616 + x, 1328 + y);
                    Color32 actual = sheet.GetPixel(x, y);
                    if (expected.a == 0 && actual.a == 0)
                        continue;
                    Assert.That(actual, Is.EqualTo(expected), $"pixel ({x},{y})");
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(atlas);
        }
    }

    [Test]
    public void OverrideController_KeepsThePresenterStates_AndSwapsEveryClipForTheDoubleDoor()
    {
        var overrideController = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(OverridePath);
        Assert.That(overrideController, Is.Not.Null, OverridePath);
        Assert.That(overrideController.runtimeAnimatorController,
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<AnimatorController>(BaseControllerPath)));

        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        overrideController.GetOverrides(overrides);
        Sprite[] frames = LoadFrames();
        foreach ((string state, int[] keysFrames, bool loop) in Clips)
        {
            KeyValuePair<AnimationClip, AnimationClip> pair = overrides.Single(o => o.Key.name == state);
            AnimationClip clip = pair.Value;
            Assert.That(clip, Is.Not.Null, $"{state} must be overridden.");
            Assert.That(clip.frameRate, Is.EqualTo(pair.Key.frameRate), state);
            Assert.That(clip.length, Is.EqualTo(pair.Key.length).Within(0.0001f), state);
            Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime, Is.EqualTo(loop), state);

            EditorCurveBinding binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single();
            Assert.That(binding.type, Is.EqualTo(typeof(SpriteRenderer)));
            Assert.That(binding.propertyName, Is.EqualTo("m_Sprite"));
            Assert.That(binding.path, Is.EqualTo(AnimationUtility.GetObjectReferenceCurveBindings(pair.Key).Single().path));
            Sprite[] keyed = AnimationUtility.GetObjectReferenceCurve(clip, binding).Select(k => (Sprite)k.value).ToArray();
            Sprite[] expected = keysFrames.Select(i => frames[i]).ToArray();
            Assert.That(keyed.Distinct(), Is.EqualTo(expected.Distinct()), state);
            Assert.That(keyed.First(), Is.EqualTo(expected.First()), state);
            Assert.That(keyed.Last(), Is.EqualTo(expected.Last()), state);
        }
    }

    [Test]
    public void Variant_InheritsTheNetworkDoor_AndShowsTheClosedDoubleDoor()
    {
        var variant = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
        Assert.That(variant, Is.Not.Null, VariantPath);
        Assert.That(PrefabUtility.GetPrefabAssetType(variant), Is.EqualTo(PrefabAssetType.Variant));
        Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(variant)), Is.EqualTo(BasePrefabPath));

        Assert.That(variant.GetComponent<NetworkDoor>(), Is.Not.Null);
        Assert.That(variant.GetComponent<DungeonDoorPresenter>(), Is.Not.Null);

        Transform view = variant.transform.Find("SortAnchor/View");
        Assert.That(view, Is.Not.Null);
        Assert.That(view.position, Is.EqualTo(variant.transform.position), "View sits on the root threshold.");
        Assert.That(view.GetComponent<SpriteRenderer>().sprite, Is.EqualTo(LoadFrames()[0]));
        Assert.That(view.GetComponent<Animator>().runtimeAnimatorController,
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(OverridePath)));
    }

    [Test]
    public void Brush_PaintsTheVariantFromTheDoorsBottomLeftCell_SoTheClosedDoorCoversItsTiles()
    {
        var brush = AssetDatabase.LoadAssetAtPath<PrefabPaletteBrush>(BrushPath);
        Assert.That(brush, Is.Not.Null, BrushPath);
        Assert.That(brush.cells[0].gameObject, Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath)));

        _gridObject = new GameObject("CatacombsDoorTestGrid");
        var grid = _gridObject.AddComponent<Grid>();
        grid.cellSize = new Vector3(1f, 1f, 0f);
        var target = new GameObject("Doors");
        target.transform.SetParent(_gridObject.transform, false);
        target.AddComponent<Tilemap>();
        var cell = new Vector3Int(18, 23, 0);

        brush.Paint(grid, target, cell);

        Assert.That(target.transform.childCount, Is.EqualTo(1));
        Transform door = target.transform.GetChild(0);
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(door.gameObject), Is.EqualTo(VariantPath));
        Assert.That(Vector2.Distance(door.position, new Vector2(cell.x + 1f, cell.y)), Is.LessThan(Tolerance),
            "Root on the threshold between the door's two bottom cells.");
        Bounds sprite = door.Find("SortAnchor/View").GetComponent<SpriteRenderer>().bounds;
        Assert.That(sprite.min.x, Is.EqualTo(cell.x).Within(Tolerance));
        Assert.That(sprite.max.x, Is.EqualTo(cell.x + 2f).Within(Tolerance));
        Assert.That(sprite.min.y, Is.EqualTo(cell.y).Within(Tolerance));
        Assert.That(sprite.max.y, Is.EqualTo(cell.y + 3f).Within(Tolerance));
    }

    [Test]
    public void CatacombsMap_HasANetworkedDoubleDoorOnEveryDoorway_AndNoDoorTilesBelowTheCap()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(CatacombsScenePath);
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            NetworkDoor[] doors = roots.SelectMany(r => r.GetComponentsInChildren<NetworkDoor>(true)).ToArray();
            Assert.That(doors, Has.Length.EqualTo(DoorCells.Length));
            foreach (NetworkDoor door in doors)
            {
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(door.gameObject), Is.EqualTo(VariantPath), door.name);
            }

            foreach (Vector3Int cell in DoorCells)
            {
                Vector2 threshold = new(cell.x + 1f, cell.y);
                Assert.That(doors.Count(d => Vector2.Distance(d.transform.position, threshold) < Tolerance), Is.EqualTo(1),
                    $"door at {cell}");
            }

            Tilemap[] tilemaps = roots.SelectMany(r => r.GetComponentsInChildren<Tilemap>(true)).ToArray();
            Tilemap access = tilemaps.Single(t => t.name == "Access");
            Tilemap walls = tilemaps.Single(t => t.name == "Walls");
            foreach (Vector3Int cell in DoorCells)
            {
                for (int dy = 0; dy < 3; dy++)
                {
                    for (int dx = 0; dx < 2; dx++)
                    {
                        Vector3Int c = cell + new Vector3Int(dx, dy, 0);
                        Assert.That(access.HasTile(c), Is.False, $"Access door tile left at {c}");
                        Assert.That(walls.HasTile(c), Is.False, $"Walls tile inside doorway at {c}");
                    }
                }

                Vector3Int cap = cell + new Vector3Int(0, 3, 0);
                Assert.That(access.HasTile(cap) || walls.HasTile(cap), Is.True, $"stone cap kept at {cap}");
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static Sprite[] LoadFrames()
    {
        Sprite[] all = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().ToArray();
        Assert.That(all, Has.Length.EqualTo(FrameCount), SheetPath);
        return Enumerable.Range(0, FrameCount).Select(i => all.Single(s => s.name == $"Catacombs-Door-Sheet_{i}")).ToArray();
    }

    private static Texture2D ReadPng(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Assert.That(texture.LoadImage(File.ReadAllBytes(path)), Is.True, path);
        return texture;
    }
}

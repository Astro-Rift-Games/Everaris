using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Assert = NUnit.Framework.Assert;

public sealed class GameplayCameraSceneCompositionTests
{
    private const string ConfigPath = "Assets/Scriptable Objects/GameplayCameraConfig.asset";
    private const float Tolerance = 0.01f;

    // Scenes are inspected through isolated preview scenes and never saved, so the
    // dirty state of scenes left open by other tests does not affect these checks.
    private Scene _scene;

    [TearDown]
    public void CloseWithoutSaving()
    {
        if (_scene.IsValid())
        {
            EditorSceneManager.ClosePreviewScene(_scene);
        }
    }

    [TestCase("Assets/Scenes/Lobby-Town.unity")]
    [TestCase("Assets/Scenes/CatacombsMap.unity")]
    public void MappedScene_WiresCameraConfigCursorInputAndEnclosingBounds(string scenePath)
    {
        _scene = EditorSceneManager.OpenPreviewScene(scenePath);

        LocalCameraController controller = One<LocalCameraController>();
        var serialized = new SerializedObject(controller);

        Assert.That(serialized.FindProperty("_config").objectReferenceValue,
            Is.SameAs(AssetDatabase.LoadAssetAtPath<GameplayCameraConfig>(ConfigPath)));
        Assert.That(serialized.FindProperty("_cursorInput").objectReferenceValue, Is.SameAs(One<PlayerInputReader>()));

        var mapBounds = serialized.FindProperty("_mapBounds").objectReferenceValue as CameraMapBounds;
        Assert.That(mapBounds, Is.Not.Null, "LocalCameraController._mapBounds must be assigned.");
        Assert.That(One<CameraMapBounds>(), Is.SameAs(mapBounds));

        Bounds tilemapUnion = TilemapWorldUnion();
        Rect bounds = mapBounds.WorldBounds;
        Assert.That(bounds.xMin, Is.LessThanOrEqualTo(tilemapUnion.min.x + Tolerance), "min x");
        Assert.That(bounds.yMin, Is.LessThanOrEqualTo(tilemapUnion.min.y + Tolerance), "min y");
        Assert.That(bounds.xMax, Is.GreaterThanOrEqualTo(tilemapUnion.max.x - Tolerance), "max x");
        Assert.That(bounds.yMax, Is.GreaterThanOrEqualTo(tilemapUnion.max.y - Tolerance), "max y");
    }

    [TestCase("Assets/Scenes/Gameplay.unity")]
    [TestCase("Assets/Scenes/AbilitySandbox.unity")]
    public void UnboundedScene_WiresCameraConfigAndCursorInput(string scenePath)
    {
        _scene = EditorSceneManager.OpenPreviewScene(scenePath);

        var serialized = new SerializedObject(One<LocalCameraController>());

        Assert.That(serialized.FindProperty("_config").objectReferenceValue,
            Is.SameAs(AssetDatabase.LoadAssetAtPath<GameplayCameraConfig>(ConfigPath)));
        Assert.That(serialized.FindProperty("_cursorInput").objectReferenceValue, Is.SameAs(One<PlayerInputReader>()));
    }

    private Bounds TilemapWorldUnion()
    {
        bool hasBounds = false;
        Bounds union = default;

        foreach (Tilemap tilemap in All<Tilemap>())
        {
            tilemap.CompressBounds();
            Bounds local = tilemap.localBounds;
            if (local.size == Vector3.zero)
            {
                continue;
            }

            foreach (float x in new[] { local.min.x, local.max.x })
            {
                foreach (float y in new[] { local.min.y, local.max.y })
                {
                    Vector3 world = tilemap.transform.TransformPoint(new Vector3(x, y, 0f));
                    if (!hasBounds)
                    {
                        union = new Bounds(world, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        union.Encapsulate(world);
                    }
                }
            }
        }

        Assert.That(hasBounds, Is.True, "Scene has no populated tilemaps.");
        return union;
    }

    private T One<T>() where T : Component
    {
        T[] found = All<T>();
        Assert.That(found, Has.Length.EqualTo(1), "Expected exactly one " + typeof(T).Name + ".");
        return found[0];
    }

    private T[] All<T>() where T : Component
    {
        return _scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    }
}

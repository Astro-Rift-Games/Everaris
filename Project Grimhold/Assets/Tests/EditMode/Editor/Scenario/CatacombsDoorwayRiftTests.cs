using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Door tiles left the Access tilemap when doors became prefabs, so the rift classifier no longer sees them.
/// No collapse floor rift may still appear inside a doorway: the walls on both sides of every 2-cell doorway
/// fail the floor margin.
/// </summary>
public sealed class CatacombsDoorwayRiftTests
{
    private const string ScenePath = "Assets/Scenes/CatacombsMap.unity";
    private const string ConfigPath = "Assets/Scriptable Objects/DefaultCollapseRiftConfig.asset";

    [Test]
    public void NoPossibleFloorRift_FallsInsideADoorway()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            Tilemap[] tilemaps = roots.SelectMany(r => r.GetComponentsInChildren<Tilemap>(true)).ToArray();
            NetworkDoor[] doors = roots.SelectMany(r => r.GetComponentsInChildren<NetworkDoor>(true)).ToArray();
            Assert.That(doors, Has.Length.EqualTo(20));

            var config = AssetDatabase.LoadAssetAtPath<CollapseRiftConfig>(ConfigPath);
            Assert.That(config, Is.Not.Null, ConfigPath);
            CollapseRiftPlacementSettings authored = config.BuildPlacementSettings();
            // Every valid candidate becomes a placement: no spacing, no exclusions, unbounded counts.
            var exhaustive = new CollapseRiftPlacementSettings(
                100000, 0, 0, authored.FloorMarginCells, authored.WallWidthCells, authored.WallFaceHeightCells,
                authored.WallHorizontalMarginCells, authored.WallTopMarginCells, 0f, 0f, 0f, 0f);

            var floor = new List<CollapseRiftPlacement>();
            CollapseRiftPlacementService.GeneratePlacements(
                TilemapCollapseSurfaceGrid.BuildFromTilemaps(tilemaps), exhaustive,
                new List<Vector2>(), new List<Vector2>(), 1, floor, new List<CollapseRiftPlacement>());
            Assert.That(floor, Is.Not.Empty, "The map must still offer floor rift candidates.");

            foreach (NetworkDoor door in doors)
            {
                // Doorway: 2 cells wide around the threshold root, 4 rows from the threshold up.
                Vector2 root = door.transform.position;
                var doorway = new Rect(root.x - 1f, root.y, 2f, 4f);
                foreach (CollapseRiftPlacement placement in floor)
                    Assert.That(doorway.Contains(placement.WorldPosition), Is.False,
                        $"floor rift candidate {placement.WorldPosition} inside the doorway at {root}");
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}

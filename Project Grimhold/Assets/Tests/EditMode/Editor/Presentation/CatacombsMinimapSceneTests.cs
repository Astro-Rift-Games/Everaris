using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public sealed class CatacombsMinimapSceneTests
{
    private const string ScenePath = "Assets/Scenes/CatacombsMap.unity";
    private const string LayoutPath = "Assets/Data/Minimap/CatacombsMinimapLayout.asset";

    [SetUp]
    public void RequireCleanScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Assert.That(SceneManager.GetSceneAt(i).isDirty, Is.False, "Minimap tests require clean open scenes.");
    }

    [Test]
    public void Generator_FindsEveryRaidSceneAndKeepsOneLayoutPerScene()
    {
        Assert.That(MinimapLayoutGenerator.FindMinimapScenes(),
            Does.Contain(ScenePath).And.Contain(MinimapLayoutGenerator.SourceScenePath));
        Assert.That(MinimapLayoutGenerator.TryBuildGeneratedData(ScenePath, out var catacombs,
            out string catacombsPath, out string error), Is.True, error);
        Assert.That(MinimapLayoutGenerator.TryBuildGeneratedData(MinimapLayoutGenerator.SourceScenePath,
            out var gameplay, out string gameplayPath, out error), Is.True, error);
        Assert.That(catacombsPath, Is.EqualTo(LayoutPath));
        Assert.That(gameplayPath, Is.EqualTo(MinimapLayoutGenerator.LayoutAssetPath).And.Not.EqualTo(catacombsPath));
        Assert.That(catacombs.SourceHash, Is.Not.EqualTo(gameplay.SourceHash));
        Assert.That(catacombs.CellFlags.Count(value => (value & (byte)MinimapLayoutCellFlags.Floor) != 0), Is.GreaterThan(0));
        Assert.That(MinimapLayoutGenerator.IsSourceDependency(ScenePath), Is.True);
        Assert.That(MinimapLayoutGenerator.IsSourceDependency(LayoutPath), Is.False);
    }

    [Test]
    public void SavedDeclaration_HasItsOwnValidLayoutMatchingEverySourceCellAndWorldBounds()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        try
        {
            RaidSceneMinimap[] declarations = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<RaidSceneMinimap>(true)).ToArray();
            Assert.That(declarations, Has.Length.EqualTo(1));
            RaidSceneMinimap declaration = declarations[0];
            Assert.That(declaration.isActiveAndEnabled, Is.True);
            Assert.That(AssetDatabase.GetAssetPath(declaration.Layout), Is.EqualTo(LayoutPath));
            Assert.That(declaration.Layout.TryValidate(out string error), Is.True, error);
            Assert.That(declaration.SourceTilemapRoot.name, Is.EqualTo("Dungeon_Graybox"));
            Assert.That(declaration.SourceTilemapRoot.scene, Is.EqualTo(scene));
            Assert.That(MinimapLayoutGenerator.TryBuildGeneratedData(ScenePath, out var data,
                out _, out error), Is.True, error);
            MinimapLayout layout = declaration.Layout;
            Assert.That(layout.MinimumCell, Is.EqualTo(data.MinimumCell));
            Assert.That(layout.SizeInCells, Is.EqualTo(data.SizeInCells));
            Assert.That(layout.CellSize, Is.EqualTo(data.CellSize));
            Assert.That(layout.WorldPivotPosition, Is.EqualTo(data.WorldPivotPosition));
            Assert.That(layout.SourceHash, Is.EqualTo(data.SourceHash));
            for (int y = 0; y < data.SizeInCells.y; y++)
                for (int x = 0; x < data.SizeInCells.x; x++)
                    Assert.That(layout.GetCellFlags(data.MinimumCell + new Vector2Int(x, y)),
                        Is.EqualTo((MinimapLayoutCellFlags)data.CellFlags[y * data.SizeInCells.x + x]));
            Tilemap floor = declaration.SourceTilemapRoot.GetComponentsInChildren<Tilemap>(true)
                .Single(map => map.name == "Floor");
            int floorCount = 0;
            foreach (Vector3Int cell in floor.cellBounds.allPositionsWithin)
            {
                if (!floor.HasTile(cell)) continue;
                floorCount++;
                Assert.That(layout.GetCellFlags(new Vector2Int(cell.x, cell.y)) & MinimapLayoutCellFlags.Floor,
                    Is.EqualTo(MinimapLayoutCellFlags.Floor), cell.ToString());
            }
            Assert.That(floorCount, Is.GreaterThan(0));
            Assert.That(data.CellFlags.Count(value => (value & (byte)MinimapLayoutCellFlags.Floor) != 0),
                Is.EqualTo(floorCount));
            Vector3 min = floor.layoutGrid.CellToWorld(new Vector3Int(data.MinimumCell.x, data.MinimumCell.y));
            Vector3 max = floor.layoutGrid.CellToWorld(new Vector3Int(data.MinimumCell.x + data.SizeInCells.x,
                data.MinimumCell.y + data.SizeInCells.y));
            Assert.That(layout.WorldPivotPosition, Is.EqualTo(((Vector2)min + (Vector2)max) * 0.5f));
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void TargetedGeneration_IsIdempotentAndNeverRewritesGameplayOrSourceScene()
    {
        byte[] gameplay = File.ReadAllBytes(MinimapLayoutGenerator.LayoutAssetPath);
        byte[] scene = File.ReadAllBytes(ScenePath);
        Assert.That(MinimapLayoutGenerator.EnsureCurrentLayout(ScenePath, out string error), Is.True, error);
        byte[] generated = File.ReadAllBytes(LayoutPath);
        Assert.That(MinimapLayoutGenerator.EnsureCurrentLayout(ScenePath, out error), Is.True, error);
        Assert.That(File.ReadAllBytes(LayoutPath), Is.EqualTo(generated));
        Assert.That(File.ReadAllBytes(MinimapLayoutGenerator.LayoutAssetPath), Is.EqualTo(gameplay));
        Assert.That(File.ReadAllBytes(ScenePath), Is.EqualTo(scene));
    }
}

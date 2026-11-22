#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Presentation
{
    public sealed class CollapseRiftPlacementServiceTests
    {
        private sealed class FakeSurfaceGrid : ICollapseSurfaceGrid
        {
            private readonly HashSet<Vector2Int> _floors = new();
            private readonly HashSet<Vector2Int> _walls = new();
            private readonly HashSet<Vector2Int> _overlays = new();

            public BoundsInt CellBounds { get; private set; }

            public void FillFloorRect(int minX, int minY, int width, int height)
            {
                for (int y = minY; y < minY + height; y++)
                {
                    for (int x = minX; x < minX + width; x++)
                    {
                        _floors.Add(new Vector2Int(x, y));
                    }
                }
                RecalculateBounds();
            }

            public void FillWallRect(int minX, int minY, int width, int height)
            {
                for (int y = minY; y < minY + height; y++)
                {
                    for (int x = minX; x < minX + width; x++)
                    {
                        _walls.Add(new Vector2Int(x, y));
                        _overlays.Add(new Vector2Int(x, y));
                    }
                }
                RecalculateBounds();
            }

            public void AddBlockingOverlay(int x, int y)
            {
                _overlays.Add(new Vector2Int(x, y));
            }

            public bool HasFloorTile(Vector2Int cell) => _floors.Contains(cell);
            public bool HasWallTile(Vector2Int cell) => _walls.Contains(cell);
            public bool HasBlockingFloorOverlay(Vector2Int cell) => _overlays.Contains(cell);
            public Vector2 CellToWorldCenter(Vector2Int cell) => new Vector2(cell.x + 0.5f, cell.y + 0.5f);
            public Vector2 CellToWorldBottomLeft(Vector2Int cell) => new Vector2(cell.x, cell.y);

            private void RecalculateBounds()
            {
                int minX = int.MaxValue, minY = int.MaxValue;
                int maxX = int.MinValue, maxY = int.MinValue;
                foreach (var c in _floors)
                {
                    if (c.x < minX) minX = c.x;
                    if (c.y < minY) minY = c.y;
                    if (c.x > maxX) maxX = c.x;
                    if (c.y > maxY) maxY = c.y;
                }
                foreach (var c in _walls)
                {
                    if (c.x < minX) minX = c.x;
                    if (c.y < minY) minY = c.y;
                    if (c.x > maxX) maxX = c.x;
                    if (c.y > maxY) maxY = c.y;
                }
                CellBounds = new BoundsInt(minX, minY, 0, (maxX - minX) + 1, (maxY - minY) + 1, 1);
            }
        }

        [Test]
        public void FloorRifts_RespectSurfaceMargin_NeverOverlapWallsOrOverlays_AndSplitIntoStage2AndStage3Batches()
        {
            var grid = new FakeSurfaceGrid();
            // 12x12 floor room surrounded by walls
            grid.FillFloorRect(0, 0, 12, 12);
            grid.FillWallRect(0, 12, 12, 3);
            grid.AddBlockingOverlay(5, 5); // obstacle in center

            var settings = new CollapseRiftPlacementSettings(
                stage2FloorCount: 4,
                stage3FloorCount: 3,
                wallCount: 0,
                floorMarginCells: 1,
                wallWidthCells: 2,
                wallFaceHeightCells: 2,
                wallHorizontalMarginCells: 1,
                wallTopMarginCells: 1,
                minFloorSpacing: 2.0f,
                minWallSpacing: 3.0f,
                sanctuaryExclusionRadius: 2.5f,
                wallPropExclusionRadius: 1.5f);

            var floorExclusions = new List<Vector2> { new Vector2(2.5f, 2.5f) };
            var floorResults = new List<CollapseRiftPlacement>();
            var wallResults = new List<CollapseRiftPlacement>();

            CollapseRiftPlacementService.GeneratePlacements(
                grid, settings, floorExclusions, null, seed: 42, floorResults, wallResults);

            Assert.That(floorResults.Count, Is.EqualTo(7));
            int stage2Count = 0;
            int stage3Count = 0;

            for (int i = 0; i < floorResults.Count; i++)
            {
                var p = floorResults[i];
                Assert.That(p.SurfaceType, Is.EqualTo(CollapseRiftSurfaceType.Floor));
                if (p.Batch == CollapseRiftBatch.Stage2Initial) stage2Count++;
                else if (p.Batch == CollapseRiftBatch.Stage3Secondary) stage3Count++;

                // Must keep full 1-cell floor margin around anchor
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var neighbor = new Vector2Int(p.AnchorCell.x + dx, p.AnchorCell.y + dy);
                        Assert.That(grid.HasFloorTile(neighbor), Is.True, $"Missing floor margin at {neighbor}");
                        Assert.That(grid.HasBlockingFloorOverlay(neighbor), Is.False, $"Overlapping obstacle/wall margin at {neighbor}");
                    }
                }

                // Must stay outside sanctuary exclusion radius
                Assert.That(Vector2.Distance(p.WorldPosition, floorExclusions[0]), Is.GreaterThanOrEqualTo(2.5f));

                // Must respect spacing between floor rifts
                for (int j = i + 1; j < floorResults.Count; j++)
                {
                    Assert.That(Vector2.Distance(p.WorldPosition, floorResults[j].WorldPosition), Is.GreaterThanOrEqualTo(2.0f - 0.001f));
                }
            }

            Assert.That(stage2Count, Is.EqualTo(4));
            Assert.That(stage3Count, Is.EqualTo(3));
        }

        [Test]
        public void WallRifts_OnlySpawnOnValidSouthFacingWallFaces_WithHorizontalAndTopMargins_AndAvoidProps()
        {
            var grid = new FakeSurfaceGrid();
            // Room floor from y=0..4, south-facing wall at y=5..7 (3 tiles tall: 32px face + 1 tile top margin), x=0..15
            grid.FillFloorRect(0, 0, 16, 5);
            grid.FillWallRect(0, 5, 16, 3);
            // Short 1-tile wall strip on the right that is too short for a 32px wall crack
            grid.FillFloorRect(20, 0, 8, 5);
            grid.FillWallRect(20, 5, 8, 1);

            var settings = new CollapseRiftPlacementSettings(
                stage2FloorCount: 0,
                stage3FloorCount: 0,
                wallCount: 3,
                floorMarginCells: 1,
                wallWidthCells: 2,
                wallFaceHeightCells: 2,
                wallHorizontalMarginCells: 1,
                wallTopMarginCells: 1,
                minFloorSpacing: 2.0f,
                minWallSpacing: 3.5f,
                sanctuaryExclusionRadius: 2.0f,
                wallPropExclusionRadius: 2.0f);

            var wallProps = new List<Vector2> { new Vector2(8.0f, 6.5f) };
            var floorResults = new List<CollapseRiftPlacement>();
            var wallResults = new List<CollapseRiftPlacement>();

            CollapseRiftPlacementService.GeneratePlacements(
                grid, settings, null, wallProps, seed: 99, floorResults, wallResults);

            Assert.That(wallResults.Count, Is.GreaterThanOrEqualTo(2));
            for (int i = 0; i < wallResults.Count; i++)
            {
                var p = wallResults[i];
                Assert.That(p.SurfaceType, Is.EqualTo(CollapseRiftSurfaceType.Wall));
                Assert.That(p.AnchorCell.y, Is.EqualTo(5), "Wall anchor must sit on the bottom row of the south-facing wall.");
                Assert.That(p.AnchorCell.x, Is.InRange(1, 13), "Wall anchor must keep horizontal margin inside [0..15] for 2-tile width.");
                Assert.That(p.WorldPosition.y, Is.EqualTo(6.5f).Within(0.001f), "48x48 sprite centered at cellBottomY + 1.5 aligns bottom crack pixels to wall seam.");
                Assert.That(Vector2.Distance(p.WorldPosition, wallProps[0]), Is.GreaterThanOrEqualTo(2.0f));
            }
        }

        [Test]
        public void SameSeedProducesIdenticalPlacements_DifferentSeedsVaryLocations()
        {
            var grid = new FakeSurfaceGrid();
            grid.FillFloorRect(0, 0, 24, 16);
            grid.FillWallRect(0, 16, 24, 3);

            var settings = new CollapseRiftPlacementSettings(
                stage2FloorCount: 6,
                stage3FloorCount: 6,
                wallCount: 4,
                floorMarginCells: 1,
                wallWidthCells: 2,
                wallFaceHeightCells: 2,
                wallHorizontalMarginCells: 1,
                wallTopMarginCells: 1,
                minFloorSpacing: 2.5f,
                minWallSpacing: 3.5f,
                sanctuaryExclusionRadius: 2.0f,
                wallPropExclusionRadius: 1.5f);

            var f1 = new List<CollapseRiftPlacement>();
            var w1 = new List<CollapseRiftPlacement>();
            var f2 = new List<CollapseRiftPlacement>();
            var w2 = new List<CollapseRiftPlacement>();
            var f3 = new List<CollapseRiftPlacement>();
            var w3 = new List<CollapseRiftPlacement>();

            CollapseRiftPlacementService.GeneratePlacements(grid, settings, null, null, 12345, f1, w1);
            CollapseRiftPlacementService.GeneratePlacements(grid, settings, null, null, 12345, f2, w2);
            CollapseRiftPlacementService.GeneratePlacements(grid, settings, null, null, 98765, f3, w3);

            Assert.That(f1.Count, Is.EqualTo(f2.Count));
            for (int i = 0; i < f1.Count; i++)
            {
                Assert.That(f1[i].WorldPosition, Is.EqualTo(f2[i].WorldPosition));
            }

            Assert.That(w1.Count, Is.EqualTo(w2.Count));
            for (int i = 0; i < w1.Count; i++)
            {
                Assert.That(w1[i].WorldPosition, Is.EqualTo(w2[i].WorldPosition));
            }

            bool anyDifference = false;
            for (int i = 0; i < f1.Count && i < f3.Count; i++)
            {
                if (f1[i].WorldPosition != f3[i].WorldPosition)
                {
                    anyDifference = true;
                    break;
                }
            }
            Assert.That(anyDifference, Is.True, "Different raid seeds must produce different random rift layouts.");
        }
    }
}
#endif

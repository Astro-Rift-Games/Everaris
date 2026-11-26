using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Deterministic random surface placement service for floor and wall collapse rifts.
/// Enforces strict surface margins so floor rifts stay completely inside walkable floor tiles
/// and wall rifts stay completely inside visible south-facing vertical wall faces.
/// </summary>
public static class CollapseRiftPlacementService
{
    // Wall rift sprites are 48x48 (3 world units at 16 PPU) with crack pixels occupying the bottom 32 px
    // (y=[16..46] in top-down PNG coordinates). Centering at cellBottomY + 1.5 aligns the bottom edge to the floor-wall seam.
    private const float WallSpriteHalfHeightWorld = 1.5f;

    /// <summary>
    /// Sorts exclusion centers deterministically by (x, y) so Host and Clients always produce identical
    /// placement indices regardless of Unity's FindObjectsByType return order.
    /// </summary>
    public static void SortExclusionCenters(List<Vector2> centers)
    {
        if (centers == null || centers.Count <= 1)
        {
            return;
        }

        centers.Sort((a, b) =>
        {
            int cmpX = a.x.CompareTo(b.x);
            return cmpX != 0 ? cmpX : a.y.CompareTo(b.y);
        });
    }

    public static void GeneratePlacements(
        ICollapseSurfaceGrid grid,
        CollapseRiftPlacementSettings settings,
        IReadOnlyList<Vector2> floorExclusionCenters,
        IReadOnlyList<Vector2> wallExclusionCenters,
        int seed,
        List<CollapseRiftPlacement> floorResults,
        List<CollapseRiftPlacement> wallResults)
    {
        floorResults?.Clear();
        wallResults?.Clear();

        if (grid == null)
        {
            return;
        }

        BoundsInt bounds = grid.CellBounds;
        if (bounds.size.x <= 0 || bounds.size.y <= 0)
        {
            return;
        }

        var rng = new System.Random(seed);

        int totalFloorRequested = settings.Stage2FloorCount + settings.Stage3FloorCount;
        if (floorResults != null && totalFloorRequested > 0)
        {
            var floorCandidates = new List<Vector2Int>(256);
            CollectValidFloorCandidates(grid, bounds, settings.FloorMarginCells, floorCandidates);
            Shuffle(floorCandidates, rng);
            SelectFloorPlacements(grid, settings, floorCandidates, floorExclusionCenters, rng, floorResults);
        }

        if (wallResults != null && settings.WallCount > 0)
        {
            var wallCandidates = new List<Vector2Int>(128);
            CollectValidWallCandidates(grid, bounds, settings, wallCandidates);
            Shuffle(wallCandidates, rng);
            SelectWallPlacements(grid, settings, wallCandidates, wallExclusionCenters, rng, wallResults);
        }
    }

    private static void CollectValidFloorCandidates(
        ICollapseSurfaceGrid grid,
        BoundsInt bounds,
        int marginCells,
        List<Vector2Int> candidates)
    {
        int minX = bounds.xMin + marginCells;
        int maxX = bounds.xMax - 1 - marginCells;
        int minY = bounds.yMin + marginCells;
        int maxY = bounds.yMax - 1 - marginCells;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (IsValidFloorCell(grid, x, y, marginCells))
                {
                    candidates.Add(new Vector2Int(x, y));
                }
            }
        }
    }

    private static bool IsValidFloorCell(ICollapseSurfaceGrid grid, int cx, int cy, int marginCells)
    {
        for (int dy = -marginCells; dy <= marginCells; dy++)
        {
            for (int dx = -marginCells; dx <= marginCells; dx++)
            {
                var check = new Vector2Int(cx + dx, cy + dy);
                if (!grid.HasFloorTile(check) || grid.HasBlockingFloorOverlay(check))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void SelectFloorPlacements(
        ICollapseSurfaceGrid grid,
        CollapseRiftPlacementSettings settings,
        List<Vector2Int> shuffledCandidates,
        IReadOnlyList<Vector2> exclusionCenters,
        System.Random rng,
        List<CollapseRiftPlacement> results)
    {
        int totalRequested = settings.Stage2FloorCount + settings.Stage3FloorCount;
        float minSpacingSq = settings.MinFloorSpacing * settings.MinFloorSpacing;
        float exclusionSq = settings.SanctuaryExclusionRadius * settings.SanctuaryExclusionRadius;

        for (int i = 0; i < shuffledCandidates.Count && results.Count < totalRequested; i++)
        {
            Vector2Int cell = shuffledCandidates[i];
            Vector2 worldPos = grid.CellToWorldCenter(cell);

            if (IsTooCloseToAny(worldPos, exclusionCenters, exclusionSq))
            {
                continue;
            }

            if (IsTooCloseToPlacements(worldPos, results, minSpacingSq))
            {
                continue;
            }

            CollapseRiftBatch batch = results.Count < settings.Stage2FloorCount
                ? CollapseRiftBatch.Stage2Initial
                : CollapseRiftBatch.Stage3Secondary;

            float phaseOffset = (float)rng.NextDouble() * 3.0f;
            results.Add(new CollapseRiftPlacement(worldPos, cell, CollapseRiftSurfaceType.Floor, batch, phaseOffset));
        }
    }

    private static void CollectValidWallCandidates(
        ICollapseSurfaceGrid grid,
        BoundsInt bounds,
        CollapseRiftPlacementSettings settings,
        List<Vector2Int> candidates)
    {
        int minX = bounds.xMin + settings.WallHorizontalMarginCells;
        int maxX = bounds.xMax - settings.WallWidthCells - settings.WallHorizontalMarginCells;
        int minY = bounds.yMin + 1; // Need y - 1 to check walkable floor in front of wall
        int maxY = bounds.yMax - settings.WallFaceHeightCells - settings.WallTopMarginCells;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (IsValidSouthFacingWallAnchor(grid, x, y, settings))
                {
                    candidates.Add(new Vector2Int(x, y));
                }
            }
        }
    }

    private static bool IsValidSouthFacingWallAnchor(
        ICollapseSurfaceGrid grid,
        int baseX,
        int baseY,
        CollapseRiftPlacementSettings settings)
    {
        int startCol = baseX - settings.WallHorizontalMarginCells;
        int endCol = baseX + settings.WallWidthCells - 1 + settings.WallHorizontalMarginCells;
        int totalHeight = settings.WallFaceHeightCells + settings.WallTopMarginCells;

        for (int cx = startCol; cx <= endCol; cx++)
        {
            // Must have walkable open floor directly in front (south) so this is a visible vertical wall face.
            var southCell = new Vector2Int(cx, baseY - 1);
            if (!grid.HasFloorTile(southCell) || grid.HasBlockingFloorOverlay(southCell))
            {
                return false;
            }

            // Must have solid wall tiles spanning the full vertical face height plus top margin.
            for (int dy = 0; dy < totalHeight; dy++)
            {
                var wallCell = new Vector2Int(cx, baseY + dy);
                if (!grid.HasWallTile(wallCell))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void SelectWallPlacements(
        ICollapseSurfaceGrid grid,
        CollapseRiftPlacementSettings settings,
        List<Vector2Int> shuffledCandidates,
        IReadOnlyList<Vector2> exclusionCenters,
        System.Random rng,
        List<CollapseRiftPlacement> results)
    {
        float minSpacingSq = settings.MinWallSpacing * settings.MinWallSpacing;
        float exclusionSq = settings.WallPropExclusionRadius * settings.WallPropExclusionRadius;

        for (int i = 0; i < shuffledCandidates.Count && results.Count < settings.WallCount; i++)
        {
            Vector2Int cell = shuffledCandidates[i];
            Vector2 leftBottom = grid.CellToWorldBottomLeft(cell);
            Vector2 rightBottom = grid.CellToWorldBottomLeft(new Vector2Int(cell.x + settings.WallWidthCells, cell.y));
            var worldPos = new Vector2((leftBottom.x + rightBottom.x) * 0.5f, leftBottom.y + WallSpriteHalfHeightWorld);

            if (IsTooCloseToAny(worldPos, exclusionCenters, exclusionSq))
            {
                continue;
            }

            if (IsTooCloseToPlacements(worldPos, results, minSpacingSq))
            {
                continue;
            }

            float phaseOffset = (float)rng.NextDouble() * 3.0f;
            results.Add(new CollapseRiftPlacement(worldPos, cell, CollapseRiftSurfaceType.Wall, CollapseRiftBatch.Stage2Initial, phaseOffset));
        }
    }

    private static bool IsTooCloseToAny(Vector2 point, IReadOnlyList<Vector2> centers, float minDistSq)
    {
        if (centers == null || minDistSq <= 0f)
        {
            return false;
        }

        for (int i = 0; i < centers.Count; i++)
        {
            if ((point - centers[i]).sqrMagnitude < minDistSq)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTooCloseToPlacements(Vector2 point, List<CollapseRiftPlacement> existing, float minDistSq)
    {
        if (minDistSq <= 0f)
        {
            return false;
        }

        for (int i = 0; i < existing.Count; i++)
        {
            if ((point - existing[i].WorldPosition).sqrMagnitude < minDistSq)
            {
                return true;
            }
        }

        return false;
    }

    private static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Runtime <see cref="ICollapseSurfaceGrid"/> backed by scene Tilemaps (CatacombsMap).
/// Separates walkable floor tilemaps, vertical wall-face tilemaps, and blocking overlays (walls, obstacles, stairs/access).
/// </summary>
public sealed class TilemapCollapseSurfaceGrid : ICollapseSurfaceGrid
{
    private readonly Tilemap[] _floorTilemaps;
    private readonly Tilemap[] _wallFaceTilemaps;
    private readonly Tilemap[] _blockingOverlayTilemaps;
    private readonly Tilemap _referenceTilemap;
    private readonly BoundsInt _cellBounds;

    public BoundsInt CellBounds => _cellBounds;

    public TilemapCollapseSurfaceGrid(
        Tilemap[] floorTilemaps,
        Tilemap[] wallFaceTilemaps,
        Tilemap[] blockingOverlayTilemaps)
    {
        _floorTilemaps = floorTilemaps ?? System.Array.Empty<Tilemap>();
        _wallFaceTilemaps = wallFaceTilemaps ?? System.Array.Empty<Tilemap>();
        _blockingOverlayTilemaps = blockingOverlayTilemaps ?? System.Array.Empty<Tilemap>();
        _referenceTilemap = ResolveReferenceTilemap();
        _cellBounds = ComputeCombinedBounds();
    }

    public static TilemapCollapseSurfaceGrid BuildFromTilemaps(Tilemap[] allTilemaps)
    {
        var floors = new List<Tilemap>();
        var wallFaces = new List<Tilemap>();
        var blockingOverlays = new List<Tilemap>();

        if (allTilemaps != null)
        {
            for (int i = 0; i < allTilemaps.Length; i++)
            {
                Tilemap tm = allTilemaps[i];
                if (tm == null || !tm.gameObject.activeInHierarchy)
                {
                    continue;
                }

                string lower = tm.name.ToLowerInvariant();

                // Stairs / access ramps / obstacles / interior walls block floor rifts
                if (lower.Contains("access") || lower.Contains("stair") || lower.Contains("obstacle") || lower.Contains("collision"))
                {
                    blockingOverlays.Add(tm);
                    continue;
                }

                if (lower.Contains("wall"))
                {
                    blockingOverlays.Add(tm);
                    // Only primary structural walls (exclude decorative/interior filler INS_Walls and elevated P1_Walls overlays)
                    if (!lower.Contains("ins_") && !lower.Contains("p1_"))
                    {
                        wallFaces.Add(tm);
                    }
                    continue;
                }

                if (lower.Contains("floor") || lower.Contains("ground"))
                {
                    floors.Add(tm);
                }
            }
        }

        return new TilemapCollapseSurfaceGrid(floors.ToArray(), wallFaces.ToArray(), blockingOverlays.ToArray());
    }

    public bool HasFloorTile(Vector2Int cell)
    {
        var pos = new Vector3Int(cell.x, cell.y, 0);
        for (int i = 0; i < _floorTilemaps.Length; i++)
        {
            Tilemap tm = _floorTilemaps[i];
            if (tm != null && tm.HasTile(pos))
            {
                return true;
            }
        }
        return false;
    }

    public bool HasWallTile(Vector2Int cell)
    {
        var pos = new Vector3Int(cell.x, cell.y, 0);
        for (int i = 0; i < _wallFaceTilemaps.Length; i++)
        {
            Tilemap tm = _wallFaceTilemaps[i];
            if (tm != null && tm.HasTile(pos))
            {
                return true;
            }
        }
        return false;
    }

    public bool HasBlockingFloorOverlay(Vector2Int cell)
    {
        var pos = new Vector3Int(cell.x, cell.y, 0);
        for (int i = 0; i < _blockingOverlayTilemaps.Length; i++)
        {
            Tilemap tm = _blockingOverlayTilemaps[i];
            if (tm != null && tm.HasTile(pos))
            {
                return true;
            }
        }
        return false;
    }

    public Vector2 CellToWorldCenter(Vector2Int cell)
    {
        if (_referenceTilemap == null)
        {
            return new Vector2(cell.x + 0.5f, cell.y + 0.5f);
        }

        Vector3 world = _referenceTilemap.GetCellCenterWorld(new Vector3Int(cell.x, cell.y, 0));
        return new Vector2(world.x, world.y);
    }

    public Vector2 CellToWorldBottomLeft(Vector2Int cell)
    {
        if (_referenceTilemap == null)
        {
            return new Vector2(cell.x, cell.y);
        }

        Vector3 world = _referenceTilemap.CellToWorld(new Vector3Int(cell.x, cell.y, 0));
        return new Vector2(world.x, world.y);
    }

    private Tilemap ResolveReferenceTilemap()
    {
        if (_floorTilemaps.Length > 0 && _floorTilemaps[0] != null) return _floorTilemaps[0];
        if (_wallFaceTilemaps.Length > 0 && _wallFaceTilemaps[0] != null) return _wallFaceTilemaps[0];
        if (_blockingOverlayTilemaps.Length > 0 && _blockingOverlayTilemaps[0] != null) return _blockingOverlayTilemaps[0];
        return null;
    }

    private BoundsInt ComputeCombinedBounds()
    {
        bool hasAny = false;
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;

        Accumulate(_floorTilemaps, ref hasAny, ref minX, ref minY, ref maxX, ref maxY);
        Accumulate(_wallFaceTilemaps, ref hasAny, ref minX, ref minY, ref maxX, ref maxY);

        if (!hasAny)
        {
            return new BoundsInt(0, 0, 0, 0, 0, 1);
        }

        return new BoundsInt(minX, minY, 0, maxX - minX, maxY - minY, 1);
    }

    private static void Accumulate(
        Tilemap[] tilemaps,
        ref bool hasAny,
        ref int minX,
        ref int minY,
        ref int maxX,
        ref int maxY)
    {
        for (int i = 0; i < tilemaps.Length; i++)
        {
            Tilemap tm = tilemaps[i];
            if (tm == null) continue;

            BoundsInt b = tm.cellBounds;
            if (b.size.x <= 0 || b.size.y <= 0) continue;

            if (!hasAny)
            {
                minX = b.xMin;
                minY = b.yMin;
                maxX = b.xMax;
                maxY = b.yMax;
                hasAny = true;
            }
            else
            {
                if (b.xMin < minX) minX = b.xMin;
                if (b.yMin < minY) minY = b.yMin;
                if (b.xMax > maxX) maxX = b.xMax;
                if (b.yMax > maxY) maxY = b.yMax;
            }
        }
    }
}

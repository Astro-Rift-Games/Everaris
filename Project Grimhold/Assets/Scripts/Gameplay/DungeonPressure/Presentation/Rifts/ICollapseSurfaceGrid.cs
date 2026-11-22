using UnityEngine;

/// <summary>
/// Read-only grid abstraction over dungeon tilemaps for deterministic rift surface validation.
/// </summary>
public interface ICollapseSurfaceGrid
{
    BoundsInt CellBounds { get; }
    bool HasFloorTile(Vector2Int cell);
    bool HasWallTile(Vector2Int cell);
    bool HasBlockingFloorOverlay(Vector2Int cell);
    Vector2 CellToWorldCenter(Vector2Int cell);
    Vector2 CellToWorldBottomLeft(Vector2Int cell);
}

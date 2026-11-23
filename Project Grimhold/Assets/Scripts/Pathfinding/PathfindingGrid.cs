using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Builds and exposes a 2D grid of <see cref="PathNode"/> values representing navigable space
/// for enemy pathfinding, plus a single shared <see cref="AStarPathSolver"/> owned by the grid.
///
/// Walkability is determined by <c>Physics2D.OverlapCircle</c> with
/// <see cref="PathfindingGridConfig.AgentRadius"/> (aligned to the enemy's real CircleCollider2D
/// plus safety margin) and nodes snapped to multiples of <see cref="PathfindingGridConfig.NodeSize"/>
/// so 1-tile corridors always contain centered walkable nodes.
///
/// Runtime map updates (such as <see cref="NetworkDoor"/> toggling) call <see cref="RefreshRegion"/>,
/// which increments <see cref="TopologyVersion"/> and records dirty bounds so navigators invalidate
/// affected paths on the next tick.
/// </summary>
[DisallowMultipleComponent]
public sealed class PathfindingGrid : MonoBehaviour
{
    private const int DirtyBoundsHistoryCapacity = 16;

    [SerializeField]
    private PathfindingGridConfig _config;

    // Flat row-major array: index = y * _width + x.
    private PathNode[] _nodes;
    private int _width;
    private int _height;
    private Vector2 _worldBottomLeft;
    private bool _isBuilt;

    // Shared solver instance owned by the grid (O(1) memory regardless of enemy count).
    private AStarPathSolver _sharedSolver;

    // Scene/grid-scoped crowd separation and surround-slot coordinator (TASK-318).
    private readonly EnemyCrowdCoordinator _crowdCoordinator = new EnemyCrowdCoordinator();

    // Monotonic topology change tracking for dynamic obstacles (doors).
    private int _topologyVersion;
    private readonly Bounds[] _dirtyBoundsRing = new Bounds[DirtyBoundsHistoryCapacity];
    private readonly int[] _dirtyVersionsRing = new int[DirtyBoundsHistoryCapacity];
    private int _dirtyWriteCursor;

#if UNITY_EDITOR
    [Header("Debug")]
    [SerializeField]
    private bool _showGizmos;
#endif

    /// <summary>Whether the grid has been built and is ready for queries.</summary>
    public bool IsBuilt => _isBuilt;

    /// <summary>Number of columns in the grid.</summary>
    public int Width => _width;

    /// <summary>Number of rows in the grid.</summary>
    public int Height => _height;

    /// <summary>Active grid configuration asset.</summary>
    public PathfindingGridConfig Config => _config;

    /// <summary>Shared stateless A* solver sized for this grid.</summary>
    public AStarPathSolver SharedSolver => _sharedSolver;

    /// <summary>Grid-scoped enemy crowd separation and surround coordinator (Host only).</summary>
    public EnemyCrowdCoordinator CrowdCoordinator => _crowdCoordinator;

    private void OnDestroy()
    {
        _crowdCoordinator.Clear();
    }

    /// <summary>
    /// Incremented whenever <see cref="RefreshRegion"/> flips at least one node's walkability
    /// or when <see cref="Build"/> constructs the grid.
    /// </summary>
    public int TopologyVersion => _topologyVersion;

    /// <summary>
    /// Constructs the walkability grid by scanning all active Tilemaps in loaded scenes and
    /// querying <c>Physics2D.OverlapCircle</c> at each node centre.
    /// </summary>
    public void Build()
    {
        if (_config == null)
        {
            Debug.LogError(
                $"{nameof(PathfindingGrid)} cannot build: {nameof(PathfindingGridConfig)} is not assigned.",
                this);
            return;
        }

        Bounds bounds = CalculateSceneTilemapBounds();
        if (bounds.size.sqrMagnitude < 0.001f)
        {
            Debug.LogError(
                $"{nameof(PathfindingGrid)} cannot build: no active Tilemaps found in the scene " +
                "or their bounds are empty.",
                this);
            return;
        }

        BuildFromBounds(bounds);
    }

    public PathNode GetNode(int x, int y)
    {
        return _nodes[y * _width + x];
    }

    internal void SetNode(int x, int y, in PathNode node)
    {
        _nodes[y * _width + x] = node;
    }

    public Vector2 GetWorldPosition(int x, int y)
    {
        return _worldBottomLeft + new Vector2(x * _config.NodeSize, y * _config.NodeSize);
    }

    public bool TryGetNodeFromWorldPoint(Vector2 worldPos, out int x, out int y)
    {
        float relX = worldPos.x - _worldBottomLeft.x;
        float relY = worldPos.y - _worldBottomLeft.y;
        x = Mathf.RoundToInt(relX / _config.NodeSize);
        y = Mathf.RoundToInt(relY / _config.NodeSize);
        return IsInBounds(x, y);
    }

    public bool TryGetNearestWalkableNode(Vector2 worldPos, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!_isBuilt)
        {
            return false;
        }

        TryGetNodeFromWorldPoint(worldPos, out int cx, out int cy);
        cx = Mathf.Clamp(cx, 0, _width - 1);
        cy = Mathf.Clamp(cy, 0, _height - 1);

        if (_nodes[cy * _width + cx].IsWalkable)
        {
            x = cx;
            y = cy;
            return true;
        }

        int maxRadius = Mathf.Max(_width, _height);
        for (int r = 1; r <= maxRadius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (!IsInBounds(nx, ny)) continue;
                    if (_nodes[ny * _width + nx].IsWalkable)
                    {
                        x = nx;
                        y = ny;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Re-evaluates walkability of every node whose clearance circle can overlap
    /// <paramref name="worldBounds"/>. Increments <see cref="TopologyVersion"/> when any
    /// node changes state so navigators invalidate affected paths on the next tick.
    /// </summary>
    public void RefreshRegion(Bounds worldBounds)
    {
        if (!_isBuilt || _config == null)
        {
            return;
        }

        float nodeSize = _config.NodeSize;
        float clearanceRadius = _config.AgentRadius;
        LayerMask obstacleLayer = _config.ObstacleLayer;

        int minX = Mathf.Max(0, Mathf.FloorToInt((worldBounds.min.x - clearanceRadius - _worldBottomLeft.x) / nodeSize));
        int minY = Mathf.Max(0, Mathf.FloorToInt((worldBounds.min.y - clearanceRadius - _worldBottomLeft.y) / nodeSize));
        int maxX = Mathf.Min(_width - 1, Mathf.CeilToInt((worldBounds.max.x + clearanceRadius - _worldBottomLeft.x) / nodeSize));
        int maxY = Mathf.Min(_height - 1, Mathf.CeilToInt((worldBounds.max.y + clearanceRadius - _worldBottomLeft.y) / nodeSize));

        bool anyChanged = false;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int index = y * _width + x;
                bool walkable = !IsObstacleAt(_nodes[index].WorldPosition, clearanceRadius, obstacleLayer);
                if (_nodes[index].IsWalkable != walkable)
                {
                    _nodes[index].IsWalkable = walkable;
                    anyChanged = true;
                }
            }
        }

        if (anyChanged)
        {
            _topologyVersion++;
            Bounds expanded = worldBounds;
            expanded.Expand((clearanceRadius + nodeSize) * 2f);
            _dirtyBoundsRing[_dirtyWriteCursor] = expanded;
            _dirtyVersionsRing[_dirtyWriteCursor] = _topologyVersion;
            _dirtyWriteCursor = (_dirtyWriteCursor + 1) % DirtyBoundsHistoryCapacity;
        }
    }

    /// <summary>
    /// Returns true if any topology change newer than <paramref name="sinceVersion"/> may affect
    /// the given world-space segment (or if more changes occurred than the ring buffer tracks).
    /// </summary>
    public bool HasTopologyChangedNearSegment(int sinceVersion, Vector2 a, Vector2 b)
    {
        if (sinceVersion >= _topologyVersion)
        {
            return false;
        }

        if (_topologyVersion - sinceVersion > DirtyBoundsHistoryCapacity)
        {
            return true;
        }

        float segMinX = Mathf.Min(a.x, b.x);
        float segMaxX = Mathf.Max(a.x, b.x);
        float segMinY = Mathf.Min(a.y, b.y);
        float segMaxY = Mathf.Max(a.y, b.y);

        for (int i = 0; i < DirtyBoundsHistoryCapacity; i++)
        {
            if (_dirtyVersionsRing[i] <= sinceVersion)
            {
                continue;
            }

            Bounds dirty = _dirtyBoundsRing[i];
            if (segMaxX >= dirty.min.x && segMinX <= dirty.max.x &&
                segMaxY >= dirty.min.y && segMinY <= dirty.max.y)
            {
                return true;
            }
        }

        return false;
    }

    public bool IsWalkable(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        return _nodes[y * _width + x].IsWalkable;
    }

    public bool IsWalkableWorldPosition(Vector2 worldPos)
    {
        if (!_isBuilt || !TryGetNodeFromWorldPoint(worldPos, out int x, out int y))
        {
            return false;
        }

        return _nodes[y * _width + x].IsWalkable;
    }

    public bool IsInBounds(int x, int y)
    {
        return x >= 0 && x < _width && y >= 0 && y < _height;
    }

    public bool IsValidDiagonalMove(int fromX, int fromY, int dx, int dy)
    {
        return IsWalkable(fromX + dx, fromY) && IsWalkable(fromX, fromY + dy);
    }

    private void BuildFromBounds(Bounds bounds)
    {
        float nodeSize = _config.NodeSize;
        float clearanceRadius = _config.AgentRadius;
        LayerMask obstacleLayer = _config.ObstacleLayer;

        // Align grid origin to multiples of nodeSize so 1-unit tile corridors always have
        // nodes along their exact centre lines (k + 0.5) as well as tile boundaries (k + 0.0).
        float minX = Mathf.Ceil((bounds.min.x - 0.0001f) / nodeSize) * nodeSize;
        float minY = Mathf.Ceil((bounds.min.y - 0.0001f) / nodeSize) * nodeSize;
        float maxX = Mathf.Floor((bounds.max.x + 0.0001f) / nodeSize) * nodeSize;
        float maxY = Mathf.Floor((bounds.max.y + 0.0001f) / nodeSize) * nodeSize;

        _width = Mathf.Max(1, Mathf.RoundToInt((maxX - minX) / nodeSize) + 1);
        _height = Mathf.Max(1, Mathf.RoundToInt((maxY - minY) / nodeSize) + 1);
        _worldBottomLeft = new Vector2(minX, minY);

        _nodes = new PathNode[_width * _height];

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                Vector2 worldPos = _worldBottomLeft + new Vector2(x * nodeSize, y * nodeSize);
                bool isObstacle = IsObstacleAt(worldPos, clearanceRadius, obstacleLayer);

                _nodes[y * _width + x] = new PathNode
                {
                    X = x,
                    Y = y,
                    WorldPosition = worldPos,
                    IsWalkable = !isObstacle,
                };
            }
        }

        _sharedSolver = new AStarPathSolver(_width, _height, _config);
        _topologyVersion++;
        _dirtyWriteCursor = 0;
        for (int i = 0; i < DirtyBoundsHistoryCapacity; i++)
        {
            _dirtyVersionsRing[i] = 0;
        }

        _isBuilt = true;
        Debug.Log(
            $"[{nameof(PathfindingGrid)}] Built {_width}x{_height} grid " +
            $"({_width * _height} nodes, clearance={clearanceRadius:F2}) from bounds {bounds}.",
            this);
    }

    private static bool IsObstacleAt(Vector2 worldPos, float clearanceRadius, LayerMask obstacleLayer)
    {
        return Physics2D.OverlapCircle(worldPos, clearanceRadius, obstacleLayer) != null;
    }

    private static Bounds CalculateSceneTilemapBounds()
    {
        Tilemap[] tilemaps = FindObjectsByType<Tilemap>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        if (tilemaps == null || tilemaps.Length == 0)
        {
            return default;
        }

        bool initialised = false;
        Bounds combined = default;

        foreach (Tilemap tilemap in tilemaps)
        {
            if (tilemap == null) continue;

            tilemap.CompressBounds();
            Bounds local = tilemap.localBounds;
            if (local.size.sqrMagnitude < 0.001f) continue;

            Bounds world = new Bounds(
                tilemap.transform.TransformPoint(local.center),
                Vector3.Scale(local.size, tilemap.transform.lossyScale));

            if (!initialised)
            {
                combined = world;
                initialised = true;
            }
            else
            {
                combined.Encapsulate(world);
            }
        }

        return combined;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!_showGizmos || !_isBuilt || _nodes == null) return;

        float nodeSize = _config != null ? _config.NodeSize : 0.5f;
        Vector3 cubeSize = new Vector3(nodeSize * 0.9f, nodeSize * 0.9f, 0.1f);

        Color walkableColor = new Color(0f, 1f, 0f, 0.15f);
        Color obstacleColor = new Color(1f, 0f, 0f, 0.3f);

        foreach (PathNode node in _nodes)
        {
            Gizmos.color = node.IsWalkable ? walkableColor : obstacleColor;
            Gizmos.DrawCube(new Vector3(node.WorldPosition.x, node.WorldPosition.y, 0f), cubeSize);
        }
    }
#endif
}

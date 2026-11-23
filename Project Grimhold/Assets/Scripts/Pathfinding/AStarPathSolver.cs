using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Outcome status of an A* path search.
/// </summary>
public enum AStarPathStatus
{
    NoPath = 0,
    Complete = 1,
    Partial = 2
}

/// <summary>
/// Stateless, allocation-free A* path solver over a <see cref="PathfindingGrid"/>.
///
/// Designed to be owned as a single shared instance by <see cref="PathfindingGrid"/> so memory
/// does not scale with enemy count and no heap allocations occur during simulation ticks.
/// Uses an indexed <see cref="BinaryMinHeap"/> for optimal minimum-cost paths and bounded O(n)
/// CircleCast smoothing that validates every segment including the final endpoint.
/// </summary>
public sealed class AStarPathSolver
{
    private const int CostStraight = 10;
    private const int CostDiagonal = 14; // approx sqrt(2) * 10

    private readonly PathfindingGridConfig _config;
    private readonly LayerMask _obstacleLayer;
    private readonly PathNode[] _nodeData;
    private readonly BinaryMinHeap _openHeap;
    private readonly List<int> _visitedIndices = new List<int>(256);

    /// <summary>True when the most recent search terminated early due to MaxPathIterations.</summary>
    public bool LastSearchHitIterationLimit { get; private set; }

    public AStarPathSolver(int gridWidth, int gridHeight, PathfindingGridConfig config)
    {
        _config = config;
        _obstacleLayer = config.ObstacleLayer;

        int nodeCount = Mathf.Max(1, gridWidth * gridHeight);
        _nodeData = new PathNode[nodeCount];
        _openHeap = new BinaryMinHeap(nodeCount);
    }

    /// <summary>
    /// Searches for a path from <paramref name="startWorld"/> to <paramref name="endWorld"/>.
    /// Returns the number of waypoints written (>0 on success, 0 on failure).
    /// </summary>
    public int FindPath(
        PathfindingGrid grid,
        Vector2 startWorld,
        Vector2 endWorld,
        bool enableSmoothing,
        float pathSmoothingRadius,
        List<Vector2> outputWaypoints,
        bool allowPartialPath = false)
    {
        AStarPathStatus status = FindPathWithStatus(
            grid,
            startWorld,
            endWorld,
            enableSmoothing,
            pathSmoothingRadius,
            outputWaypoints,
            allowPartialPath,
            logIterationWarning: true);

        return status == AStarPathStatus.NoPath ? 0 : outputWaypoints.Count;
    }

    /// <summary>
    /// Searches for a path and reports whether the result is Complete, Partial (closest reachable
    /// node when <paramref name="allowPartialPath"/> is true), or NoPath.
    /// </summary>
    public AStarPathStatus FindPathWithStatus(
        PathfindingGrid grid,
        Vector2 startWorld,
        Vector2 endWorld,
        bool enableSmoothing,
        float pathSmoothingRadius,
        List<Vector2> outputWaypoints,
        bool allowPartialPath = false,
        bool logIterationWarning = false)
    {
        outputWaypoints.Clear();
        LastSearchHitIterationLimit = false;

        if (grid == null || !grid.IsBuilt)
        {
            return AStarPathStatus.NoPath;
        }

        if (!grid.TryGetNearestWalkableNode(startWorld, out int sx, out int sy) ||
            !grid.TryGetNearestWalkableNode(endWorld, out int ex, out int ey))
        {
            return AStarPathStatus.NoPath;
        }

        if (sx == ex && sy == ey)
        {
            Vector2 goalNodePos = grid.GetWorldPosition(ex, ey);
            Vector2 finalPoint = HasDirectPath(startWorld, endWorld, pathSmoothingRadius)
                ? endWorld
                : goalNodePos;
            outputWaypoints.Add(finalPoint);
            return AStarPathStatus.Complete;
        }

        ResetVisitedNodes();
        _openHeap.Clear();

        int width = grid.Width;
        int startIndex = FlatIndex(sx, sy, width);
        PathNode startNode = grid.GetNode(sx, sy);
        startNode.GCost = 0;
        startNode.HCost = Heuristic(sx, sy, ex, ey);
        startNode.InOpenSet = true;
        _nodeData[startIndex] = startNode;
        Track(startIndex);

        _openHeap.PushOrDecreaseKey(startNode.FCost, startIndex, sx, sy);

        int bestX = sx;
        int bestY = sy;
        int bestHCost = startNode.HCost;
        int bestGCost = 0;

        int iterations = 0;
        int maxIterations = _config.MaxPathIterations;

        while (_openHeap.Count > 0 && iterations < maxIterations)
        {
            iterations++;

            (_, int cx, int cy) = _openHeap.Pop();
            int currentIndex = FlatIndex(cx, cy, width);

            ref PathNode current = ref _nodeData[currentIndex];
            if (current.InClosedSet) continue;
            current.InClosedSet = true;
            current.InOpenSet = false;

            if (current.HCost < bestHCost || (current.HCost == bestHCost && current.GCost < bestGCost))
            {
                bestX = cx;
                bestY = cy;
                bestHCost = current.HCost;
                bestGCost = current.GCost;
            }

            if (cx == ex && cy == ey)
            {
                RetracePath(grid, sx, sy, ex, ey, outputWaypoints);
                FinalizeWaypoints(outputWaypoints, enableSmoothing, pathSmoothingRadius, endWorld);
                return AStarPathStatus.Complete;
            }

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (!grid.IsWalkable(nx, ny)) continue;

                    bool isDiagonal = dx != 0 && dy != 0;
                    if (isDiagonal && !grid.IsValidDiagonalMove(cx, cy, dx, dy)) continue;

                    int neighbourIndex = FlatIndex(nx, ny, width);
                    ref PathNode neighbour = ref _nodeData[neighbourIndex];

                    if (neighbour.InClosedSet) continue;

                    if (!neighbour.InOpenSet)
                    {
                        PathNode original = grid.GetNode(nx, ny);
                        neighbour.X = original.X;
                        neighbour.Y = original.Y;
                        neighbour.WorldPosition = original.WorldPosition;
                        neighbour.IsWalkable = original.IsWalkable;
                        Track(neighbourIndex);
                    }

                    int tentativeG = current.GCost + (isDiagonal ? CostDiagonal : CostStraight);
                    if (!neighbour.InOpenSet || tentativeG < neighbour.GCost)
                    {
                        neighbour.GCost = tentativeG;
                        neighbour.HCost = Heuristic(nx, ny, ex, ey);
                        neighbour.ParentX = cx;
                        neighbour.ParentY = cy;
                        neighbour.InOpenSet = true;

                        _openHeap.PushOrDecreaseKey(neighbour.FCost, neighbourIndex, nx, ny);
                    }
                }
            }
        }

        // Only report iteration limit exhaustion when unexplored open nodes remain.
        bool hitLimit = _openHeap.Count > 0 && iterations >= maxIterations;
        LastSearchHitIterationLimit = hitLimit;
        if (hitLimit && logIterationWarning)
        {
            Debug.LogWarning(
                $"[{nameof(AStarPathSolver)}] Path search exceeded {maxIterations} iterations " +
                $"(start={startWorld}, end={endWorld}).",
                null);
        }

        if (allowPartialPath && (bestX != sx || bestY != sy))
        {
            RetracePath(grid, sx, sy, bestX, bestY, outputWaypoints);
            Vector2 bestWorld = grid.GetWorldPosition(bestX, bestY);
            FinalizeWaypoints(outputWaypoints, enableSmoothing, pathSmoothingRadius, bestWorld);
            return outputWaypoints.Count > 0 ? AStarPathStatus.Partial : AStarPathStatus.NoPath;
        }

        return AStarPathStatus.NoPath;
    }

    private static int Heuristic(int ax, int ay, int bx, int by)
    {
        int dx = Mathf.Abs(ax - bx);
        int dy = Mathf.Abs(ay - by);
        return CostStraight * (dx + dy) + (CostDiagonal - 2 * CostStraight) * Mathf.Min(dx, dy);
    }

    private static int FlatIndex(int x, int y, int width) => y * width + x;

    private void Track(int index)
    {
        _visitedIndices.Add(index);
    }

    private void ResetVisitedNodes()
    {
        for (int i = 0; i < _visitedIndices.Count; i++)
        {
            int index = _visitedIndices[i];
            _nodeData[index].GCost = 0;
            _nodeData[index].HCost = 0;
            _nodeData[index].ParentX = 0;
            _nodeData[index].ParentY = 0;
            _nodeData[index].InOpenSet = false;
            _nodeData[index].InClosedSet = false;
        }
        _visitedIndices.Clear();
    }

    private void RetracePath(
        PathfindingGrid grid,
        int sx, int sy,
        int ex, int ey,
        List<Vector2> output)
    {
        int cx = ex;
        int cy = ey;
        int width = grid.Width;

        while (cx != sx || cy != sy)
        {
            output.Add(grid.GetWorldPosition(cx, cy));
            int idx = FlatIndex(cx, cy, width);
            int px = _nodeData[idx].ParentX;
            int py = _nodeData[idx].ParentY;
            cx = px;
            cy = py;
        }

        output.Add(grid.GetWorldPosition(sx, sy));
        output.Reverse();
    }

    private void FinalizeWaypoints(
        List<Vector2> waypoints,
        bool enableSmoothing,
        float pathSmoothingRadius,
        Vector2 endWorld)
    {
        if (waypoints.Count == 0)
        {
            return;
        }

        if (enableSmoothing)
        {
            SmoothPath(waypoints, pathSmoothingRadius, endWorld);
            return;
        }

        // Preserve exact endWorld only when directly reachable from the preceding waypoint without clipping walls.
        int lastIndex = waypoints.Count - 1;
        Vector2 anchor = lastIndex > 0 ? waypoints[lastIndex - 1] : waypoints[0];
        if (HasDirectPath(anchor, endWorld, pathSmoothingRadius))
        {
            waypoints[lastIndex] = endWorld;
        }
    }

    /// <summary>
    /// Applies bounded O(n) CircleCast smoothing.
    /// Checks a fast direct-to-end shortcut first, then advances greedily along reachable nodes
    /// stopping at the first blocked cast so worst-case casts stay linear in waypoint count.
    /// Always validates the final segment to <paramref name="endWorld"/> so smoothing never cuts walls.
    /// </summary>
    private void SmoothPath(List<Vector2> waypoints, float pathSmoothingRadius, Vector2 endWorld)
    {
        int count = waypoints.Count;
        if (count == 0)
        {
            return;
        }

        Vector2 finalGridNode = waypoints[count - 1];

        if (count == 1)
        {
            if (HasDirectPath(waypoints[0], endWorld, pathSmoothingRadius))
            {
                waypoints[0] = endWorld;
            }
            return;
        }

        // Fast-path for straight unobstructed corridors: O(1) cast directly to endWorld.
        if (HasDirectPath(waypoints[0], endWorld, pathSmoothingRadius))
        {
            waypoints[1] = endWorld;
            TrimToCount(waypoints, 2);
            return;
        }

        int writeIndex = 0;
        int current = 0;
        int lastIndex = count - 1;

        while (current < lastIndex)
        {
            int furthest = current + 1;

            // Check direct shortcut to the final grid node first (O(1) finish once clear of corners).
            if (lastIndex > furthest && HasDirectPath(waypoints[current], waypoints[lastIndex], pathSmoothingRadius))
            {
                furthest = lastIndex;
            }
            else
            {
                for (int lookahead = current + 2; lookahead < lastIndex; lookahead++)
                {
                    if (HasDirectPath(waypoints[current], waypoints[lookahead], pathSmoothingRadius))
                    {
                        furthest = lookahead;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            waypoints[writeIndex++] = waypoints[current];
            current = furthest;
        }

        // Validate final endpoint: never substitute endWorld if the segment clips a wall.
        Vector2 previousKept = waypoints[writeIndex - 1];
        if (HasDirectPath(previousKept, endWorld, pathSmoothingRadius))
        {
            waypoints[writeIndex++] = endWorld;
        }
        else
        {
            waypoints[writeIndex++] = finalGridNode;
            if (Vector2.SqrMagnitude(endWorld - finalGridNode) > 0.0001f &&
                HasDirectPath(finalGridNode, endWorld, pathSmoothingRadius))
            {
                if (writeIndex < waypoints.Count)
                {
                    waypoints[writeIndex++] = endWorld;
                }
                else
                {
                    waypoints.Add(endWorld);
                    writeIndex = waypoints.Count;
                }
            }
        }

        TrimToCount(waypoints, writeIndex);
    }

    private static void TrimToCount(List<Vector2> list, int targetCount)
    {
        for (int i = list.Count - 1; i >= targetCount; i--)
        {
            list.RemoveAt(i);
        }
    }

    private bool HasDirectPath(Vector2 from, Vector2 to, float pathSmoothingRadius)
    {
        Vector2 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.001f) return true;

        RaycastHit2D hit = Physics2D.CircleCast(
            from,
            pathSmoothingRadius,
            delta / distance,
            distance,
            _obstacleLayer);

        return !hit.collider;
    }
}

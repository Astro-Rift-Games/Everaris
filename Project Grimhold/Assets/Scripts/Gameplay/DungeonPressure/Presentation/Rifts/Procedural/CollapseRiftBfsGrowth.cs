using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure BFS growth-order and core/edge mask calculator for procedural collapse rifts (TASK-340).
/// Exact 8-neighbor breadth-first traversal from the rift origin (PDF proposal p.2-3).
/// </summary>
public static class CollapseRiftBfsGrowth
{
    public static byte[] ComputeGrowthOrder(bool[] mask, int size, Vector2Int origin)
    {
        int total = size * size;
        var order = new byte[total];
        if (mask == null || mask.Length != total || size <= 0)
        {
            return order;
        }

        Vector2Int seedOrigin = ResolveNearestOpaquePixel(mask, size, origin);
        if (seedOrigin.x < 0)
        {
            return order;
        }

        var dist = new int[total];
        Array.Fill(dist, -1);

        var queue = new Queue<Vector2Int>(64);
        int originIdx = seedOrigin.y * size + seedOrigin.x;
        dist[originIdx] = 0;
        queue.Enqueue(seedOrigin);

        int maxDist = 0;
        RunBfs(mask, size, dist, queue, ref maxDist);

        // Safety fallback: if any opaque pixel remained disconnected (e.g. diagonal 1px art gap),
        // seed from its Manhattan distance to origin so no opaque pixel stalls at order 0.
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = y * size + x;
                if (!mask[idx] || dist[idx] >= 0) continue;

                int fallbackDist = Math.Max(1, Math.Abs(x - seedOrigin.x) + Math.Abs(y - seedOrigin.y));
                dist[idx] = fallbackDist;
                if (fallbackDist > maxDist) maxDist = fallbackDist;
                queue.Enqueue(new Vector2Int(x, y));
                RunBfs(mask, size, dist, queue, ref maxDist);
            }
        }

        int denom = Math.Max(1, maxDist);
        for (int i = 0; i < total; i++)
        {
            order[i] = dist[i] < 0 ? (byte)0 : (byte)(dist[i] * 255 / denom);
        }

        return order;
    }

    /// <summary>
    /// Refines the interior core mask against the composed silhouette so outer 1px perimeter pixels
    /// are always classified as border (false) while interior pixels retain core (true).
    /// </summary>
    public static bool[] ComputeCoreMask(bool[] mask, bool[] authoredCore, int size)
    {
        int total = size * size;
        var core = new bool[total];
        if (mask == null || mask.Length != total || size <= 0)
        {
            return core;
        }

        bool hasAuthoredCore = false;
        if (authoredCore != null && authoredCore.Length == total)
        {
            for (int i = 0; i < total; i++)
            {
                if (mask[i] && authoredCore[i])
                {
                    hasAuthoredCore = true;
                    break;
                }
            }
        }

        int coreCount = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = y * size + x;
                if (!mask[idx]) continue;

                bool isInterior4 =
                    x > 0 && x < size - 1 &&
                    y > 0 && y < size - 1 &&
                    mask[idx - 1] &&
                    mask[idx + 1] &&
                    mask[idx - size] &&
                    mask[idx + size];

                if (hasAuthoredCore)
                {
                    core[idx] = authoredCore[idx] && isInterior4;
                }
                else
                {
                    core[idx] = isInterior4;
                }

                if (core[idx]) coreCount++;
            }
        }

        // If a very thin 1-2px piece had no strict 4-neighbor interior pixels, preserve authored core directly.
        if (coreCount == 0 && hasAuthoredCore)
        {
            for (int i = 0; i < total; i++)
            {
                core[i] = mask[i] && authoredCore[i];
            }
        }

        return core;
    }

    public static Vector2Int ResolveNearestOpaquePixel(bool[] mask, int size, Vector2Int target)
    {
        int clampedX = Mathf.Clamp(target.x, 0, size - 1);
        int clampedY = Mathf.Clamp(target.y, 0, size - 1);
        if (mask[clampedY * size + clampedX])
        {
            return new Vector2Int(clampedX, clampedY);
        }

        int bestDistSq = int.MaxValue;
        var best = new Vector2Int(-1, -1);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (!mask[y * size + x]) continue;
                int dx = x - clampedX;
                int dy = y - clampedY;
                int dSq = dx * dx + dy * dy;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    best = new Vector2Int(x, y);
                }
            }
        }

        return best;
    }

    private static void RunBfs(bool[] mask, int size, int[] dist, Queue<Vector2Int> queue, ref int maxDist)
    {
        while (queue.Count > 0)
        {
            Vector2Int p = queue.Dequeue();
            int d = dist[p.y * size + p.x];
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = p.x + dx;
                    int ny = p.y + dy;
                    if ((uint)nx >= (uint)size || (uint)ny >= (uint)size) continue;
                    int nIdx = ny * size + nx;
                    if (!mask[nIdx] || dist[nIdx] >= 0) continue;
                    int nd = d + 1;
                    dist[nIdx] = nd;
                    if (nd > maxDist) maxDist = nd;
                    queue.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }
    }
}

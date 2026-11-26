using System;
using UnityEngine;

/// <summary>
/// Pure deterministic procedural composer for floor and wall collapse rifts (TASK-340).
/// Uses <see cref="System.Random"/> exclusively (never <see cref="UnityEngine.Random"/>) so Host,
/// Clients, late-joiners, and Host Migration peers always synthesize byte-identical shapes.
/// </summary>
public static class CollapseRiftComposer
{
    public static int ComputeRiftSeed(int raidSeed, Vector2Int cell, CollapseRiftSurfaceType surfaceType)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)raidSeed) * 16777619u;
            h = (h ^ (uint)cell.x) * 16777619u;
            h = (h ^ (uint)cell.y) * 16777619u;
            h = (h ^ (uint)(int)surfaceType) * 16777619u;
            h ^= h >> 16;
            h *= 0x85ebca6bu;
            h ^= h >> 13;
            h *= 0xc2b2ae35u;
            h ^= h >> 16;
            return (int)h;
        }
    }

    /// <summary>
    /// Composes a floor rift centered inside a <paramref name="cellSize"/> x <paramref name="cellSize"/> canvas:
    /// - 1 Core in one of 8 dihedral orientations at center (BFS origin).
    /// - 2 to 3 Branches launched at evenly spread angles with controlled jitter.
    /// - Each branch chains 1-2 oriented segments, optional low-probability sub-branch, and a Tip.
    /// </summary>
    public static CollapseRiftComposedBitmap ComposeFloorRift(
        CollapseRiftRuntimeKit kit,
        int riftSeed,
        int cellSize = 48)
    {
        if (kit == null || !kit.IsValid)
        {
            kit = CollapseRiftPlaceholderKitFactory.CreateFloorKit();
        }

        int size = Mathf.Max(16, cellSize);
        var mask = new bool[size * size];
        var authoredCore = new bool[size * size];
        var rng = new System.Random(riftSeed);

        var center = new Vector2Int(size / 2, size / 2);
        CollapseRiftPieceData corePiece = kit.CoresOrBases[rng.Next(kit.CoresOrBases.Count)];
        int coreOrientation = rng.Next(8);
        StampPieceDihedral(corePiece, coreOrientation, center, alignByEntry: false, mask, authoredCore, size, size);

        float coreRadius = Mathf.Max(2.5f, Mathf.Min(corePiece.Width, corePiece.Height) * 0.42f);
        int branchCount = kit.MinFloorBranches + rng.Next(kit.MaxFloorBranches - kit.MinFloorBranches + 1);
        float baseAngle = (float)(rng.NextDouble() * 360.0);
        float sectorStep = 360f / Mathf.Max(1, branchCount);

        for (int b = 0; b < branchCount; b++)
        {
            float jitter = (float)((rng.NextDouble() * 2.0 - 1.0) * kit.AngleSpreadJitterDegrees);
            float angleDeg = NormalizeAngle(baseAngle + b * sectorStep + jitter);
            Vector2 dir = AngleToDir(angleDeg);

            Vector2Int attach = new Vector2Int(
                Mathf.RoundToInt(center.x + dir.x * coreRadius),
                Mathf.RoundToInt(center.y + dir.y * coreRadius));
            DrawBridge(center, attach, mask, authoredCore, size, size);

            // First branch segment
            CollapseRiftPieceData seg1 = kit.Branches[rng.Next(kit.Branches.Count)];
            int orient1 = PickClosestDihedralOrientation(seg1, angleDeg, rng);
            Vector2Int cursor = StampPieceDihedral(seg1, orient1, attach, alignByEntry: true, mask, authoredCore, size, size);

            // Optional second chained segment
            if (rng.NextDouble() < kit.SecondSegmentProbability)
            {
                float bend = (float)((rng.NextDouble() * 2.0 - 1.0) * 35.0);
                float angle2 = NormalizeAngle(angleDeg + bend);
                CollapseRiftPieceData seg2 = kit.Branches[rng.Next(kit.Branches.Count)];
                int orient2 = PickClosestDihedralOrientation(seg2, angle2, rng);
                Vector2Int prevCursor = cursor;
                cursor = StampPieceDihedral(seg2, orient2, cursor, alignByEntry: true, mask, authoredCore, size, size);
                DrawBridge(prevCursor, cursor, mask, authoredCore, size, size);

                // Optional low-probability sub-branch forking off the first segment joint
                if (rng.NextDouble() < kit.SubBranchProbability)
                {
                    float side = rng.Next(2) == 0 ? 1f : -1f;
                    float subAngle = NormalizeAngle(angleDeg + side * (35f + (float)rng.NextDouble() * 25f));
                    CollapseRiftPieceData subTip = kit.Tips[rng.Next(kit.Tips.Count)];
                    int subOrient = PickClosestDihedralOrientation(subTip, subAngle, rng);
                    StampPieceDihedral(subTip, subOrient, prevCursor, alignByEntry: true, mask, authoredCore, size, size);
                }

                angleDeg = angle2;
            }

            // Tip closure
            CollapseRiftPieceData tip = kit.Tips[rng.Next(kit.Tips.Count)];
            int tipOrient = PickClosestDihedralOrientation(tip, angleDeg, rng);
            StampPieceDihedral(tip, tipOrient, cursor, alignByEntry: true, mask, authoredCore, size, size);
        }

        return FinalizeBitmap(mask, authoredCore, size, center);
    }

    /// <summary>
    /// Composes a wall rift rising from the bottom seam (<c>y = 0</c>) up to <paramref name="maxWallPixelHeight"/>:
    /// - Horizontal mirror only (never rotated).
    /// - Trunk tilt constrained within ±MaxWallTrunkAngleDegrees (~25° of vertical).
    /// - Y-fork upward branches and tapered tips strictly clamped below <paramref name="maxWallPixelHeight"/>.
    /// </summary>
    public static CollapseRiftComposedBitmap ComposeWallRift(
        CollapseRiftRuntimeKit kit,
        int riftSeed,
        int cellSize = 48,
        int maxWallPixelHeight = 32)
    {
        if (kit == null || !kit.IsValid)
        {
            kit = CollapseRiftPlaceholderKitFactory.CreateWallKit();
        }

        int size = Mathf.Max(16, cellSize);
        int maxYExclusive = Mathf.Clamp(maxWallPixelHeight, 8, size);
        var mask = new bool[size * size];
        var authoredCore = new bool[size * size];
        var rng = new System.Random(riftSeed);

        // 1. Grounded base at bottom seam (horizontal mirror only)
        CollapseRiftPieceData basePiece = kit.CoresOrBases[rng.Next(kit.CoresOrBases.Count)];
        bool baseFlipX = rng.Next(2) == 1;
        int baseAnchorX = (size / 2) + rng.Next(-4, 5);
        var origin = new Vector2Int(Mathf.Clamp(baseAnchorX, 6, size - 7), 1);
        Vector2Int cursor = StampPieceMirrorOnly(basePiece, baseFlipX, origin, alignByEntry: true, mask, authoredCore, size, maxYExclusive);

        // 2. Rising trunk segments bounded by ±MaxWallTrunkAngleDegrees and maxWallPixelHeight
        int trunkSegments = kit.MinWallTrunkSegments + rng.Next(kit.MaxWallTrunkSegments - kit.MinWallTrunkSegments + 1);
        float maxTan = Mathf.Tan(kit.MaxWallTrunkAngleDegrees * Mathf.Deg2Rad);
        int forkYLimit = Mathf.Max(6, maxYExclusive - 12);

        for (int t = 0; t < trunkSegments && cursor.y < forkYLimit; t++)
        {
            CollapseRiftPieceData trunk = kit.Trunks[rng.Next(kit.Trunks.Count)];
            bool flipX = rng.Next(2) == 1;

            // Enforce cumulative lean stays within ±MaxWallTrunkAngleDegrees relative to origin
            Vector2Int predictedExit = cursor + ResolveMirroredDelta(trunk, flipX);
            float rise = Mathf.Max(1f, predictedExit.y - origin.y);
            float maxOffsetX = rise * maxTan;
            if (Mathf.Abs(predictedExit.x - origin.x) > maxOffsetX)
            {
                flipX = !flipX;
            }

            Vector2Int prev = cursor;
            cursor = StampPieceMirrorOnly(trunk, flipX, cursor, alignByEntry: true, mask, authoredCore, size, maxYExclusive);
            DrawBridge(prev, cursor, mask, authoredCore, size, maxYExclusive);
        }

        // 3. Y-fork upward diagonal branches (left & right via horizontal mirror only) + Tips
        int branchCount = kit.WallBranchCountMin + rng.Next(kit.WallBranchCountMax - kit.WallBranchCountMin + 1);
        bool firstSideFlip = rng.Next(2) == 1;
        for (int b = 0; b < branchCount; b++)
        {
            CollapseRiftPieceData branch = kit.Branches[rng.Next(kit.Branches.Count)];
            // Strictly alternate left/right horizontal mirror so 2-branch wall rifts form a clean Y-fork
            bool flipX = (b % 2 == 0) ? firstSideFlip : !firstSideFlip;
            Vector2Int branchStart = new Vector2Int(
                Mathf.Clamp(cursor.x + (flipX ? -1 : 1), 2, size - 3),
                Mathf.Clamp(cursor.y, 2, maxYExclusive - 3));
            DrawBridge(cursor, branchStart, mask, authoredCore, size, maxYExclusive);

            Vector2Int branchEnd = StampPieceMirrorOnly(branch, flipX, branchStart, alignByEntry: true, mask, authoredCore, size, maxYExclusive);

            if (branchEnd.y < maxYExclusive - 2)
            {
                CollapseRiftPieceData tip = kit.Tips[rng.Next(kit.Tips.Count)];
                Vector2Int tipEnd = StampPieceMirrorOnly(tip, flipX, branchEnd, alignByEntry: true, mask, authoredCore, size, maxYExclusive);
                DrawBridge(branchEnd, new Vector2Int((branchEnd.x + tipEnd.x) / 2, (branchEnd.y + tipEnd.y) / 2), mask, authoredCore, size, maxYExclusive);
            }
        }

        // Optional central top tip if headroom remains
        if (cursor.y < maxYExclusive - 4 && rng.NextDouble() < 0.5)
        {
            CollapseRiftPieceData centerTip = kit.Tips[rng.Next(kit.Tips.Count)];
            StampPieceMirrorOnly(centerTip, rng.Next(2) == 1, cursor, alignByEntry: true, mask, authoredCore, size, maxYExclusive);
        }

        return FinalizeBitmap(mask, authoredCore, size, origin);
    }

    private static CollapseRiftComposedBitmap FinalizeBitmap(
        bool[] mask,
        bool[] authoredCore,
        int size,
        Vector2Int requestedOrigin)
    {
        Vector2Int resolvedOrigin = CollapseRiftBfsGrowth.ResolveNearestOpaquePixel(mask, size, requestedOrigin);
        if (resolvedOrigin.x < 0)
        {
            resolvedOrigin = new Vector2Int(size / 2, size / 2);
        }

        byte[] growthOrder = CollapseRiftBfsGrowth.ComputeGrowthOrder(mask, size, resolvedOrigin);
        bool[] coreMask = CollapseRiftBfsGrowth.ComputeCoreMask(mask, authoredCore, size);

        int maxOccupiedY = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (mask[y * size + x] && y > maxOccupiedY)
                {
                    maxOccupiedY = y;
                }
            }
        }

        return new CollapseRiftComposedBitmap(size, resolvedOrigin, mask, growthOrder, coreMask, maxOccupiedY);
    }

    private static Vector2Int StampPieceDihedral(
        CollapseRiftPieceData piece,
        int orientation,
        Vector2Int targetAnchor,
        bool alignByEntry,
        bool[] canvasMask,
        bool[] canvasCore,
        int canvasSize,
        int maxYExclusive)
    {
        GetOrientedDimensions(piece.Width, piece.Height, orientation, out int oW, out int oH);
        Vector2Int orientedEntry = TransformPointDihedral(piece.Entry, piece.Width, piece.Height, orientation);
        Vector2Int orientedExit = TransformPointDihedral(piece.Exit, piece.Width, piece.Height, orientation);

        Vector2Int pivot = alignByEntry ? orientedEntry : new Vector2Int(oW / 2, oH / 2);
        int baseX = targetAnchor.x - pivot.x;
        int baseY = targetAnchor.y - pivot.y;

        for (int py = 0; py < piece.Height; py++)
        {
            for (int px = 0; px < piece.Width; px++)
            {
                int srcIdx = py * piece.Width + px;
                if (!piece.Mask[srcIdx]) continue;

                Vector2Int tp = TransformPointDihedral(new Vector2Int(px, py), piece.Width, piece.Height, orientation);
                int cx = baseX + tp.x;
                int cy = baseY + tp.y;
                if (cx < 0 || cx >= canvasSize || cy < 0 || cy >= maxYExclusive) continue;

                int dstIdx = cy * canvasSize + cx;
                canvasMask[dstIdx] = true;
                if (piece.CoreMask[srcIdx])
                {
                    canvasCore[dstIdx] = true;
                }
            }
        }

        return new Vector2Int(
            Mathf.Clamp(baseX + orientedExit.x, 1, canvasSize - 2),
            Mathf.Clamp(baseY + orientedExit.y, 1, maxYExclusive - 2));
    }

    private static Vector2Int StampPieceMirrorOnly(
        CollapseRiftPieceData piece,
        bool flipX,
        Vector2Int targetAnchor,
        bool alignByEntry,
        bool[] canvasMask,
        bool[] canvasCore,
        int canvasSize,
        int maxYExclusive)
    {
        int orientation = flipX ? 4 : 0; // 0 = identity, 4 = horizontal mirror only (no rotation)
        return StampPieceDihedral(piece, orientation, targetAnchor, alignByEntry, canvasMask, canvasCore, canvasSize, maxYExclusive);
    }

    private static Vector2Int ResolveMirroredDelta(CollapseRiftPieceData piece, bool flipX)
    {
        int orientation = flipX ? 4 : 0;
        Vector2Int e = TransformPointDihedral(piece.Entry, piece.Width, piece.Height, orientation);
        Vector2Int x = TransformPointDihedral(piece.Exit, piece.Width, piece.Height, orientation);
        return x - e;
    }

    private static int PickClosestDihedralOrientation(CollapseRiftPieceData piece, float desiredAngleDeg, System.Random rng)
    {
        Vector2 desired = AngleToDir(desiredAngleDeg);
        float bestDot = -2f;
        int bestOrient = 0;
        int tieCount = 0;

        for (int orient = 0; orient < 8; orient++)
        {
            Vector2Int e = TransformPointDihedral(piece.Entry, piece.Width, piece.Height, orient);
            Vector2Int x = TransformPointDihedral(piece.Exit, piece.Width, piece.Height, orient);
            Vector2 v = new Vector2(x.x - e.x, x.y - e.y);
            float dot = v.sqrMagnitude > 1e-4f ? Vector2.Dot(v.normalized, desired) : 0f;

            if (dot > bestDot + 0.05f)
            {
                bestDot = dot;
                bestOrient = orient;
                tieCount = 1;
            }
            else if (Mathf.Abs(dot - bestDot) <= 0.05f)
            {
                tieCount++;
                if (rng.Next(tieCount) == 0)
                {
                    bestOrient = orient;
                }
            }
        }

        return bestOrient;
    }

    private static void DrawBridge(
        Vector2Int a,
        Vector2Int b,
        bool[] canvasMask,
        bool[] canvasCore,
        int canvasSize,
        int maxYExclusive)
    {
        int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        if (steps <= 0) return;

        for (int s = 0; s <= steps; s++)
        {
            float t = (float)s / steps;
            int cx = Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t));
            int cy = Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t));
            if (cx >= 0 && cx < canvasSize && cy >= 0 && cy < maxYExclusive)
            {
                int idx = cy * canvasSize + cx;
                canvasMask[idx] = true;
                canvasCore[idx] = true;
            }
        }
    }

    private static void GetOrientedDimensions(int width, int height, int orientation, out int oW, out int oH)
    {
        int rot = orientation & 3;
        if (rot == 1 || rot == 3)
        {
            oW = height;
            oH = width;
        }
        else
        {
            oW = width;
            oH = height;
        }
    }

    private static Vector2Int TransformPointDihedral(Vector2Int p, int width, int height, int orientation)
    {
        int x = p.x;
        int y = p.y;
        bool flipX = (orientation & 4) != 0;
        if (flipX)
        {
            x = (width - 1) - x;
        }

        int rot = orientation & 3;
        return rot switch
        {
            1 => new Vector2Int(height - 1 - y, x),
            2 => new Vector2Int(width - 1 - x, height - 1 - y),
            3 => new Vector2Int(y, width - 1 - x),
            _ => new Vector2Int(x, y)
        };
    }

    private static float NormalizeAngle(float deg)
    {
        deg %= 360f;
        return deg < 0f ? deg + 360f : deg;
    }

    private static Vector2 AngleToDir(float deg)
    {
        float rad = deg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
    }
}

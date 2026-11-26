using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure C# runtime kit consumed by <see cref="CollapseRiftComposer"/> without touching Unity assets.
/// </summary>
public sealed class CollapseRiftRuntimeKit
{
    public CollapseRiftSurfaceType SurfaceType { get; }
    public IReadOnlyList<CollapseRiftPieceData> CoresOrBases { get; }
    public IReadOnlyList<CollapseRiftPieceData> Trunks { get; }
    public IReadOnlyList<CollapseRiftPieceData> Branches { get; }
    public IReadOnlyList<CollapseRiftPieceData> Tips { get; }

    // Floor tuning
    public int MinFloorBranches { get; }
    public int MaxFloorBranches { get; }
    public float SecondSegmentProbability { get; }
    public float SubBranchProbability { get; }
    public float AngleSpreadJitterDegrees { get; }

    // Wall tuning
    public float MaxWallTrunkAngleDegrees { get; }
    public int MinWallTrunkSegments { get; }
    public int MaxWallTrunkSegments { get; }
    public int WallBranchCountMin { get; }
    public int WallBranchCountMax { get; }
    public int PixelsPerWorldCell { get; }

    public CollapseRiftRuntimeKit(
        CollapseRiftSurfaceType surfaceType,
        IReadOnlyList<CollapseRiftPieceData> coresOrBases,
        IReadOnlyList<CollapseRiftPieceData> trunks,
        IReadOnlyList<CollapseRiftPieceData> branches,
        IReadOnlyList<CollapseRiftPieceData> tips,
        int minFloorBranches = 2,
        int maxFloorBranches = 3,
        float secondSegmentProbability = 0.65f,
        float subBranchProbability = 0.25f,
        float angleSpreadJitterDegrees = 22f,
        float maxWallTrunkAngleDegrees = 25f,
        int minWallTrunkSegments = 2,
        int maxWallTrunkSegments = 3,
        int wallBranchCountMin = 2,
        int wallBranchCountMax = 2,
        int pixelsPerWorldCell = 16)
    {
        SurfaceType = surfaceType;
        CoresOrBases = coresOrBases ?? Array.Empty<CollapseRiftPieceData>();
        Trunks = trunks ?? Array.Empty<CollapseRiftPieceData>();
        Branches = branches ?? Array.Empty<CollapseRiftPieceData>();
        Tips = tips ?? Array.Empty<CollapseRiftPieceData>();

        MinFloorBranches = Mathf.Clamp(minFloorBranches, 1, 6);
        MaxFloorBranches = Mathf.Clamp(maxFloorBranches, MinFloorBranches, 6);
        SecondSegmentProbability = Mathf.Clamp01(secondSegmentProbability);
        SubBranchProbability = Mathf.Clamp01(subBranchProbability);
        AngleSpreadJitterDegrees = Mathf.Clamp(angleSpreadJitterDegrees, 0f, 45f);

        MaxWallTrunkAngleDegrees = Mathf.Clamp(maxWallTrunkAngleDegrees, 0f, 35f);
        MinWallTrunkSegments = Mathf.Clamp(minWallTrunkSegments, 1, 6);
        MaxWallTrunkSegments = Mathf.Clamp(maxWallTrunkSegments, MinWallTrunkSegments, 6);
        WallBranchCountMin = Mathf.Clamp(wallBranchCountMin, 1, 4);
        WallBranchCountMax = Mathf.Clamp(wallBranchCountMax, WallBranchCountMin, 4);
        PixelsPerWorldCell = Mathf.Max(8, pixelsPerWorldCell);
    }

    public bool IsValid =>
        SurfaceType == CollapseRiftSurfaceType.Floor
            ? CoresOrBases.Count > 0 && Branches.Count > 0 && Tips.Count > 0
            : CoresOrBases.Count > 0 && Trunks.Count > 0 && Branches.Count > 0 && Tips.Count > 0;
}

/// <summary>
/// Built-in deterministic pixel-art placeholder kit used by unit tests and as a safe fallback
/// when an authored <see cref="CollapseRiftKitAsset"/> has no piece sprites assigned yet.
/// </summary>
public static class CollapseRiftPlaceholderKitFactory
{
    public static CollapseRiftRuntimeKit CreateFloorKit()
    {
        var cores = new List<CollapseRiftPieceData>
        {
            FromAscii(new[]
            {
                "..oooo..",
                ".o####o.",
                "o######o",
                "o######o",
                ".o####o.",
                "..oooo.."
            }, new Vector2Int(3, 2), new Vector2Int(7, 2)),
            FromAscii(new[]
            {
                ".ooooo..",
                "o#####o.",
                "o#####oo",
                ".o####o.",
                "..oooo.."
            }, new Vector2Int(3, 2), new Vector2Int(7, 2)),
            FromAscii(new[]
            {
                "..ooo...",
                ".o###oo.",
                "o#####o.",
                ".oo###o.",
                "...ooo.."
            }, new Vector2Int(3, 2), new Vector2Int(6, 2))
        };

        var branches = new List<CollapseRiftPieceData>
        {
            FromAscii(new[]
            {
                ".oooooo.",
                "o######o",
                "o######o",
                ".oooooo."
            }, new Vector2Int(0, 1), new Vector2Int(7, 1)),
            FromAscii(new[]
            {
                "...ooooo",
                ".oo####o",
                "o####oo.",
                "ooooo..."
            }, new Vector2Int(0, 1), new Vector2Int(7, 2)),
            FromAscii(new[]
            {
                "ooooo...",
                "o####oo.",
                ".oo####o",
                "...ooooo"
            }, new Vector2Int(0, 2), new Vector2Int(7, 1)),
            FromAscii(new[]
            {
                "oooooo",
                "o####o",
                "o####o",
                "oooooo"
            }, new Vector2Int(0, 1), new Vector2Int(5, 1))
        };

        var tips = new List<CollapseRiftPieceData>
        {
            FromAscii(new[]
            {
                "ooo..",
                "o##oo",
                "ooo.."
            }, new Vector2Int(0, 1), new Vector2Int(4, 1)),
            FromAscii(new[]
            {
                "..oo",
                "oo#o",
                "ooo."
            }, new Vector2Int(0, 1), new Vector2Int(3, 2)),
            FromAscii(new[]
            {
                "ooo.",
                "o#oo",
                "..oo"
            }, new Vector2Int(0, 1), new Vector2Int(3, 0))
        };

        return new CollapseRiftRuntimeKit(CollapseRiftSurfaceType.Floor, cores, null, branches, tips);
    }

    public static CollapseRiftRuntimeKit CreateWallKit()
    {
        var bases = new List<CollapseRiftPieceData>
        {
            FromAscii(new[]
            {
                "....o##o....",
                "...o####o...",
                "..o######o..",
                "oo###oo###oo",
                "o##oo..oo##o"
            }, new Vector2Int(6, 0), new Vector2Int(6, 4)),
            FromAscii(new[]
            {
                "...o##o...",
                "..o####o..",
                "oo######oo",
                "ooo....ooo"
            }, new Vector2Int(5, 0), new Vector2Int(5, 3))
        };

        var trunks = new List<CollapseRiftPieceData>
        {
            FromAscii(new[]
            {
                ".o##o",
                ".o##o",
                "o###o",
                "o###o",
                "o##o.",
                "o##o."
            }, new Vector2Int(2, 0), new Vector2Int(3, 5)),
            FromAscii(new[]
            {
                "o##o",
                "o##o",
                "o##o",
                "o##o",
                "o##o"
            }, new Vector2Int(2, 0), new Vector2Int(2, 4)),
            FromAscii(new[]
            {
                "..o##o",
                ".o###o",
                ".o##o.",
                "o###o.",
                "o##o.."
            }, new Vector2Int(2, 0), new Vector2Int(4, 4))
        };

        var branches = new List<CollapseRiftPieceData>
        {
            FromAscii(new[]
            {
                "....o#o",
                "...o##o",
                "..o##o.",
                ".o##o..",
                "o##o..."
            }, new Vector2Int(1, 0), new Vector2Int(5, 4)),
            FromAscii(new[]
            {
                "...o#o",
                "..o##o",
                ".o##o.",
                "o##o.."
            }, new Vector2Int(1, 0), new Vector2Int(4, 3)),
            FromAscii(new[]
            {
                ".....o#o",
                "...oo##o",
                ".oo###o.",
                "o###oo.."
            }, new Vector2Int(1, 0), new Vector2Int(6, 3))
        };

        var tips = new List<CollapseRiftPieceData>
        {
            FromAscii(new[]
            {
                ".o.",
                "o#o",
                "o#o"
            }, new Vector2Int(1, 0), new Vector2Int(1, 2)),
            FromAscii(new[]
            {
                "..o",
                ".o#",
                "o#o"
            }, new Vector2Int(1, 0), new Vector2Int(2, 2)),
            FromAscii(new[]
            {
                "o..",
                "#o.",
                "o#o"
            }, new Vector2Int(1, 0), new Vector2Int(0, 2))
        };

        return new CollapseRiftRuntimeKit(CollapseRiftSurfaceType.Wall, bases, trunks, branches, tips);
    }

    public static CollapseRiftPieceData FromAscii(string[] topDownRows, Vector2Int entryBottomUp, Vector2Int exitBottomUp)
    {
        int height = topDownRows.Length;
        int width = topDownRows[0].Length;
        var mask = new bool[width * height];
        var core = new bool[width * height];

        for (int topY = 0; topY < height; topY++)
        {
            int y = height - 1 - topY;
            string row = topDownRows[topY];
            for (int x = 0; x < width; x++)
            {
                char c = row[x];
                int idx = y * width + x;
                if (c == '#')
                {
                    mask[idx] = true;
                    core[idx] = true;
                }
                else if (c == 'o')
                {
                    mask[idx] = true;
                    core[idx] = false;
                }
            }
        }

        return new CollapseRiftPieceData(width, height, mask, core, entryBottomUp, exitBottomUp);
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Inspector-configurable entry for one static piece in a procedural collapse rift kit (TASK-342).
/// Artists or developers drop transparent PNG sprites here and specify bottom-up pixel entry/exit sockets.
/// </summary>
[Serializable]
public struct CollapseRiftPieceEntry
{
    public Sprite PieceSprite;
    [Tooltip("Bottom-up pixel coordinates inside the sprite rect where this piece connects to its parent.")]
    public Vector2Int EntryPixel;
    [Tooltip("Bottom-up pixel coordinates inside the sprite rect where the next piece attaches.")]
    public Vector2Int ExitPixel;
}

/// <summary>
/// Extracts pure C# <see cref="CollapseRiftPieceData"/> bitmaps from readable Unity <see cref="Sprite"/> or <see cref="Texture2D"/> assets.
/// Automatically separates interior core pixels (#19191C / dark pit / cyan interior) from perimeter rim pixels (#29292E).
/// </summary>
public static class CollapseRiftPieceExtractor
{
    public static bool TryExtractFromSprite(Sprite sprite, Vector2Int entryPixel, Vector2Int exitPixel, out CollapseRiftPieceData pieceData)
    {
        pieceData = null;
        if (sprite == null || sprite.texture == null)
        {
            return false;
        }

        Texture2D tex = sprite.texture;
        if (!tex.isReadable)
        {
            Debug.LogWarning($"[CollapseRiftPieceExtractor] Texture '{tex.name}' is not Read/Write enabled; skipping piece.");
            return false;
        }

        Rect r = sprite.textureRect;
        int x0 = Mathf.Clamp(Mathf.RoundToInt(r.x), 0, tex.width - 1);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(r.y), 0, tex.height - 1);
        int w = Mathf.Clamp(Mathf.RoundToInt(r.width), 1, tex.width - x0);
        int h = Mathf.Clamp(Mathf.RoundToInt(r.height), 1, tex.height - y0);

        Color[] pixels = tex.GetPixels(x0, y0, w, h);
        return TryExtractFromColors(pixels, w, h, entryPixel, exitPixel, out pieceData);
    }

    public static bool TryExtractFromColors(
        Color[] bottomUpPixels,
        int width,
        int height,
        Vector2Int entryPixel,
        Vector2Int exitPixel,
        out CollapseRiftPieceData pieceData)
    {
        pieceData = null;
        if (bottomUpPixels == null || width <= 0 || height <= 0 || bottomUpPixels.Length != width * height)
        {
            return false;
        }

        int total = width * height;
        var mask = new bool[total];
        var core = new bool[total];
        int opaqueCount = 0;

        // First pass: opaque mask and luminance range across opaque pixels
        float minLuma = float.MaxValue;
        float maxLuma = float.MinValue;
        for (int i = 0; i < total; i++)
        {
            Color c = bottomUpPixels[i];
            if (c.a < 0.5f) continue;
            mask[i] = true;
            opaqueCount++;
            float luma = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            if (luma < minLuma) minLuma = luma;
            if (luma > maxLuma) maxLuma = luma;
        }

        if (opaqueCount == 0)
        {
            return false;
        }

        // Second pass: classify core vs edge using the Catacombs Stage1/Stage3 palette contrast
        // (Stage1: core #19191C is darker than rim #29292E; Stage3: cyan interior glows)
        bool hasLumaContrast = (maxLuma - minLuma) > 0.03f;
        float splitLuma = (minLuma + maxLuma) * 0.5f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                if (!mask[idx]) continue;

                Color c = bottomUpPixels[idx];
                bool isCyanInterior = (c.g - c.r) > 0.12f && (c.b - c.r) > 0.08f;
                if (isCyanInterior)
                {
                    core[idx] = true;
                }
                else if (hasLumaContrast)
                {
                    float luma = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                    core[idx] = luma <= splitLuma;
                }
                else
                {
                    bool interior4 = x > 0 && x < width - 1 && y > 0 && y < height - 1 &&
                        mask[idx - 1] && mask[idx + 1] && mask[idx - width] && mask[idx + width];
                    core[idx] = interior4;
                }
            }
        }

        Vector2Int clampedEntry = new Vector2Int(Mathf.Clamp(entryPixel.x, 0, width - 1), Mathf.Clamp(entryPixel.y, 0, height - 1));
        Vector2Int clampedExit = new Vector2Int(Mathf.Clamp(exitPixel.x, 0, width - 1), Mathf.Clamp(exitPixel.y, 0, height - 1));
        pieceData = new CollapseRiftPieceData(width, height, mask, core, clampedEntry, clampedExit);
        return true;
    }
}

/// <summary>
/// Inspector-configurable kit of static rift pieces for Floor or Wall procedural composition (US-92 / TASK-342).
/// Supports drop-in replacement with final artist PNG kits without changing any code.
/// </summary>
[CreateAssetMenu(fileName = "CollapseRiftKit", menuName = "Grimhold/Dungeon Pressure/Collapse Rift Kit")]
public sealed class CollapseRiftKitAsset : ScriptableObject
{
    [Header("Surface Type")]
    [SerializeField] private CollapseRiftSurfaceType _surfaceType = CollapseRiftSurfaceType.Floor;

    [Header("Pieces (Drop-in PNG Sprites with Read/Write Enabled)")]
    [Tooltip("Floor: central cores. Wall: grounded bases at the bottom seam.")]
    [SerializeField] private CollapseRiftPieceEntry[] _coresOrBases;

    [Tooltip("Wall only: rising trunk segments between base and Y-branches.")]
    [SerializeField] private CollapseRiftPieceEntry[] _trunks;

    [Tooltip("Floor: radial branch segments. Wall: upward diagonal Y-branches.")]
    [SerializeField] private CollapseRiftPieceEntry[] _branches;

    [Tooltip("Tapered 1px tip closures at the end of branches.")]
    [SerializeField] private CollapseRiftPieceEntry[] _tips;

    [Header("Floor Composition Parameters")]
    [Range(1, 5)] [SerializeField] private int _minFloorBranches = 2;
    [Range(1, 6)] [SerializeField] private int _maxFloorBranches = 3;
    [Range(0f, 1f)] [SerializeField] private float _secondSegmentProbability = 0.65f;
    [Range(0f, 1f)] [SerializeField] private float _subBranchProbability = 0.25f;
    [Range(0f, 45f)] [SerializeField] private float _angleSpreadJitterDegrees = 22f;

    [Header("Wall Composition Parameters")]
    [Range(0f, 35f)] [SerializeField] private float _maxWallTrunkAngleDegrees = 25f;
    [Range(1, 5)] [SerializeField] private int _minWallTrunkSegments = 2;
    [Range(1, 6)] [SerializeField] private int _maxWallTrunkSegments = 3;
    [Range(1, 4)] [SerializeField] private int _wallBranchCountMin = 2;
    [Range(1, 4)] [SerializeField] private int _wallBranchCountMax = 2;
    [Min(8)] [SerializeField] private int _pixelsPerWorldCell = 16;

    public CollapseRiftSurfaceType SurfaceType => _surfaceType;
    public int PixelsPerWorldCell => _pixelsPerWorldCell;

    public CollapseRiftRuntimeKit BuildRuntimeKit()
    {
        var cores = ExtractList(_coresOrBases);
        var trunks = ExtractList(_trunks);
        var branches = ExtractList(_branches);
        var tips = ExtractList(_tips);

        var kit = new CollapseRiftRuntimeKit(
            _surfaceType,
            cores,
            trunks,
            branches,
            tips,
            _minFloorBranches,
            _maxFloorBranches,
            _secondSegmentProbability,
            _subBranchProbability,
            _angleSpreadJitterDegrees,
            _maxWallTrunkAngleDegrees,
            _minWallTrunkSegments,
            _maxWallTrunkSegments,
            _wallBranchCountMin,
            _wallBranchCountMax,
            _pixelsPerWorldCell);

        if (kit.IsValid)
        {
            return kit;
        }

        // Safe fallback to built-in placeholder pieces if kit slots are incomplete or unreadable.
        return _surfaceType == CollapseRiftSurfaceType.Floor
            ? CollapseRiftPlaceholderKitFactory.CreateFloorKit()
            : CollapseRiftPlaceholderKitFactory.CreateWallKit();
    }

    private static List<CollapseRiftPieceData> ExtractList(CollapseRiftPieceEntry[] entries)
    {
        var list = new List<CollapseRiftPieceData>();
        if (entries == null) return list;

        for (int i = 0; i < entries.Length; i++)
        {
            if (CollapseRiftPieceExtractor.TryExtractFromSprite(
                    entries[i].PieceSprite,
                    entries[i].EntryPixel,
                    entries[i].ExitPixel,
                    out CollapseRiftPieceData piece))
            {
                list.Add(piece);
            }
        }

        return list;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        _maxFloorBranches = Mathf.Max(_minFloorBranches, _maxFloorBranches);
        _maxWallTrunkSegments = Mathf.Max(_minWallTrunkSegments, _maxWallTrunkSegments);
        _wallBranchCountMax = Mathf.Max(_wallBranchCountMin, _wallBranchCountMax);
        _pixelsPerWorldCell = Mathf.Max(8, _pixelsPerWorldCell);
    }
#endif
}

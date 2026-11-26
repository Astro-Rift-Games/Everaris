using System;
using UnityEngine;

/// <summary>
/// Pure C# static bitmap of one authored or cropped rift kit piece (bottom-up coordinates: y=0 is bottom).
/// </summary>
public sealed class CollapseRiftPieceData
{
    public int Width { get; }
    public int Height { get; }
    public bool[] Mask { get; }
    public bool[] CoreMask { get; }
    public Vector2Int Entry { get; }
    public Vector2Int Exit { get; }
    public Vector2Int[] ExtraSockets { get; }

    public CollapseRiftPieceData(
        int width,
        int height,
        bool[] mask,
        bool[] coreMask,
        Vector2Int entry,
        Vector2Int exit,
        Vector2Int[] extraSockets = null)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Piece dimensions must be positive.");
        }

        int expected = width * height;
        if (mask == null || mask.Length != expected)
        {
            throw new ArgumentException("Mask length must equal width * height.", nameof(mask));
        }

        if (coreMask == null || coreMask.Length != expected)
        {
            throw new ArgumentException("CoreMask length must equal width * height.", nameof(coreMask));
        }

        Width = width;
        Height = height;
        Mask = mask;
        CoreMask = coreMask;
        Entry = entry;
        Exit = exit;
        ExtraSockets = extraSockets ?? Array.Empty<Vector2Int>();
    }
}

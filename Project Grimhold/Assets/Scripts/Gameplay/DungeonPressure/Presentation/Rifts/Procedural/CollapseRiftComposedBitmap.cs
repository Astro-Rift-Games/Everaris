using UnityEngine;

/// <summary>
/// Output of deterministic procedural composition for a single rift cell (TASK-340).
/// All arrays use row-major bottom-up indexing (y * Size + x).
/// </summary>
public sealed class CollapseRiftComposedBitmap
{
    public int Size { get; }
    public Vector2Int Origin { get; }
    public bool[] Mask { get; }
    public byte[] GrowthOrder { get; }
    public bool[] CoreMask { get; }
    public int MaxOccupiedY { get; }

    public CollapseRiftComposedBitmap(
        int size,
        Vector2Int origin,
        bool[] mask,
        byte[] growthOrder,
        bool[] coreMask,
        int maxOccupiedY)
    {
        Size = size;
        Origin = origin;
        Mask = mask;
        GrowthOrder = growthOrder;
        CoreMask = coreMask;
        MaxOccupiedY = maxOccupiedY;
    }
}

using UnityEngine;

public readonly struct CollapseRiftPlacement
{
    public readonly Vector2 WorldPosition;
    public readonly Vector2Int AnchorCell;
    public readonly CollapseRiftSurfaceType SurfaceType;
    public readonly CollapseRiftBatch Batch;
    public readonly float PhaseOffsetSeconds;

    public CollapseRiftPlacement(
        Vector2 worldPosition,
        Vector2Int anchorCell,
        CollapseRiftSurfaceType surfaceType,
        CollapseRiftBatch batch,
        float phaseOffsetSeconds)
    {
        WorldPosition = worldPosition;
        AnchorCell = anchorCell;
        SurfaceType = surfaceType;
        Batch = batch;
        PhaseOffsetSeconds = phaseOffsetSeconds;
    }
}

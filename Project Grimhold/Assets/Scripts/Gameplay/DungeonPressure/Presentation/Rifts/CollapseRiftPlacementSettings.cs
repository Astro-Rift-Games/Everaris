using UnityEngine;

public readonly struct CollapseRiftPlacementSettings
{
    public readonly int Stage2FloorCount;
    public readonly int Stage3FloorCount;
    public readonly int WallCount;
    public readonly int FloorMarginCells;
    public readonly int WallWidthCells;
    public readonly int WallFaceHeightCells;
    public readonly int WallHorizontalMarginCells;
    public readonly int WallTopMarginCells;
    public readonly float MinFloorSpacing;
    public readonly float MinWallSpacing;
    public readonly float SanctuaryExclusionRadius;
    public readonly float WallPropExclusionRadius;

    public CollapseRiftPlacementSettings(
        int stage2FloorCount,
        int stage3FloorCount,
        int wallCount,
        int floorMarginCells,
        int wallWidthCells,
        int wallFaceHeightCells,
        int wallHorizontalMarginCells,
        int wallTopMarginCells,
        float minFloorSpacing,
        float minWallSpacing,
        float sanctuaryExclusionRadius,
        float wallPropExclusionRadius)
    {
        Stage2FloorCount = Mathf.Max(0, stage2FloorCount);
        Stage3FloorCount = Mathf.Max(0, stage3FloorCount);
        WallCount = Mathf.Max(0, wallCount);
        FloorMarginCells = Mathf.Max(0, floorMarginCells);
        WallWidthCells = Mathf.Max(1, wallWidthCells);
        WallFaceHeightCells = Mathf.Max(1, wallFaceHeightCells);
        WallHorizontalMarginCells = Mathf.Max(0, wallHorizontalMarginCells);
        WallTopMarginCells = Mathf.Max(0, wallTopMarginCells);
        MinFloorSpacing = Mathf.Max(0f, minFloorSpacing);
        MinWallSpacing = Mathf.Max(0f, minWallSpacing);
        SanctuaryExclusionRadius = Mathf.Max(0f, sanctuaryExclusionRadius);
        WallPropExclusionRadius = Mathf.Max(0f, wallPropExclusionRadius);
    }
}

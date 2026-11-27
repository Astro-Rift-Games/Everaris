using UnityEngine;
using UnityEngine.UI;

/// <summary>One ordered texture/vertex-limit continuation of the minimap, not one object per tile.</summary>
[DisallowMultipleComponent]
public sealed class RaidMinimapBatchGraphic : MaskableGraphic
{
    private MinimapMeshBatch _batch;
    private float _scale;
    private Vector2 _lastRectSize = new Vector2(float.NaN, float.NaN);

    public override Texture mainTexture => _batch?.Texture != null ? _batch.Texture : base.mainTexture;

    public void Configure(MinimapMeshBatch batch, float scale, Color tint)
    {
        _batch = batch;
        _scale = scale;
        color = tint;
        raycastTarget = false;
        SetAllDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertices)
    {
        Populate(vertices, _batch, _scale, color);
    }

    protected override void OnRectTransformDimensionsChange()
    {
        if (HasRectSizeChanged(rectTransform, ref _lastRectSize))
            base.OnRectTransformDimensionsChange();
    }

    /// <summary>
    /// Pixel-perfect Canvases report a dimension change to the map and every child whenever the
    /// panned map reaches a new screen pixel, although no rect size changed. Tile-art meshes do not
    /// depend on their position, so only a real size change may dirty them; configuration rebuilds
    /// through its explicit SetAllDirty.
    /// </summary>
    internal static bool HasRectSizeChanged(RectTransform rect, ref Vector2 lastSize)
    {
        Vector2 size = rect.rect.size;
        if (size == lastSize) return false;
        lastSize = size;
        return true;
    }

    internal static void Populate(VertexHelper vertices, MinimapMeshBatch batch, float scale, Color tint)
    {
        vertices.Clear();
        if (batch == null) return;
        for (int i = 0; i < batch.VertexCount; i++)
            vertices.AddVert(batch.GetPosition(i) * scale, (Color)batch.GetColor(i) * tint, batch.GetUv(i));
        for (int i = 0; i < batch.IndexCount; i += 3)
            vertices.AddTriangle(batch.GetIndex(i), batch.GetIndex(i + 1), batch.GetIndex(i + 2));
    }
}

using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// FIX-107 guards: the tile-art meshes ignore position-only dimension callbacks, but a real
/// configuration change or resize must still rebuild them. The per-frame panning defect itself
/// only reproduces in the player loop; see RaidMinimapMeshRebuildPlayModeTests.
/// </summary>
public sealed class RaidMinimapMeshRebuildTests
{
    private GameObject _root;
    private RectTransform _map;
    private RaidMinimapView _view;
    private RaidMinimapGraphic _mapGraphic;
    private RaidMinimapBatchGraphic _batch;
    private int _mapDirtyCount;
    private int _batchDirtyCount;

    [TearDown]
    public void TearDown()
    {
        if (_root != null)
            Object.DestroyImmediate(_root);
    }

    [Test]
    public void ReconfiguringBatch_StillRebuildsItsMesh()
    {
        CreateView(1f);

        _batch.Configure(null, 2f, Color.white);
        Canvas.ForceUpdateCanvases();

        Assert.That(_batchDirtyCount, Is.EqualTo(1));
    }

    [Test]
    public void ResizingMap_StillRebuildsTileArtMeshes()
    {
        CreateView(1f);

        _map.sizeDelta = new Vector2(820f, 640f);
        Canvas.ForceUpdateCanvases();

        Assert.That(_batchDirtyCount, Is.GreaterThan(0));
        Assert.That(_mapDirtyCount, Is.GreaterThan(0));
    }

    private void CreateView(float canvasScale)
    {
        _root = new GameObject("IsolatedMinimapRebuild", typeof(RectTransform));
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Matches LocalGameplayHud.prefab, whose Canvas is pixel perfect.
        canvas.pixelPerfect = true;
        canvas.scaleFactor = canvasScale;

        RectTransform viewport = CreateRect("Viewport", _root.transform);
        viewport.sizeDelta = new Vector2(196f, 196f);
        viewport.anchoredPosition = new Vector2(37.37f, -23.29f);
        viewport.gameObject.AddComponent<RectMask2D>();

        _map = CreateRect("Map", viewport);
        _map.sizeDelta = new Vector2(800f, 600f);
        _map.gameObject.AddComponent<CanvasRenderer>();
        _mapGraphic = _map.gameObject.AddComponent<RaidMinimapGraphic>();

        // Mirrors RaidMinimapGraphic.RefreshBatches: stretched child with its own CanvasRenderer.
        RectTransform batchRect = CreateRect("TileArtBatch", _map);
        batchRect.gameObject.AddComponent<CanvasRenderer>();
        batchRect.anchorMin = Vector2.zero;
        batchRect.anchorMax = Vector2.one;
        batchRect.offsetMin = Vector2.zero;
        batchRect.offsetMax = Vector2.zero;
        _batch = batchRect.gameObject.AddComponent<RaidMinimapBatchGraphic>();
        _batch.Configure(null, 1f, Color.white);

        _view = _root.AddComponent<RaidMinimapView>();
        SetViewField("_mapRect", _map);
        SetViewField("_mapGraphic", _mapGraphic);
        Canvas.ForceUpdateCanvases();

        _mapGraphic.RegisterDirtyVerticesCallback(() => _mapDirtyCount++);
        _batch.RegisterDirtyVerticesCallback(() => _batchDirtyCount++);
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var owner = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)owner.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
        return rect;
    }

    private void SetViewField(string name, object value)
    {
        typeof(RaidMinimapView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(_view, value);
    }
}

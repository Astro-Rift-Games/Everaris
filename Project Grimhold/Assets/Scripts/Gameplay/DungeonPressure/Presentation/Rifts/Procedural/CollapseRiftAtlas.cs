using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-raid runtime procedural atlas packing all floor and wall rift silhouettes into a single
/// RGBA32 Point-filtered Texture2D (R=mask, G=BFS order, B=core, A=255) with explicit disposal (TASK-341).
/// </summary>
public sealed class CollapseRiftAtlas : IDisposable
{
    public Texture2D Texture { get; private set; }
    public IReadOnlyList<Sprite> FloorSprites => _floorSprites;
    public IReadOnlyList<Sprite> WallSprites => _wallSprites;
    public bool IsDisposed { get; private set; }

    private readonly List<Sprite> _floorSprites = new();
    private readonly List<Sprite> _wallSprites = new();

    private CollapseRiftAtlas()
    {
    }

    public static CollapseRiftAtlas Build(
        IReadOnlyList<CollapseRiftPlacement> floorPlacements,
        IReadOnlyList<CollapseRiftPlacement> wallPlacements,
        CollapseRiftRuntimeKit floorKit,
        CollapseRiftRuntimeKit wallKit,
        int raidSeed,
        int cellPixelSize,
        int maxWallPixelHeight,
        float pixelsPerUnit = 16f)
    {
        var atlas = new CollapseRiftAtlas();
        int floorCount = floorPlacements != null ? floorPlacements.Count : 0;
        int wallCount = wallPlacements != null ? wallPlacements.Count : 0;
        int totalRifts = floorCount + wallCount;
        if (totalRifts <= 0)
        {
            return atlas;
        }

        int cellSize = Mathf.Clamp(cellPixelSize, 16, 128);
        int cols = Mathf.CeilToInt(Mathf.Sqrt(totalRifts));
        int rows = Mathf.CeilToInt((float)totalRifts / cols);
        int atlasWidth = NextPowerOfTwo(cols * cellSize);
        int atlasHeight = NextPowerOfTwo(rows * cellSize);
        int cellsPerRow = atlasWidth / cellSize;

        var pixels = new Color32[atlasWidth * atlasHeight];
        int slot = 0;

        for (int i = 0; i < floorCount; i++)
        {
            int seed = CollapseRiftComposer.ComputeRiftSeed(raidSeed, floorPlacements[i].AnchorCell, CollapseRiftSurfaceType.Floor);
            CollapseRiftComposedBitmap composed = CollapseRiftComposer.ComposeFloorRift(floorKit, seed, cellSize);
            BlitCell(composed, slot, cellsPerRow, cellSize, atlasWidth, pixels);
            slot++;
        }

        for (int i = 0; i < wallCount; i++)
        {
            int seed = CollapseRiftComposer.ComputeRiftSeed(raidSeed, wallPlacements[i].AnchorCell, CollapseRiftSurfaceType.Wall);
            CollapseRiftComposedBitmap composed = CollapseRiftComposer.ComposeWallRift(wallKit, seed, cellSize, maxWallPixelHeight);
            BlitCell(composed, slot, cellsPerRow, cellSize, atlasWidth, pixels);
            slot++;
        }

        var texture = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, mipChain: false, linear: true)
        {
            name = $"CollapseRiftAtlas_{raidSeed}",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 0,
            hideFlags = HideFlags.DontSave
        };
        texture.SetPixels32(pixels);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        atlas.Texture = texture;

        var pivot = new Vector2(0.5f, 0.5f);
        float ppu = Mathf.Max(1f, pixelsPerUnit);
        slot = 0;

        for (int i = 0; i < floorCount; i++)
        {
            atlas._floorSprites.Add(CreateCellSprite(texture, slot, cellsPerRow, cellSize, pivot, ppu, $"FloorRiftSprite_{i}"));
            slot++;
        }

        for (int i = 0; i < wallCount; i++)
        {
            atlas._wallSprites.Add(CreateCellSprite(texture, slot, cellsPerRow, cellSize, pivot, ppu, $"WallRiftSprite_{i}"));
            slot++;
        }

        return atlas;
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        DestroyList(_floorSprites);
        DestroyList(_wallSprites);

        if (Texture != null)
        {
            DestroyUnityObject(Texture);
            Texture = null;
        }
    }

    private static void BlitCell(
        CollapseRiftComposedBitmap bitmap,
        int slotIndex,
        int cellsPerRow,
        int cellSize,
        int atlasWidth,
        Color32[] atlasPixels)
    {
        int col = slotIndex % cellsPerRow;
        int row = slotIndex / cellsPerRow;
        int originX = col * cellSize;
        int originY = row * cellSize;

        for (int y = 0; y < cellSize; y++)
        {
            int dstRowStart = (originY + y) * atlasWidth + originX;
            int srcRowStart = y * cellSize;
            for (int x = 0; x < cellSize; x++)
            {
                int srcIdx = srcRowStart + x;
                if (!bitmap.Mask[srcIdx]) continue;

                byte order = bitmap.GrowthOrder[srcIdx];
                byte core = bitmap.CoreMask[srcIdx] ? (byte)255 : (byte)0;
                atlasPixels[dstRowStart + x] = new Color32(255, order, core, 255);
            }
        }
    }

    private static Sprite CreateCellSprite(
        Texture2D texture,
        int slotIndex,
        int cellsPerRow,
        int cellSize,
        Vector2 pivot,
        float pixelsPerUnit,
        string spriteName)
    {
        int col = slotIndex % cellsPerRow;
        int row = slotIndex / cellsPerRow;
        var rect = new Rect(col * cellSize, row * cellSize, cellSize, cellSize);
        Sprite sprite = Sprite.Create(texture, rect, pivot, pixelsPerUnit, 0u, SpriteMeshType.FullRect);
        sprite.name = spriteName;
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    private static int NextPowerOfTwo(int value)
    {
        int pot = 64;
        while (pot < value && pot < 4096)
        {
            pot <<= 1;
        }
        return pot;
    }

    private static void DestroyList(List<Sprite> sprites)
    {
        for (int i = 0; i < sprites.Count; i++)
        {
            if (sprites[i] != null)
            {
                DestroyUnityObject(sprites[i]);
            }
        }
        sprites.Clear();
    }

    private static void DestroyUnityObject(UnityEngine.Object obj)
    {
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(obj);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}

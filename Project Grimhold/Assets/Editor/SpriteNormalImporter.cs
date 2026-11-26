using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

internal static class SpriteNormalImporter
{
    private const string NormalMapName = "_NormalMap";

    internal static void Generate(string path, float relief, float detail, int smoothing, bool repeat)
    {
        var source = RequireUnlinkedSprite(path);
        string output = AssetDatabase.GenerateUniqueAssetPath(path.Substring(0, path.Length - 4) + "_NormalMap.png");
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            if (!tex.LoadImage(File.ReadAllBytes(path))) throw new IOException("Could not decode source PNG.");
            // The decoded PNG is in source pixels; imported Sprite.rect may be downscaled.
            var frames = SourceSliceRects(source, tex.width, tex.height);
            if (frames.Length == 0) throw new InvalidOperationException("The texture contains no sprite slices.");
            var input = tex.GetPixels32();
            var result = new Color32[input.Length];
            Array.Fill(result, new Color32(128, 128, 255, 255));
            foreach (Rect rect in frames)
            {
                int x = (int)rect.x, y = (int)rect.y, w = (int)rect.width, h = (int)rect.height;
                var slice = new Color32[w * h];
                for (int row = 0; row < h; row++) Array.Copy(input, (y + row) * tex.width + x, slice, row * w, w);
                var normals = SpriteNormalFrame.Generate(slice, w, h, relief, detail, smoothing, repeat);
                for (int row = 0; row < h; row++) Array.Copy(normals, row * w, result, (y + row) * tex.width + x, w);
            }
            tex.SetPixels32(result);
            tex.Apply();
            using var stream = new FileStream(output, FileMode.CreateNew);
            byte[] bytes = tex.EncodeToPNG();
            stream.Write(bytes, 0, bytes.Length);
        }
        finally { UnityEngine.Object.DestroyImmediate(tex); }

        AssetDatabase.ImportAsset(output);
        LinkImported(source, output, repeat);
    }

    // Links a normal map produced outside Unity (for example by SpriteFusion), laid out in source pixels.
    internal static void Link(string path, string normalPath, bool repeat = false)
    {
        var source = RequireUnlinkedSprite(path);
        if (!(AssetImporter.GetAtPath(normalPath) is TextureImporter))
            throw new ArgumentException("Normal map must be an imported texture under Assets.", nameof(normalPath));
        source.GetSourceTextureWidthAndHeight(out int width, out int height);
        var map = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        map.GetSourceTextureWidthAndHeight(out int mapWidth, out int mapHeight);
        if (mapWidth != width || mapHeight != height)
            throw new ArgumentException($"Normal map is {mapWidth}x{mapHeight}; source is {width}x{height}.", nameof(normalPath));
        LinkImported(source, normalPath, repeat);
    }

    internal static Rect[] SourceSliceRects(TextureImporter source, int width, int height)
    {
        if (source.spriteImportMode == SpriteImportMode.Single) return new[] { new Rect(0, 0, width, height) };
        var factories = new SpriteDataProviderFactories();
        factories.Init();
        var provider = factories.GetSpriteEditorDataProviderFromObject(source);
        provider.InitSpriteEditorDataProvider();
        return provider.GetSpriteRects().Select(slice => slice.rect).ToArray();
    }

    private static TextureImporter RequireUnlinkedSprite(string path)
    {
        var source = AssetImporter.GetAtPath(path) as TextureImporter;
        if (source == null || source.textureType != TextureImporterType.Sprite || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Select a PNG sprite under Assets.");
        if (source.secondarySpriteTextures.Any(link => link.name == NormalMapName))
            throw new InvalidOperationException("Source already has a normal map. Use a pilot copy to compare settings.");
        return source;
    }

    private static void LinkImported(TextureImporter source, string normalPath, bool repeat)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        importer.textureType = TextureImporterType.NormalMap;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Point;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = source.maxTextureSize;
        importer.spritePixelsPerUnit = source.spritePixelsPerUnit;
        importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        var links = source.secondarySpriteTextures.ToList();
        links.Add(new SecondarySpriteTexture { name = NormalMapName, texture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) });
        source.secondarySpriteTextures = links.ToArray();
        source.SaveAndReimport();
    }
}

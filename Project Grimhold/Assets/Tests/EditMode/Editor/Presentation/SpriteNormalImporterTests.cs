using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class SpriteNormalImporterTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Generate_LinksMatchingNormalWithoutChangingSourceSprites(bool sheet, bool downscale)
    {
        string folder = "Assets/NormalTest_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        string path = folder + "/Source.png";
        int width = downscale ? 64 : 8, height = width / 2;
        var texture = new Texture2D(width, height);
        try
        {
            texture.SetPixels(Enumerable.Repeat(Color.white, width * height).ToArray());
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = sheet ? SpriteImportMode.Multiple : SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 16;
            importer.maxTextureSize = downscale ? 32 : 2048;
            importer.filterMode = FilterMode.Point;
            if (sheet)
            {
#pragma warning disable 618
                importer.spritesheet = new[] { new SpriteMetaData { name = "Left", rect = new Rect(0, 0, height, height) },
                    new SpriteMetaData { name = "Right", rect = new Rect(height, 0, height, height) } };
#pragma warning restore 618
            }
            importer.SaveAndReimport();
            Rect[] BeforeOrAfter() => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                .OrderBy(s => s.rect.x).Select(s => s.rect).ToArray();
            var before = BeforeOrAfter();
            Assert.That(before.Length, Is.EqualTo(sheet ? 2 : 1));
            SpriteNormalImporter.Generate(path, 1f, 0f, 0, false);
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(BeforeOrAfter(), Is.EqualTo(before));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            var normal = importer.secondarySpriteTextures.Single(t => t.name == "_NormalMap").texture;
            Assert.That(normal.width, Is.EqualTo(downscale ? width / 2 : width));
            Assert.That(normal.height, Is.EqualTo(downscale ? height / 2 : height));
            Assert.That(texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(normal))), Is.True);
            int edge = sheet ? height - 1 : width - 1;
            Assert.That(texture.GetPixel(edge, height / 2).r, Is.GreaterThan(0.55f), "Right edge in source pixels");
            if (sheet) Assert.That(texture.GetPixel(edge + 1, height / 2).r, Is.LessThan(0.45f), "Next slice has an independent left edge");
            Assert.That(normal.filterMode, Is.EqualTo(FilterMode.Point));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.DeleteAsset(folder);
        }
    }

    [Test]
    public void Link_ExternalMapOfSameSize_LinksItAsPointNormalMapAndKeepsSlices()
    {
        string folder = CreateFolder();
        try
        {
            string source = WritePng(folder + "/Sheet.png", 8, 4);
            ImportSpriteSheet(source);
            Rect[] before = SliceRects(source);
            string normal = WritePng(folder + "/Sheet_NormalMap.png", 8, 4);

            SpriteNormalImporter.Link(source, normal);

            var importer = (TextureImporter)AssetImporter.GetAtPath(source);
            Assert.That(SliceRects(source), Is.EqualTo(before));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16));
            Assert.That(importer.secondarySpriteTextures.Single(t => t.name == "_NormalMap").texture,
                Is.EqualTo(AssetDatabase.LoadAssetAtPath<Texture2D>(normal)));
            var map = (TextureImporter)AssetImporter.GetAtPath(normal);
            Assert.That(map.textureType, Is.EqualTo(TextureImporterType.NormalMap));
            Assert.That(map.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(map.mipmapEnabled, Is.False);
            Assert.That(map.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
        }
        finally { AssetDatabase.DeleteAsset(folder); }
    }

    [Test]
    public void Link_ExternalMapOfDifferentSize_ThrowsAndLeavesSourceUnlinked()
    {
        string folder = CreateFolder();
        try
        {
            string source = WritePng(folder + "/Sheet.png", 8, 4);
            ImportSpriteSheet(source);
            string normal = WritePng(folder + "/Sheet_NormalMap.png", 4, 4);

            Assert.Throws<ArgumentException>(() => SpriteNormalImporter.Link(source, normal));

            var importer = (TextureImporter)AssetImporter.GetAtPath(source);
            Assert.That(importer.secondarySpriteTextures, Is.Empty);
        }
        finally { AssetDatabase.DeleteAsset(folder); }
    }

    private static string CreateFolder()
    {
        string name = "NormalLinkTest_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", name);
        return "Assets/" + name;
    }

    private static string WritePng(string path, int width, int height)
    {
        var texture = new Texture2D(width, height);
        try
        {
            texture.SetPixels(Enumerable.Repeat(Color.white, width * height).ToArray());
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
        AssetDatabase.ImportAsset(path);
        return path;
    }

    private static void ImportSpriteSheet(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 16;
        importer.filterMode = FilterMode.Point;
#pragma warning disable 618
        importer.spritesheet = new[] { new SpriteMetaData { name = "Left", rect = new Rect(0, 0, 4, 4) },
            new SpriteMetaData { name = "Right", rect = new Rect(4, 0, 4, 4) } };
#pragma warning restore 618
        importer.SaveAndReimport();
    }

    private static Rect[] SliceRects(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
        .OrderBy(s => s.rect.x).Select(s => s.rect).ToArray();
}

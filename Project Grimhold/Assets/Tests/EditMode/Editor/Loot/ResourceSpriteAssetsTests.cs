using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Loot
{
    public sealed class ResourceSpriteAssetsTests
    {
        private const string DefinitionsRoot = "Assets/Scriptable Objects/Loot/Definitions";
        private const string CatalogPath = "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset";
        private const string ArtRoot = "Assets/Art/Loot/World and Monsters Resources";

        private static readonly string[] ResourceIds =
        {
            "RES_ANCIENT_ARMOR",
            "RES_ANCIENT_ROOT",
            "RES_ATTUNEMENT_GEM",
            "RES_BAT_FANG",
            "RES_BAT_WING",
            "RES_BLACK_IRON",
            "RES_CORRUPT_MARROW",
            "RES_CUSTODIAN_SEAL",
            "RES_ETHER_DUST",
            "RES_GOBLIN_ARROWHEAD",
            "RES_GOBLIN_INSIGNIA",
            "RES_GOBLIN_SCRAP",
            "RES_GOLEM_CORE",
            "RES_LOST_GRIMOIRE_PAGE",
            "RES_RUNE_FRAGMENT",
            "RES_SLIME_CORE",
            "RES_SLIME_GEL",
            "RES_SOUL_FRAGMENT",
            "RES_SPECTRAL_ESSENCE",
            "RES_TANNED_LEATHER",
            "RES_WITHERED_BONE",
        };

        [Test]
        public void AllTwentyOneResources_HavePixelArtImportSettings_AndDefinitionSpritesBound()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null, "Missing LootDefinitionCatalog asset.");

            foreach (string id in ResourceIds)
            {
                var definition = AssetDatabase.LoadAssetAtPath<LootDefinition>($"{DefinitionsRoot}/{id}.asset");
                Assert.That(definition, Is.Not.Null, $"Missing LootDefinition for {id}.");

                Sprite world = AssertSprite($"{ArtRoot}/{id}-Base.png", id);
                Sprite icon = AssertSprite($"{ArtRoot}/{id}-Icon.png", id);
                AssertSprite($"{ArtRoot}/{id}-Interactable.png", id);

                Assert.That(definition.WorldSprite, Is.EqualTo(world), $"{id} WorldSprite should bind -Base sprite.");
                Assert.That(definition.Icon, Is.EqualTo(icon), $"{id} Icon should bind -Icon sprite.");
                Assert.That(definition.TryValidate(out string error), Is.True, $"{id}: {error}");

                Assert.That(catalog.TryGet(id, out LootDefinition registered), Is.True, $"{id} missing from catalog.");
                Assert.That(registered, Is.SameAs(definition), id);
            }

            Assert.That(catalog.TryValidate(out string catalogError), Is.True, catalogError);
        }

        private static Sprite AssertSprite(string path, string id)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null, $"{id} missing texture importer at {path}");
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite), path);
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single), path);
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16f), path);
            Assert.That(importer.spritePivot, Is.EqualTo(new Vector2(0.5f, 0.5f)), path);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), path);
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), path);
            Assert.That(importer.mipmapEnabled, Is.False, path);
            Assert.That(importer.alphaIsTransparency, Is.True, path);

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.That(sprite, Is.Not.Null, $"{id} missing Sprite sub-asset at {path}");
            Assert.That(sprite.rect, Is.EqualTo(new Rect(0f, 0f, 16f, 16f)), path);
            Assert.That(sprite.texture.width, Is.EqualTo(16), path);
            Assert.That(sprite.texture.height, Is.EqualTo(16), path);
            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(16f), path);
            Assert.That(sprite.pivot, Is.EqualTo(new Vector2(8f, 8f)), path);
            return sprite;
        }
    }
}

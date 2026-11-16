using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Loot
{
    public sealed class TreasureSpriteAssetsTests
    {
        private const string DefinitionsRoot = "Assets/Scriptable Objects/Loot/Definitions";
        private const string CatalogPath = "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset";
        private const string ArtRoot = "Assets/Art/Treasures";
        private const string PrefabsRoot = "Assets/Prefabs/Loot";

        private static readonly (string Id, string PrefabName)[] TreasureEntries =
        {
            ("TRE_CHALICE", null),
            ("TRE_CHAOS_COIN", "LootPickup_ChaosCoin.prefab"),
            ("TRE_CROWN", null),
            ("TRE_FAMINE_IDOL", "LootPickup_FamineIdol.prefab"),
            ("TRE_PILGRIM_LIGHT", "LootPickup_PilgrimLight.prefab"),
            ("TRE_SANCTUARY_HEART", "LootPickup_SanctuaryHeart.prefab"),
            ("TRE_VOID_BELL", "LootPickup_VoidBell.prefab"),
            ("TRE_WANDERER_EYE", "LootPickup_WandererEye.prefab"),
        };

        [Test]
        public void AllEightTreasures_HavePixelArtImportSettings_AndDefinitionSpritesBound()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null, "Missing LootDefinitionCatalog asset.");

            foreach ((string id, string prefabName) in TreasureEntries)
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

                if (!string.IsNullOrEmpty(prefabName))
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsRoot}/{prefabName}");
                    Assert.That(prefab, Is.Not.Null, $"Missing prefab {prefabName} for {id}.");
                    SpriteRenderer renderer = prefab.GetComponentInChildren<SpriteRenderer>(true);
                    Assert.That(renderer, Is.Not.Null, $"Missing SpriteRenderer on {prefabName}.");
                    Assert.That(renderer.sprite, Is.EqualTo(world), $"{prefabName} SpriteRenderer should bind {id}-Base sprite.");
                }
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
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), path);
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), path);
            Assert.That(importer.mipmapEnabled, Is.False, path);
            Assert.That(importer.alphaIsTransparency, Is.True, path);

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.That(sprite, Is.Not.Null, $"{id} missing Sprite sub-asset at {path}");
            Assert.That(sprite.texture.width, Is.EqualTo(16), path);
            Assert.That(sprite.texture.height, Is.EqualTo(16), path);
            Assert.That(sprite.pivot, Is.EqualTo(new Vector2(8f, 8f)), path);
            return sprite;
        }
    }
}

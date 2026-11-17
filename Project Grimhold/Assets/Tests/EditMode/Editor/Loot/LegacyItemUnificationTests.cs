using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Loot
{
    public sealed class LegacyItemUnificationTests
    {
        private const string CatalogPath = "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset";
        private LootDefinitionCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(CatalogPath);
            Assert.That(_catalog, Is.Not.Null, $"Failed to load catalog at '{CatalogPath}'.");
        }

        [Test]
        public void Catalog_ContainsSingleHealthPotionWithCanonicalId()
        {
            var field = typeof(LootDefinitionCatalog).GetField("_definitions", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = (List<LootDefinition>)field.GetValue(_catalog);

            int healthPotionCount = 0;
            foreach (var item in list)
            {
                if (item != null && (item.Id == "healthpotion" || item.Id == "CON_HEALTH_POTION"))
                {
                    healthPotionCount++;
                    Assert.That(item.Id, Is.EqualTo("CON_HEALTH_POTION"), "Expected only canonical 'CON_HEALTH_POTION' ID in catalog entries.");
                }
            }

            Assert.That(healthPotionCount, Is.EqualTo(1), "Expected exactly one health potion registered in catalog.");
        }

        [Test]
        public void Catalog_TryGet_ResolvesLegacyHealthPotionToCanonical()
        {
            Assert.That(_catalog.TryGet("CON_HEALTH_POTION", out LootDefinition canonical), Is.True);
            Assert.That(canonical, Is.Not.Null);
            Assert.That(canonical.Id, Is.EqualTo("CON_HEALTH_POTION"));

            Assert.That(_catalog.TryGet("healthpotion", out LootDefinition resolved), Is.True);
            Assert.That(resolved, Is.SameAs(canonical));
        }

        [Test]
        public void Catalog_HasNoItemsWithZeroObjectValueAndEmptyFamily()
        {
            var field = typeof(LootDefinitionCatalog).GetField("_definitions", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = (List<LootDefinition>)field.GetValue(_catalog);

            foreach (var item in list)
            {
                if (item == null) continue;
                if (item.Category == LootCategory.Resource || item.Category == LootCategory.Consumable || item.Category == LootCategory.Treasure)
                {
                    Assert.That(string.IsNullOrWhiteSpace(item.Family), Is.False,
                        $"Item '{item.name}' ({item.Id}) has empty Family.");
                }
            }
        }
    }
}

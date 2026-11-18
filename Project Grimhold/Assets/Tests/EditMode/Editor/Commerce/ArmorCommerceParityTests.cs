using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

namespace Tests.EditMode.Commerce
{
    public sealed class ArmorCommerceParityTests
    {
        private const string CatalogPath =
            "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset";

        private static readonly Dictionary<string, int> ExpectedArmorObjectValues = new Dictionary<string, int>
        {
            // Caballero Pesado
            ["heavy_armor_helmet"] = 90,
            ["heavy_armor_breastplate"] = 160,
            ["heavy_armor_gauntlets"] = 60,
            ["heavy_armor_leg_plate"] = 80,

            // Caballero Templado
            ["light_armor_open_sallet"] = 75,
            ["light_armor_chain_mail_armor"] = 130,
            ["light_armor_gloves"] = 50,
            ["light_armor_chain_mail_trousers"] = 65,

            // Ranger Explorador
            ["army_ranger_hat"] = 65,
            ["army_ranger_armor"] = 110,
            ["army_ranger_gloves"] = 45,
            ["army_ranger_trousers"] = 60,

            // Ranger Acechador
            ["forest_ranger_hood"] = 80,
            ["forest_ranger_leather_armor"] = 135,
            ["forest_ranger_gloves"] = 55,
            ["forest_ranger_trousers"] = 75,

            // Mago Arcano (placeholders)
            ["placeholder_helmet"] = 85,
            ["placeholder_armor"] = 145,
            ["placeholder_gloves"] = 55,
            ["placeholder_boots"] = 70,

            // Mago Viajero
            ["fire_mage_hood"] = 60,
            ["fire_mage_garb"] = 100,
            ["fire_mage_gloves"] = 40,
            ["fire_mage_trousers"] = 55
        };

        private static readonly string[] ExcludedBuybackOrPurchaseIds =
        {
            "CON_EXPLOSIVE_BOMB",
            "CON_SMOKE_BOMB",
            "CON_THROWING_DAGGER",
            "CON_SELF_REVIVE",
            "TRE_CHALICE",
            "TRE_CHAOS_COIN",
            "TRE_CROWN",
            "TRE_FAMINE_IDOL",
            "TRE_PILGRIM_LIGHT",
            "TRE_SANCTUARY_HEART",
            "TRE_VOID_BELL",
            "TRE_WANDERER_EYE",
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
            "RES_RUBY_GEM",
            "RES_RUNE_FRAGMENT",
            "RES_SAPPHIRE_GEM",
            "RES_SLIME_CORE",
            "RES_SLIME_GEL",
            "RES_SOUL_FRAGMENT",
            "RES_SPECTRAL_ESSENCE",
            "RES_TANNED_LEATHER",
            "RES_WITHERED_BONE",
            "bone"
        };

        private static readonly string[] TrainingItemIds =
        {
            "training_short_sword",
            "training_dagger",
            "training_wand"
        };

        private LootDefinitionCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(CatalogPath);
            Assert.That(_catalog, Is.Not.Null);
        }

        [Test]
        public void All24Armors_MatchDoc23ObjectValuesAndPricing()
        {
            Assert.That(ExpectedArmorObjectValues.Count, Is.EqualTo(24));

            foreach (var kvp in ExpectedArmorObjectValues)
            {
                string id = kvp.Key;
                int expectedOv = kvp.Value;

                Assert.That(_catalog.TryGet(id, out LootDefinition definition), Is.True, id);
                Assert.That(definition, Is.Not.Null, id);
                Assert.That(definition.ObjectValue, Is.EqualTo(expectedOv), $"{id} ObjectValue");
                Assert.That(definition.SellValuePerUnit, Is.EqualTo(expectedOv), $"{id} SellValuePerUnit");
                Assert.That(definition.BuyValuePerUnit, Is.EqualTo(expectedOv * 2), $"{id} BuyValuePerUnit");
            }
        }

        [Test]
        public void SpecialConsumablesTreasuresAndResources_HaveZeroBuyValue()
        {
            foreach (string id in ExcludedBuybackOrPurchaseIds)
            {
                Assert.That(_catalog.TryGet(id, out LootDefinition definition), Is.True, id);
                Assert.That(definition, Is.Not.Null, id);
                Assert.That(definition.BuyValuePerUnit, Is.Zero, $"{id} BuyValuePerUnit must be 0 (cannot be bought from regular NPC)");
                Assert.That(definition.SellValuePerUnit, Is.EqualTo(definition.ObjectValue), $"{id} SellValuePerUnit must equal ObjectValue");
                Assert.That(definition.Tradable, Is.True, $"{id} must be tradable/sellable");
            }
        }

        [Test]
        public void PermanentConsumables_HaveCanonicalValues()
        {
            AssertConsumable("CON_HEALTH_POTION", 20, 40, 20);
            AssertConsumable("CON_MANA_POTION", 20, 40, 20);
            AssertConsumable("CON_MANA_FRAGMENT", 8, 16, 8);
        }

        [Test]
        public void TrainingItems_AreNotTradableAndHaveZeroValues()
        {
            foreach (string id in TrainingItemIds)
            {
                Assert.That(_catalog.TryGet(id, out LootDefinition definition), Is.True, id);
                Assert.That(definition, Is.Not.Null, id);
                Assert.That(definition.Tradable, Is.False, $"{id} Tradable must be false");
                Assert.That(definition.ObjectValue, Is.Zero, $"{id} ObjectValue must be 0");
                Assert.That(definition.BuyValuePerUnit, Is.Zero, $"{id} BuyValuePerUnit must be 0");
                Assert.That(definition.SellValuePerUnit, Is.Zero, $"{id} SellValuePerUnit must be 0");
            }
        }

        private void AssertConsumable(string id, int objectValue, int buyPrice, int sellPrice)
        {
            Assert.That(_catalog.TryGet(id, out LootDefinition definition), Is.True, id);
            Assert.That(definition, Is.Not.Null, id);
            Assert.That(definition.ObjectValue, Is.EqualTo(objectValue), $"{id} ObjectValue");
            Assert.That(definition.BuyValuePerUnit, Is.EqualTo(buyPrice), $"{id} BuyValuePerUnit");
            Assert.That(definition.SellValuePerUnit, Is.EqualTo(sellPrice), $"{id} SellValuePerUnit");
            Assert.That(definition.Tradable, Is.True, $"{id} Tradable");
        }
    }
}

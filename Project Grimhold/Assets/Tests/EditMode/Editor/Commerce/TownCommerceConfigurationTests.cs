using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class TownCommerceConfigurationTests
{
    private const string AssetPath = "Assets/Scriptable Objects/Commerce/TownCommerceConfiguration.asset";

    [Test]
    public void ConfigurationAsset_ExistsAndPassesValidation()
    {
        var config = AssetDatabase.LoadAssetAtPath<TownCommerceConfiguration>(AssetPath);
        Assert.That(config, Is.Not.Null, $"Expected TownCommerceConfiguration asset at {AssetPath}");

        bool isValid = config.TryValidate(out string error);
        Assert.That(isValid, Is.True, $"Configuration validation failed: {error}");
        Assert.That(error, Is.Empty);
    }

    [Test]
    public void ConfigurationAsset_ContainsDoc23AuthoritativePoolsAndMultipliers()
    {
        var config = AssetDatabase.LoadAssetAtPath<TownCommerceConfiguration>(AssetPath);
        Assert.That(config, Is.Not.Null);

        Assert.That(config.BuyPriceMultiplier, Is.EqualTo(2.0f));
        Assert.That(config.RegularSellMultiplier, Is.EqualTo(1.0f));
        Assert.That(config.DemandSellMultiplier, Is.EqualTo(1.2f));
        Assert.That(config.BuybackPriceMultiplier, Is.EqualTo(1.5f));
        Assert.That(config.MaxBuybackEntries, Is.EqualTo(16));

        Assert.That(config.NormalWeaponsCount, Is.EqualTo(3));
        Assert.That(config.SpecialWeaponsCount, Is.EqualTo(1));
        Assert.That(config.ArmorsCount, Is.EqualTo(3));
        Assert.That(config.UtilitiesCount, Is.EqualTo(2));
        Assert.That(config.DemandItemsCount, Is.EqualTo(3));

        Assert.That(config.PermanentOffers.Count, Is.EqualTo(3), "Expected 3 permanent offers (Doc 23 §5.1)");
        Assert.That(config.PhysicalWeaponsPool.Count, Is.EqualTo(7), "Expected 7 physical weapons (Doc 23 §6.1)");
        Assert.That(config.MagicalWeaponsPool.Count, Is.EqualTo(5), "Expected 5 magical weapons (Doc 23 §6.1)");
        Assert.That(config.ArmorsPool.Count, Is.EqualTo(24), "Expected 24 armor pieces (Doc 23 §6.2)");
        Assert.That(config.UtilitiesPool.Count, Is.EqualTo(3), "Expected 3 utilities (Doc 23 §5.5)");
        Assert.That(config.DemandCandidatesPool.Count, Is.EqualTo(29), "Expected 29 demand candidates (Doc 23 §7)");
        Assert.That(config.SpecialModifiers.Count, Is.EqualTo(4), "Expected 4 special modifier options (Doc 23 §5.6)");
    }

    [Test]
    public void ExportToJson_ProducesValidJsonStructure()
    {
        var config = AssetDatabase.LoadAssetAtPath<TownCommerceConfiguration>(AssetPath);
        Assert.That(config, Is.Not.Null);

        string json = config.ExportToJson();
        Assert.That(json, Is.Not.Null.And.Not.Empty);
        Assert.That(json, Does.Contain("\"multipliers\""));
        Assert.That(json, Does.Contain("\"pools\""));
        Assert.That(json, Does.Contain("\"physicalWeapons\""));
        Assert.That(json, Does.Contain("\"magicalWeapons\""));
        Assert.That(json, Does.Contain("\"armors\""));
        Assert.That(json, Does.Contain("\"utilities\""));
        Assert.That(json, Does.Contain("\"demandCandidates\""));
        Assert.That(json, Does.Contain("\"specialModifiers\""));
    }

    [Test]
    public void BuildInitialStockForMerchant_BuildsValidNonEmptyStockForEveryMerchantType()
    {
        var config = AssetDatabase.LoadAssetAtPath<TownCommerceConfiguration>(AssetPath);
        Assert.That(config, Is.Not.Null);

        // General Merchant
        var generalStock = config.BuildInitialStockForMerchant(TownMerchantType.General);
        Assert.That(generalStock.Count, Is.GreaterThan(0));
        Assert.That(generalStock.Count, Is.LessThanOrEqualTo(16));
        bool hasGeneralItems = generalStock.TrueForAll(s => MerchantSpecializationPolicy.IsAllowedToBuyFromMerchant(TownMerchantType.General, s.Item));
        Assert.That(hasGeneralItems, Is.True, "General merchant stock should contain only allowed general items");

        // Blacksmith
        var blacksmithStock = config.BuildInitialStockForMerchant(TownMerchantType.Blacksmith);
        Assert.That(blacksmithStock.Count, Is.GreaterThan(0));
        Assert.That(blacksmithStock.Count, Is.LessThanOrEqualTo(16));
        bool hasBlacksmithItems = blacksmithStock.TrueForAll(s => MerchantSpecializationPolicy.IsAllowedToBuyFromMerchant(TownMerchantType.Blacksmith, s.Item));
        Assert.That(hasBlacksmithItems, Is.True, "Blacksmith stock should contain only allowed physical weapons and armors");

        // Arcanist
        var arcanistStock = config.BuildInitialStockForMerchant(TownMerchantType.Arcanist);
        Assert.That(arcanistStock.Count, Is.GreaterThan(0));
        Assert.That(arcanistStock.Count, Is.LessThanOrEqualTo(16));
        bool hasArcanistItems = arcanistStock.TrueForAll(s => MerchantSpecializationPolicy.IsAllowedToBuyFromMerchant(TownMerchantType.Arcanist, s.Item));
        Assert.That(hasArcanistItems, Is.True, "Arcanist stock should contain only allowed magical weapons");
    }

    [Test]
    public void MerchantNetworkController_InitializesAppropriateStockForEveryMerchantType()
    {
        var config = AssetDatabase.LoadAssetAtPath<TownCommerceConfiguration>(AssetPath);
        Assert.That(config, Is.Not.Null);

        var go = new GameObject("TestMerchant");
        try
        {
            var merchant = go.AddComponent<TownMerchantNetworkController>();
            merchant.CommerceConfig = config;

            // 1. Blacksmith initialization
            merchant.MerchantType = TownMerchantType.Blacksmith;
            merchant.InitializeStockFromConfiguration();
            Assert.That(merchant.Stock, Is.Not.Null.And.Not.Empty);
            Assert.That(merchant.Stock.Count, Is.LessThanOrEqualTo(16));
            bool allBlacksmith = true;
            foreach (var item in merchant.Stock)
            {
                if (!MerchantSpecializationPolicy.IsAllowedToBuyFromMerchant(TownMerchantType.Blacksmith, item.Item))
                {
                    allBlacksmith = false;
                    break;
                }
            }
            Assert.That(allBlacksmith, Is.True, "Blacksmith must only stock allowed weapons/armors");

            // 2. Arcanist initialization (switching merchant type must re-initialize because previous stock had blacksmith items)
            merchant.MerchantType = TownMerchantType.Arcanist;
            merchant.InitializeStockFromConfiguration();
            Assert.That(merchant.Stock, Is.Not.Null.And.Not.Empty);
            Assert.That(merchant.Stock.Count, Is.LessThanOrEqualTo(16));
            bool allArcanist = true;
            foreach (var item in merchant.Stock)
            {
                if (!MerchantSpecializationPolicy.IsAllowedToBuyFromMerchant(TownMerchantType.Arcanist, item.Item))
                {
                    allArcanist = false;
                    break;
                }
            }
            Assert.That(allArcanist, Is.True, "Arcanist must only stock allowed magical weapons");

            // 3. General Merchant initialization
            merchant.MerchantType = TownMerchantType.General;
            merchant.InitializeStockFromConfiguration();
            Assert.That(merchant.Stock, Is.Not.Null.And.Not.Empty);
            Assert.That(merchant.Stock.Count, Is.LessThanOrEqualTo(16));
            bool allGeneral = true;
            foreach (var item in merchant.Stock)
            {
                if (!MerchantSpecializationPolicy.IsAllowedToBuyFromMerchant(TownMerchantType.General, item.Item))
                {
                    allGeneral = false;
                    break;
                }
            }
            Assert.That(allGeneral, Is.True, "General merchant must only stock allowed general items");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}

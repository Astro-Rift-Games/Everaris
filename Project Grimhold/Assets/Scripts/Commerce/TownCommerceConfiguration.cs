using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Authoritative ScriptableObject configuration for the Town Commerce System (Docs 22 and 23).
/// Allows Game Designers to configure in Unity Editor:
/// - Commercial multipliers (Buy, Regular Sell, Demand +20%, Buyback, Max FIFO Buyback).
/// - Weekly rotation quantities (Weapons, Armors, Utilities, Demand).
/// - Permanent catalog for General Merchant.
/// - Weekly rotating item pools (Physical weapons, Magical weapons, Armors, Utilities with stock).
/// - Special weapon modifier table (probabilities and ObjectValue increments).
/// - Weekly demand bonus candidate pool.
/// </summary>
[CreateAssetMenu(fileName = "TownCommerceConfiguration", menuName = "Grimhold/Commerce/Town Commerce Configuration")]
public sealed class TownCommerceConfiguration : ScriptableObject
{
    [Header("Economic Multipliers (Doc 23 §7 & §10)")]
    [Tooltip("Multiplier applied to ObjectValue when buying from NPC (default 2.0).")]
    [SerializeField] private float _buyPriceMultiplier = 2.0f;

    [Tooltip("Multiplier applied to ObjectValue when selling to NPC (default 1.0).")]
    [SerializeField] private float _regularSellMultiplier = 1.0f;

    [Tooltip("Multiplier applied to ObjectValue when an item is in weekly demand (default 1.2).")]
    [SerializeField] private float _demandSellMultiplier = 1.2f;

    [Tooltip("Multiplier applied to gold received when buying back an item (default 1.5).")]
    [SerializeField] private float _buybackPriceMultiplier = 1.5f;

    [Tooltip("Maximum capacity of shared FIFO buyback pool per player (default 16).")]
    [SerializeField] private int _maxBuybackEntries = 16;

    [Header("Rotation Counts per Week (Doc 23 §5)")]
    [SerializeField] private int _normalWeaponsCount = 3;
    [SerializeField] private int _specialWeaponsCount = 1;
    [SerializeField] private int _armorsCount = 3;
    [SerializeField] private int _utilitiesCount = 2;
    [SerializeField] private int _demandItemsCount = 3;

    [Header("Permanent Catalog (Mercader General - Doc 23 §5.1)")]
    [SerializeField] private List<PermanentCommerceOfferConfig> _permanentOffers = new List<PermanentCommerceOfferConfig>();

    [Header("Weekly Rotating Pools (Doc 23 §5)")]
    [Tooltip("Physical weapons pool sold by Blacksmith.")]
    [SerializeField] private List<LootDefinition> _physicalWeaponsPool = new List<LootDefinition>();

    [Tooltip("Magical weapons pool sold by Arcanist.")]
    [SerializeField] private List<LootDefinition> _magicalWeaponsPool = new List<LootDefinition>();

    [Tooltip("Armor pieces pool sold by Blacksmith.")]
    [SerializeField] private List<LootDefinition> _armorsPool = new List<LootDefinition>();

    [Tooltip("Utilities pool with individual stock sold by General Merchant.")]
    [SerializeField] private List<UtilityCommerceOfferConfig> _utilitiesPool = new List<UtilityCommerceOfferConfig>();

    [Header("Special Weapon Modifiers (Doc 23 §5.6)")]
    [SerializeField] private List<SpecialModifierConfigOption> _specialModifiers = new List<SpecialModifierConfigOption>();

    [Header("Weekly Demand Candidates (Doc 23 §7)")]
    [Tooltip("Resources and treasures eligible to receive the +20% weekly sell bonus.")]
    [SerializeField] private List<LootDefinition> _demandCandidatesPool = new List<LootDefinition>();

    // Public accessors
    public float BuyPriceMultiplier => _buyPriceMultiplier;
    public float RegularSellMultiplier => _regularSellMultiplier;
    public float DemandSellMultiplier => _demandSellMultiplier;
    public float BuybackPriceMultiplier => _buybackPriceMultiplier;
    public int MaxBuybackEntries => _maxBuybackEntries;

    public int NormalWeaponsCount => _normalWeaponsCount;
    public int SpecialWeaponsCount => _specialWeaponsCount;
    public int ArmorsCount => _armorsCount;
    public int UtilitiesCount => _utilitiesCount;
    public int DemandItemsCount => _demandItemsCount;

    public IReadOnlyList<PermanentCommerceOfferConfig> PermanentOffers => _permanentOffers;
    public IReadOnlyList<LootDefinition> PhysicalWeaponsPool => _physicalWeaponsPool;
    public IReadOnlyList<LootDefinition> MagicalWeaponsPool => _magicalWeaponsPool;
    public IReadOnlyList<LootDefinition> ArmorsPool => _armorsPool;
    public IReadOnlyList<UtilityCommerceOfferConfig> UtilitiesPool => _utilitiesPool;
    public IReadOnlyList<SpecialModifierConfigOption> SpecialModifiers => _specialModifiers;
    public IReadOnlyList<LootDefinition> DemandCandidatesPool => _demandCandidatesPool;

    /// <summary>
    /// Validates the configuration consistency and completeness according to Doc 23.
    /// </summary>
    public bool TryValidate(out string error)
    {
        if (_buyPriceMultiplier <= 0f) { error = "BuyPriceMultiplier must be > 0."; return false; }
        if (_regularSellMultiplier <= 0f) { error = "RegularSellMultiplier must be > 0."; return false; }
        if (_demandSellMultiplier <= 0f) { error = "DemandSellMultiplier must be > 0."; return false; }
        if (_buybackPriceMultiplier <= 0f) { error = "BuybackPriceMultiplier must be > 0."; return false; }
        if (_maxBuybackEntries <= 0) { error = "MaxBuybackEntries must be > 0."; return false; }

        if (_permanentOffers == null || _permanentOffers.Count == 0)
        {
            error = "PermanentOffers list is empty.";
            return false;
        }

        foreach (var offer in _permanentOffers)
        {
            if (offer.Item == null)
            {
                error = "PermanentOffers contains an unassigned item reference.";
                return false;
            }
        }

        if (_physicalWeaponsPool == null || _physicalWeaponsPool.Count == 0)
        {
            error = "PhysicalWeaponsPool is empty.";
            return false;
        }

        if (_magicalWeaponsPool == null || _magicalWeaponsPool.Count == 0)
        {
            error = "MagicalWeaponsPool is empty.";
            return false;
        }

        if (_armorsPool == null || _armorsPool.Count == 0)
        {
            error = "ArmorsPool is empty.";
            return false;
        }

        if (_utilitiesPool == null || _utilitiesPool.Count == 0)
        {
            error = "UtilitiesPool is empty.";
            return false;
        }

        if (_specialModifiers == null || _specialModifiers.Count == 0)
        {
            error = "SpecialModifiers is empty.";
            return false;
        }

        if (_demandCandidatesPool == null || _demandCandidatesPool.Count == 0)
        {
            error = "DemandCandidatesPool is empty.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Builds the initial offering stock for a specific merchant type according to Doc 23 §3.
    /// Respects Fusion's maximum stock slot capacity (16 slots).
    /// </summary>
    public List<MerchantStockItem> BuildInitialStockForMerchant(TownMerchantType merchantType, int maxSlots = 16)
    {
        var stock = new List<MerchantStockItem>();

        switch (merchantType)
        {
            case TownMerchantType.General:
                if (_permanentOffers != null)
                {
                    foreach (var offer in _permanentOffers)
                    {
                        if (offer.Item != null && stock.Count < maxSlots)
                        {
                            stock.Add(new MerchantStockItem
                            {
                                Item = offer.Item,
                                InitialQuantity = offer.UnlimitedStock ? MerchantStockItem.UnlimitedQuantity : offer.StockQuantity
                            });
                        }
                    }
                }
                if (_utilitiesPool != null)
                {
                    foreach (var utility in _utilitiesPool)
                    {
                        if (utility.Item != null && stock.Count < maxSlots)
                        {
                            stock.Add(new MerchantStockItem
                            {
                                Item = utility.Item,
                                InitialQuantity = utility.WeeklyStock
                            });
                        }
                    }
                }
                break;

            case TownMerchantType.Blacksmith:
                if (_physicalWeaponsPool != null)
                {
                    foreach (var weapon in _physicalWeaponsPool)
                    {
                        if (weapon != null && stock.Count < maxSlots)
                        {
                            stock.Add(new MerchantStockItem
                            {
                                Item = weapon,
                                InitialQuantity = 1
                            });
                        }
                    }
                }
                if (_armorsPool != null)
                {
                    foreach (var armor in _armorsPool)
                    {
                        if (armor != null && stock.Count < maxSlots)
                        {
                            stock.Add(new MerchantStockItem
                            {
                                Item = armor,
                                InitialQuantity = 1
                            });
                        }
                    }
                }
                break;

            case TownMerchantType.Arcanist:
                if (_magicalWeaponsPool != null)
                {
                    foreach (var magicWeapon in _magicalWeaponsPool)
                    {
                        if (magicWeapon != null && stock.Count < maxSlots)
                        {
                            stock.Add(new MerchantStockItem
                            {
                                Item = magicWeapon,
                                InitialQuantity = 1
                            });
                        }
                    }
                }
                break;
        }

        return stock;
    }

    /// <summary>
    /// Serializes the entire configuration into a formatted JSON string compatible with the Node.js backend.
    /// </summary>
    public string ExportToJson()
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"multipliers\": {");
        sb.AppendLine($"    \"buyPriceMultiplier\": {_buyPriceMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        sb.AppendLine($"    \"regularSellMultiplier\": {_regularSellMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        sb.AppendLine($"    \"demandSellMultiplier\": {_demandSellMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        sb.AppendLine($"    \"buybackPriceMultiplier\": {_buybackPriceMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        sb.AppendLine($"    \"maxBuybackEntries\": {_maxBuybackEntries}");
        sb.AppendLine("  },");

        sb.AppendLine("  \"rotationCounts\": {");
        sb.AppendLine($"    \"normalWeapons\": {_normalWeaponsCount},");
        sb.AppendLine($"    \"specialWeapons\": {_specialWeaponsCount},");
        sb.AppendLine($"    \"armors\": {_armorsCount},");
        sb.AppendLine($"    \"utilities\": {_utilitiesCount},");
        sb.AppendLine($"    \"demandItems\": {_demandItemsCount}");
        sb.AppendLine("  },");

        sb.AppendLine("  \"permanentOffers\": [");
        for (int i = 0; i < _permanentOffers.Count; i++)
        {
            var p = _permanentOffers[i];
            string lootId = p.Item != null ? p.Item.LootId.Value : "";
            int buyPrice = p.BuyPrice;
            if (buyPrice <= 0 && p.Item != null)
            {
                buyPrice = Mathf.CeilToInt(p.Item.ObjectValue * _buyPriceMultiplier);
            }
            string comma = i < _permanentOffers.Count - 1 ? "," : "";
            sb.AppendLine($"    {{ \"lootId\": \"{lootId}\", \"buyPrice\": {buyPrice}, \"unlimited\": {p.UnlimitedStock.ToString().ToLower()}, \"stock\": {p.StockQuantity} }}{comma}");
        }
        sb.AppendLine("  ],");

        sb.AppendLine("  \"pools\": {");

        // Physical weapons
        sb.AppendLine("    \"physicalWeapons\": [");
        for (int i = 0; i < _physicalWeaponsPool.Count; i++)
        {
            string id = _physicalWeaponsPool[i] != null ? _physicalWeaponsPool[i].LootId.Value : "";
            string comma = i < _physicalWeaponsPool.Count - 1 ? "," : "";
            sb.AppendLine($"      \"{id}\"{comma}");
        }
        sb.AppendLine("    ],");

        // Magical weapons
        sb.AppendLine("    \"magicalWeapons\": [");
        for (int i = 0; i < _magicalWeaponsPool.Count; i++)
        {
            string id = _magicalWeaponsPool[i] != null ? _magicalWeaponsPool[i].LootId.Value : "";
            string comma = i < _magicalWeaponsPool.Count - 1 ? "," : "";
            sb.AppendLine($"      \"{id}\"{comma}");
        }
        sb.AppendLine("    ],");

        // Armors
        sb.AppendLine("    \"armors\": [");
        for (int i = 0; i < _armorsPool.Count; i++)
        {
            string id = _armorsPool[i] != null ? _armorsPool[i].LootId.Value : "";
            string comma = i < _armorsPool.Count - 1 ? "," : "";
            sb.AppendLine($"      \"{id}\"{comma}");
        }
        sb.AppendLine("    ],");

        // Utilities
        sb.AppendLine("    \"utilities\": [");
        for (int i = 0; i < _utilitiesPool.Count; i++)
        {
            var u = _utilitiesPool[i];
            string id = u.Item != null ? u.Item.LootId.Value : "";
            string comma = i < _utilitiesPool.Count - 1 ? "," : "";
            sb.AppendLine($"      {{ \"lootId\": \"{id}\", \"stock\": {u.WeeklyStock} }}{comma}");
        }
        sb.AppendLine("    ],");

        // Demand candidates
        sb.AppendLine("    \"demandCandidates\": [");
        for (int i = 0; i < _demandCandidatesPool.Count; i++)
        {
            string id = _demandCandidatesPool[i] != null ? _demandCandidatesPool[i].LootId.Value : "";
            string comma = i < _demandCandidatesPool.Count - 1 ? "," : "";
            sb.AppendLine($"      \"{id}\"{comma}");
        }
        sb.AppendLine("    ]");

        sb.AppendLine("  },");

        // Special Modifiers
        sb.AppendLine("  \"specialModifiers\": [");
        for (int i = 0; i < _specialModifiers.Count; i++)
        {
            var m = _specialModifiers[i];
            string comma = i < _specialModifiers.Count - 1 ? "," : "";
            sb.AppendLine($"    {{ \"type\": \"{m.Type}\", \"prob\": {m.ProbabilityPercent.ToString(System.Globalization.CultureInfo.InvariantCulture)}, \"increment\": {m.ObjectValueIncrement}, \"description\": \"{m.Description}\" }}{comma}");
        }
        sb.AppendLine("  ]");

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Exports the JSON configuration to the backend configuration file.
    /// </summary>
    public bool ExportToBackendFile(string customPath = null)
    {
        string targetPath = customPath;
        if (string.IsNullOrEmpty(targetPath))
        {
            string basePath = Application.dataPath; // <root>/Project Grimhold/Assets
            targetPath = Path.GetFullPath(Path.Combine(basePath, "../../backend/src/config/commerceConfig.json"));
        }

        try
        {
            string dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = ExportToJson();
            File.WriteAllText(targetPath, json, new UTF8Encoding(false));
            Debug.Log($"[TownCommerceConfiguration] Successfully exported commerce configuration to {targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TownCommerceConfiguration] Failed to export commerce configuration to {targetPath}: {ex.Message}");
            return false;
        }
    }
}

[Serializable]
public struct PermanentCommerceOfferConfig
{
    public LootDefinition Item;
    [Tooltip("Explicit buy price, or -1 to auto-calculate as ceil(ObjectValue * BuyMultiplier)")]
    public int BuyPrice;
    public bool UnlimitedStock;
    public int StockQuantity;

    public PermanentCommerceOfferConfig(LootDefinition item, int buyPrice = -1, bool unlimited = true, int stock = 999)
    {
        Item = item;
        BuyPrice = buyPrice;
        UnlimitedStock = unlimited;
        StockQuantity = stock;
    }
}

[Serializable]
public struct UtilityCommerceOfferConfig
{
    public LootDefinition Item;
    [Tooltip("Weekly stock quota allocated to each individual player")]
    public int WeeklyStock;

    public UtilityCommerceOfferConfig(LootDefinition item, int weeklyStock = 1)
    {
        Item = item;
        WeeklyStock = weeklyStock;
    }
}

[Serializable]
public struct SpecialModifierConfigOption
{
    public string Type;
    public float ProbabilityPercent;
    public int ObjectValueIncrement;
    public string Description;

    public SpecialModifierConfigOption(string type, float prob, int increment, string description)
    {
        Type = type;
        ProbabilityPercent = prob;
        ObjectValueIncrement = increment;
        Description = description;
    }
}

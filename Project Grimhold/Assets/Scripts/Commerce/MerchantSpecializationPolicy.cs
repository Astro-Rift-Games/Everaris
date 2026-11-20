using System;
using System.Collections.Generic;

/// <summary>
/// Specialized merchant types in Town according to Doc 23 §3.
/// </summary>
public enum TownMerchantType
{
    General,
    Blacksmith,
    Arcanist
}

/// <summary>
/// Authoritative specialization policies for Town Merchants according to Doc 23 §3.
/// General Merchant buys all tradable items; Blacksmith trades physical weapons and armors;
/// Arcanist trades magical weapons. Training items (OV = 0, Tradable = false) are excluded.
/// </summary>
public static class MerchantSpecializationPolicy
{
    private static readonly HashSet<string> PhysicalWeapons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "arming_sword", "long_sword", "great_hammer", "rondel_dagger", "compound_bow", "light_crossbow", "rapier"
    };

    private static readonly HashSet<string> MagicalWeapons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "magic_wand", "spell_book", "magic_staff", "magic_sword", "magic_cinquedea"
    };

    public static bool IsPhysicalWeapon(string lootId) =>
        !string.IsNullOrEmpty(lootId) && PhysicalWeapons.Contains(lootId);

    public static bool IsMagicalWeapon(string lootId) =>
        !string.IsNullOrEmpty(lootId) && MagicalWeapons.Contains(lootId);

    public static bool IsAllowedToSellToMerchant(TownMerchantType? merchant, LootDefinition definition)
    {
        if (!merchant.HasValue)
        {
            return true;
        }

        if (definition == null || !definition.Tradable || definition.ObjectValue <= 0)
        {
            return false;
        }

        string id = definition.Id ?? string.Empty;

        switch (merchant.Value)
        {
            case TownMerchantType.General:
                // General Merchant buys ALL tradable items (Doc 23 §3.1)
                return true;

            case TownMerchantType.Blacksmith:
                // Blacksmith buys physical weapons and armors only (Doc 23 §3.2)
                return PhysicalWeapons.Contains(id) ||
                       definition.Category is LootCategory.Helmet or LootCategory.Armor or LootCategory.Gloves or LootCategory.Boots or LootCategory.Shield;

            case TownMerchantType.Arcanist:
                // Arcanist buys magical weapons only (Doc 23 §3.3)
                return MagicalWeapons.Contains(id);

            default:
                return true;
        }
    }

    public static bool IsAllowedToBuyFromMerchant(TownMerchantType? merchant, LootDefinition definition)
    {
        if (!merchant.HasValue)
        {
            return true;
        }

        if (definition == null || definition.BuyValuePerUnit <= 0)
        {
            return false;
        }

        string id = definition.Id ?? string.Empty;

        switch (merchant.Value)
        {
            case TownMerchantType.General:
            default:
                // General Merchant sells utilities, consumables, and general items (not specialized weapons/armor)
                return !PhysicalWeapons.Contains(id) && !MagicalWeapons.Contains(id) &&
                       definition.Category is not (LootCategory.Helmet or LootCategory.Armor or LootCategory.Gloves or LootCategory.Boots or LootCategory.Shield);

            case TownMerchantType.Blacksmith:
                // Blacksmith sells rotating physical weapons and armors (Doc 23 §3.2)
                return PhysicalWeapons.Contains(id) ||
                       definition.Category is LootCategory.Helmet or LootCategory.Armor or LootCategory.Gloves or LootCategory.Boots or LootCategory.Shield;

            case TownMerchantType.Arcanist:
                // Arcanist sells rotating magical weapons (Doc 23 §3.3)
                return MagicalWeapons.Contains(id);
        }
    }
}

using System;
using System.Collections.Generic;

/// <summary>
/// Client mirror of the Town NPC upgrade rules (Doc 23 §9) used only to preview. The backend
/// (backend/src/services/ItemUpgrades.js) stays authoritative; backend/tests/item.upgrades.parity.test.js
/// keeps the tables below in parity. Previewing never mutates or reserves an instance.
/// </summary>
public static class NpcUpgradeRules
{
    public const int ArmorMaxLevel = EquipmentInstance.MaxArmorUpgradeLevel;

    // Physical weapons the Blacksmith upgrades but never trades (backend UPGRADE_ONLY_PHYSICAL_WEAPONS).
    private static readonly HashSet<string> UpgradeOnlyPhysicalWeapons = new(StringComparer.OrdinalIgnoreCase)
    {
        "zweihander", "long_bow"
    };

    private readonly struct ScalingStep
    {
        public readonly WeaponScalingGrade From, To;
        public readonly int Gold;
        public readonly NpcUpgradeMaterialCost[] Materials;

        public ScalingStep(WeaponScalingGrade from, WeaponScalingGrade to, int gold, params NpcUpgradeMaterialCost[] materials)
        {
            From = from;
            To = to;
            Gold = gold;
            Materials = materials;
        }
    }

    private readonly struct ArmorStep
    {
        public readonly int Gold, Basic, Ancient;

        public ArmorStep(int gold, int basic, int ancient)
        {
            Gold = gold;
            Basic = basic;
            Ancient = ancient;
        }
    }

    // parity:primary-scaling-steps (Doc 23 §9.1)
    private static readonly ScalingStep[] PrimaryScalingSteps =
    {
        new(WeaponScalingGrade.None, WeaponScalingGrade.E, 60, new NpcUpgradeMaterialCost("RES_RUNE_FRAGMENT", 2)),
        new(WeaponScalingGrade.E, WeaponScalingGrade.D, 100, new NpcUpgradeMaterialCost("RES_RUNE_FRAGMENT", 3), new NpcUpgradeMaterialCost("RES_ETHER_DUST", 1)),
        new(WeaponScalingGrade.D, WeaponScalingGrade.C, 160, new NpcUpgradeMaterialCost("RES_RUNE_FRAGMENT", 4), new NpcUpgradeMaterialCost("RES_ETHER_DUST", 2)),
    };

    // parity:first-socket (Doc 23 §9.1)
    private const int FirstSocketGold = 180;
    private static readonly NpcUpgradeMaterialCost[] FirstSocketMaterials =
    {
        new NpcUpgradeMaterialCost("RES_ATTUNEMENT_GEM", 1), new NpcUpgradeMaterialCost("RES_RUNE_FRAGMENT", 2)
    };

    // parity:armor-steps; index = target level, each row is the transition from the previous level (Doc 23 §9.2).
    private static readonly ArmorStep[] ArmorSteps =
    {
        default,
        new(40, 2, 0),
        new(80, 3, 1),
        new(140, 4, 2),
    };

    private const string AncientArmorMaterial = "RES_ANCIENT_ARMOR";
    private static readonly int[] ArmorValueBonus = { 0, 15, 35, 60 };
    private const int FirstSocketValueBonus = 60;

    // parity:armor-family-materials (Doc 23 §9.2: Caballero, Ranger, Mago).
    private static readonly (string Prefix, string Material)[] ArmorFamilyMaterials =
    {
        ("heavy_armor_", "RES_BLACK_IRON"), ("light_armor_", "RES_BLACK_IRON"),
        ("army_ranger_", "RES_TANNED_LEATHER"), ("forest_ranger_", "RES_TANNED_LEATHER"),
        ("placeholder_", "RES_ETHER_DUST"), ("fire_mage_", "RES_ETHER_DUST"),
    };

    private static readonly NpcUpgradeOperation[] WeaponOperations = { NpcUpgradeOperation.PrimaryScaling, NpcUpgradeOperation.FirstSocket };
    private static readonly NpcUpgradeOperation[] ArmorOperations = { NpcUpgradeOperation.ArmorLevel };

    public static string WireName(NpcUpgradeOperation operation) => operation switch
    {
        NpcUpgradeOperation.PrimaryScaling => "primary_scaling",
        NpcUpgradeOperation.FirstSocket => "first_socket",
        _ => "armor_level"
    };

    /// <summary>Which merchant upgrades the item, or null when no Town NPC does.</summary>
    public static TownMerchantType? UpgradingMerchant(LootDefinition definition)
    {
        string id = definition?.Id;
        if (string.IsNullOrEmpty(id)) return null;
        if (definition.Category == LootCategory.Weapon)
        {
            if (MerchantSpecializationPolicy.IsPhysicalWeapon(id) || UpgradeOnlyPhysicalWeapons.Contains(id)) return TownMerchantType.Blacksmith;
            if (MerchantSpecializationPolicy.IsMagicalWeapon(id)) return TownMerchantType.Arcanist;
            return null;
        }
        return IsArmorPiece(definition.Category) && ArmorBasicMaterial(id) != null ? TownMerchantType.Blacksmith : null;
    }

    public static bool IsUpgradableBy(TownMerchantType merchant, LootDefinition definition) => UpgradingMerchant(definition) == merchant;

    public static IReadOnlyList<NpcUpgradeOperation> OperationsFor(LootDefinition definition)
    {
        if (UpgradingMerchant(definition) == null) return Array.Empty<NpcUpgradeOperation>();
        return definition.Category == LootCategory.Weapon ? WeaponOperations : ArmorOperations;
    }

    /// <summary>DefensaFinal = floor(DefensaBase × (1 + BonoMejora)); single source in ArmorUpgradeDefense.</summary>
    public static int UpgradedDefense(int baseDefense, int level) => ArmorUpgradeDefense.Apply(baseDefense, level);

    /// <summary>
    /// Projects one upgrade. Material counts come from the Inventory only; stash counts are used solely to
    /// tell the player that a missing material sits in the Stash.
    /// </summary>
    public static NpcUpgradePreview Preview(TownMerchantType merchant, NpcUpgradeOperation operation, NpcUpgradeSubject subject,
        int gold, IReadOnlyDictionary<string, int> inventoryCounts, IReadOnlyDictionary<string, int> stashCounts)
    {
        var definition = subject.Definition;
        var preview = new NpcUpgradePreview { Operation = operation, LootId = definition?.Id, GoldAvailable = gold };
        var owner = UpgradingMerchant(definition);
        bool isArmor = owner != null && definition.Category != LootCategory.Weapon;
        if (owner == null || isArmor != (operation == NpcUpgradeOperation.ArmorLevel))
        {
            preview.BlockReason = NpcUpgradeBlockReason.NotEligible;
            return preview;
        }

        int stepGold;
        NpcUpgradeMaterialCost[] materials;
        preview.BlockReason = isArmor ? ProjectArmor(preview, subject, out stepGold, out materials)
            : ProjectWeapon(preview, operation, subject, out stepGold, out materials);
        if (owner != merchant && preview.BlockReason == NpcUpgradeBlockReason.None) preview.BlockReason = NpcUpgradeBlockReason.WrongMerchant;
        if (materials == null) return preview;

        preview.GoldRequired = stepGold;
        var lines = new NpcUpgradeMaterialLine[materials.Length];
        bool missing = false;
        for (int i = 0; i < materials.Length; i++)
        {
            lines[i] = new NpcUpgradeMaterialLine(materials[i].LootId, materials[i].Amount,
                Count(inventoryCounts, materials[i].LootId), Count(stashCounts, materials[i].LootId));
            missing |= !lines[i].IsSatisfied;
        }
        preview.Materials = lines;
        if (preview.BlockReason != NpcUpgradeBlockReason.None) return preview;
        if (gold < stepGold) preview.BlockReason = NpcUpgradeBlockReason.InsufficientGold;
        else if (missing) preview.BlockReason = NpcUpgradeBlockReason.MissingMaterials;
        return preview;
    }

    private static NpcUpgradeBlockReason ProjectWeapon(NpcUpgradePreview preview, NpcUpgradeOperation operation, NpcUpgradeSubject subject,
        out int gold, out NpcUpgradeMaterialCost[] materials)
    {
        gold = 0;
        materials = null;
        var grade = subject.Weapon.HasValue && subject.Weapon.Value.HasPrimary ? subject.Weapon.Value.Primary.Grade : WeaponScalingGrade.None;
        int sockets = subject.Weapon?.GemSocketCount ?? 0;
        int baseValue = subject.Definition.ObjectValue;
        preview.CurrentGrade = preview.ProjectedGrade = grade;
        preview.CurrentSocketCount = preview.ProjectedSocketCount = sockets;
        preview.CurrentObjectValue = preview.ProjectedObjectValue = WeaponValue(baseValue, grade, sockets);

        if (operation == NpcUpgradeOperation.FirstSocket)
        {
            if (sockets > 0) return NpcUpgradeBlockReason.SocketExists;
            preview.ProjectedSocketCount = 1;
            preview.ProjectedObjectValue = WeaponValue(baseValue, grade, 1);
            gold = FirstSocketGold;
            materials = FirstSocketMaterials;
            return NpcUpgradeBlockReason.None;
        }

        if (grade >= WeaponScalingGrade.B) return NpcUpgradeBlockReason.GradeLocked;
        foreach (var step in PrimaryScalingSteps)
        {
            if (step.From != grade) continue;
            preview.ProjectedGrade = step.To;
            preview.ProjectedObjectValue = WeaponValue(baseValue, step.To, sockets);
            gold = step.Gold;
            materials = step.Materials;
            return NpcUpgradeBlockReason.None;
        }
        return NpcUpgradeBlockReason.LimitReached;
    }

    private static NpcUpgradeBlockReason ProjectArmor(NpcUpgradePreview preview, NpcUpgradeSubject subject,
        out int gold, out NpcUpgradeMaterialCost[] materials)
    {
        gold = 0;
        materials = null;
        int level = Math.Clamp(subject.ArmorLevel, 0, ArmorMaxLevel);
        var armor = subject.Definition.ArmorDefinition;
        int physical = armor != null ? armor.PhysicalDefense : 0;
        int magical = armor != null ? armor.MagicalDefense : 0;
        int baseValue = subject.Definition.ObjectValue;
        preview.CurrentArmorLevel = preview.ProjectedArmorLevel = level;
        preview.CurrentPhysicalDefense = preview.ProjectedPhysicalDefense = UpgradedDefense(physical, level);
        preview.CurrentMagicalDefense = preview.ProjectedMagicalDefense = UpgradedDefense(magical, level);
        preview.CurrentObjectValue = preview.ProjectedObjectValue = baseValue + ArmorValueBonus[level];
        if (level >= ArmorMaxLevel) return NpcUpgradeBlockReason.LimitReached;

        int target = level + 1;
        var step = ArmorSteps[target];
        preview.ProjectedArmorLevel = target;
        preview.ProjectedPhysicalDefense = UpgradedDefense(physical, target);
        preview.ProjectedMagicalDefense = UpgradedDefense(magical, target);
        preview.ProjectedObjectValue = baseValue + ArmorValueBonus[target];
        gold = step.Gold;
        string basic = ArmorBasicMaterial(subject.Definition.Id);
        materials = step.Ancient > 0
            ? new[] { new NpcUpgradeMaterialCost(basic, step.Basic), new NpcUpgradeMaterialCost(AncientArmorMaterial, step.Ancient) }
            : new[] { new NpcUpgradeMaterialCost(basic, step.Basic) };
        return NpcUpgradeBlockReason.None;
    }

    // Doc 23 §9.3: E +20, D +35, C +50 replace each other; B/A/S bonuses belong to GD-09 (0 here, as in the backend).
    private static int WeaponValue(int baseValue, WeaponScalingGrade grade, int sockets) => baseValue + grade switch
    {
        WeaponScalingGrade.E => 20,
        WeaponScalingGrade.D => 35,
        WeaponScalingGrade.C => 50,
        _ => 0
    } + (sockets > 0 ? FirstSocketValueBonus : 0);

    private static bool IsArmorPiece(LootCategory category) =>
        category is LootCategory.Helmet or LootCategory.Armor or LootCategory.Gloves or LootCategory.Boots;

    private static string ArmorBasicMaterial(string lootId)
    {
        foreach (var (prefix, material) in ArmorFamilyMaterials)
            if (lootId.StartsWith(prefix, StringComparison.Ordinal)) return material;
        return null;
    }

    private static int Count(IReadOnlyDictionary<string, int> counts, string lootId) =>
        counts != null && counts.TryGetValue(lootId, out int amount) ? amount : 0;
}

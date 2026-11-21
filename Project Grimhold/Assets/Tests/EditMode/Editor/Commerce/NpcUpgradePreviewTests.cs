#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

/// <summary>TASK-303 S1-S4, S6, S8: Town NPC upgrade eligibility, projection and limits (Doc 23 §§9, 11, 13).</summary>
public sealed class NpcUpgradePreviewTests
{
    private const string NpcLimitMessage = "Límite de mejora del NPC alcanzado; grados superiores disponibles en Dungeon";
    private LootDefinitionCatalog _catalog;

    [OneTimeSetUp]
    public void LoadCatalog() => _catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
        "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");

    private LootDefinition Def(string id)
    {
        Assert.That(_catalog.TryGet(id, out var definition), Is.True, id);
        return definition;
    }

    private static WeaponInstanceModifiers Weapon(WeaponScalingGrade grade, int sockets = 0, WeaponScalingGrade secondary = WeaponScalingGrade.None)
    {
        WeaponScalingModifier primary = default, second = default;
        if (grade != WeaponScalingGrade.None) WeaponScalingModifier.TryCreate(CharacterAttribute.Dexterity, grade, out primary, out _);
        if (secondary != WeaponScalingGrade.None) WeaponScalingModifier.TryCreate(CharacterAttribute.Luck, secondary, out second, out _);
        Assert.That(WeaponInstanceModifiers.TryCreate(primary, second, CharacterAttribute.Dexterity, sockets, default, default,
            out var modifiers, out string error), Is.True, error);
        return modifiers;
    }

    private static Dictionary<string, int> Counts(params (string id, int amount)[] entries)
    {
        var counts = new Dictionary<string, int>();
        foreach (var (id, amount) in entries) counts[id] = amount;
        return counts;
    }

    [TestCase(TownMerchantType.Blacksmith, "rapier", true)]
    [TestCase(TownMerchantType.Blacksmith, "zweihander", true)]
    [TestCase(TownMerchantType.Blacksmith, "long_bow", true)]
    [TestCase(TownMerchantType.Blacksmith, "heavy_armor_breastplate", true)]
    [TestCase(TownMerchantType.Blacksmith, "fire_mage_garb", true)]
    [TestCase(TownMerchantType.Blacksmith, "magic_wand", false)]
    [TestCase(TownMerchantType.Arcanist, "magic_wand", true)]
    [TestCase(TownMerchantType.Arcanist, "rapier", false)]
    [TestCase(TownMerchantType.Arcanist, "heavy_armor_breastplate", false)]
    [TestCase(TownMerchantType.General, "rapier", false)]
    public void ListingFilter_BlacksmithPhysicalAndArmor_ArcanistMagical(TownMerchantType merchant, string id, bool expected) =>
        Assert.That(NpcUpgradeRules.IsUpgradableBy(merchant, Def(id)), Is.EqualTo(expected));

    [Test]
    public void UpgradeOnlyWeapons_DoNotChangeBlacksmithBuyAndSell()
    {
        Assert.That(MerchantSpecializationPolicy.IsAllowedToSellToMerchant(TownMerchantType.Blacksmith, Def("zweihander")), Is.False);
        Assert.That(MerchantSpecializationPolicy.IsAllowedToBuyFromMerchant(TownMerchantType.Blacksmith, Def("long_bow")), Is.False);
    }

    [Test]
    public void WeaponScaling_DtoC_ProjectsGradeCostValueAndKeepsSecondary()
    {
        var subject = new NpcUpgradeSubject(Def("rapier"), Weapon(WeaponScalingGrade.D, secondary: WeaponScalingGrade.E), 0);
        var preview = NpcUpgradeRules.Preview(TownMerchantType.Blacksmith, NpcUpgradeOperation.PrimaryScaling, subject, 500,
            Counts(("RES_RUNE_FRAGMENT", 4), ("RES_ETHER_DUST", 2)), Counts());

        Assert.That(preview.CurrentGrade, Is.EqualTo(WeaponScalingGrade.D));
        Assert.That(preview.ProjectedGrade, Is.EqualTo(WeaponScalingGrade.C));
        Assert.That(preview.GoldRequired, Is.EqualTo(160));
        Assert.That(preview.ProjectedObjectValue - preview.CurrentObjectValue, Is.EqualTo(50 - 35));
        Assert.That(preview.Materials.Count, Is.EqualTo(2));
        Assert.That(preview.Materials[0].LootId, Is.EqualTo("RES_RUNE_FRAGMENT"));
        Assert.That(preview.Materials[0].Required, Is.EqualTo(4));
        Assert.That(preview.Materials[1].LootId, Is.EqualTo("RES_ETHER_DUST"));
        Assert.That(preview.Materials[1].Required, Is.EqualTo(2));
        Assert.That(preview.CanConfirm, Is.True);
        Assert.That(subject.Weapon.Value.Secondary.Grade, Is.EqualTo(WeaponScalingGrade.E), "preview never mutates the instance");
    }

    [Test]
    public void WeaponScaling_NoneToE_CostsSixtyAndTwoRuneFragments()
    {
        var preview = NpcUpgradeRules.Preview(TownMerchantType.Arcanist, NpcUpgradeOperation.PrimaryScaling,
            new NpcUpgradeSubject(Def("magic_wand"), null, 0), 60, Counts(("RES_RUNE_FRAGMENT", 2)), Counts());
        Assert.That(preview.CurrentGrade, Is.EqualTo(WeaponScalingGrade.None));
        Assert.That(preview.ProjectedGrade, Is.EqualTo(WeaponScalingGrade.E));
        Assert.That(preview.GoldRequired, Is.EqualTo(60));
        Assert.That(preview.Materials.Count, Is.EqualTo(1));
        Assert.That(preview.CanConfirm, Is.True);
    }

    [Test]
    public void FirstSocket_ProjectsOneSocketAndSixtyValue()
    {
        var preview = NpcUpgradeRules.Preview(TownMerchantType.Blacksmith, NpcUpgradeOperation.FirstSocket,
            new NpcUpgradeSubject(Def("rapier"), Weapon(WeaponScalingGrade.E), 0), 180,
            Counts(("RES_ATTUNEMENT_GEM", 1), ("RES_RUNE_FRAGMENT", 2)), Counts());
        Assert.That(preview.CurrentSocketCount, Is.EqualTo(0));
        Assert.That(preview.ProjectedSocketCount, Is.EqualTo(1));
        Assert.That(preview.GoldRequired, Is.EqualTo(180));
        Assert.That(preview.ProjectedObjectValue - preview.CurrentObjectValue, Is.EqualTo(60));
        Assert.That(preview.CanConfirm, Is.True);
    }

    [Test]
    public void Armor_PlusTwoToPlusThree_ProjectsFlooredDefenseValueAndFamilyMaterials()
    {
        var definition = Def("heavy_armor_breastplate");
        var preview = NpcUpgradeRules.Preview(TownMerchantType.Blacksmith, NpcUpgradeOperation.ArmorLevel,
            new NpcUpgradeSubject(definition, null, 2), 140, Counts(("RES_BLACK_IRON", 4), ("RES_ANCIENT_ARMOR", 2)), Counts());
        int physical = definition.ArmorDefinition.PhysicalDefense;
        Assert.That(preview.CurrentArmorLevel, Is.EqualTo(2));
        Assert.That(preview.ProjectedArmorLevel, Is.EqualTo(3));
        Assert.That(preview.CurrentPhysicalDefense, Is.EqualTo(physical * 110 / 100));
        Assert.That(preview.ProjectedPhysicalDefense, Is.EqualTo(physical * 115 / 100));
        Assert.That(preview.ProjectedObjectValue, Is.EqualTo(definition.ObjectValue + 60));
        Assert.That(preview.GoldRequired, Is.EqualTo(140));
        Assert.That(preview.Materials[0].LootId, Is.EqualTo("RES_BLACK_IRON"));
        Assert.That(preview.Materials[0].Required, Is.EqualTo(4));
        Assert.That(preview.Materials[1].LootId, Is.EqualTo("RES_ANCIENT_ARMOR"));
        Assert.That(preview.Materials[1].Required, Is.EqualTo(2));
        Assert.That(preview.CanConfirm, Is.True);
    }

    [TestCase(0, 26)]
    [TestCase(1, 27)]
    [TestCase(2, 28)]
    [TestCase(3, 29)]
    public void UpgradedDefense_FloorsCumulativePercentOnBase(int level, int expected) =>
        Assert.That(NpcUpgradeRules.UpgradedDefense(26, level), Is.EqualTo(expected));

    [Test]
    public void Armor_PlusOne_UsesTannedLeatherForRangerFamily()
    {
        var preview = NpcUpgradeRules.Preview(TownMerchantType.Blacksmith, NpcUpgradeOperation.ArmorLevel,
            new NpcUpgradeSubject(Def("army_ranger_armor"), null, 0), 40, Counts(("RES_TANNED_LEATHER", 2)), Counts());
        Assert.That(preview.Materials.Count, Is.EqualTo(1));
        Assert.That(preview.Materials[0].LootId, Is.EqualTo("RES_TANNED_LEATHER"));
        Assert.That(preview.Materials[0].Required, Is.EqualTo(2));
        Assert.That(preview.CanConfirm, Is.True);
    }

    [Test]
    public void MaterialsOnlyInStash_AreNotCountedAndAreFlagged()
    {
        var preview = NpcUpgradeRules.Preview(TownMerchantType.Blacksmith, NpcUpgradeOperation.PrimaryScaling,
            new NpcUpgradeSubject(Def("rapier"), null, 0), 999, Counts(("RES_RUNE_FRAGMENT", 1)), Counts(("RES_RUNE_FRAGMENT", 5)));
        Assert.That(preview.CanConfirm, Is.False);
        Assert.That(preview.BlockReason, Is.EqualTo(NpcUpgradeBlockReason.MissingMaterials));
        Assert.That(preview.Materials[0].InInventory, Is.EqualTo(1));
        Assert.That(preview.Materials[0].IsSatisfied, Is.False);
        Assert.That(preview.Materials[0].AvailableInStash, Is.True);
        Assert.That(NpcUpgradeMessages.For(preview), Is.EqualTo("Faltan materiales en el Inventario; los del Stash no se usan para mejorar."));
    }

    [Test]
    public void InsufficientGold_BlocksWithExplicitMessage()
    {
        var preview = NpcUpgradeRules.Preview(TownMerchantType.Blacksmith, NpcUpgradeOperation.ArmorLevel,
            new NpcUpgradeSubject(Def("heavy_armor_breastplate"), null, 0), 39, Counts(("RES_BLACK_IRON", 2)), Counts());
        Assert.That(preview.BlockReason, Is.EqualTo(NpcUpgradeBlockReason.InsufficientGold));
        Assert.That(NpcUpgradeMessages.For(preview), Is.EqualTo("Oro insuficiente: se requieren 40 y tienes 39."));
    }

    [TestCase(WeaponScalingGrade.C, 0, NpcUpgradeOperation.PrimaryScaling, NpcUpgradeBlockReason.LimitReached, NpcLimitMessage)]
    [TestCase(WeaponScalingGrade.B, 0, NpcUpgradeOperation.PrimaryScaling, NpcUpgradeBlockReason.GradeLocked, "El NPC no modifica escalados de grado B, A o S; grados superiores disponibles en Dungeon")]
    [TestCase(WeaponScalingGrade.S, 0, NpcUpgradeOperation.PrimaryScaling, NpcUpgradeBlockReason.GradeLocked, "El NPC no modifica escalados de grado B, A o S; grados superiores disponibles en Dungeon")]
    [TestCase(WeaponScalingGrade.E, 1, NpcUpgradeOperation.FirstSocket, NpcUpgradeBlockReason.SocketExists, "El arma ya tiene una ranura; el NPC solo crea la primera")]
    public void WeaponAtLimit_StaysVisibleButBlockedWithReason(WeaponScalingGrade grade, int sockets, NpcUpgradeOperation operation,
        NpcUpgradeBlockReason reason, string message)
    {
        var preview = NpcUpgradeRules.Preview(TownMerchantType.Blacksmith, operation,
            new NpcUpgradeSubject(Def("rapier"), Weapon(grade, sockets), 0), 9999, Counts(), Counts());
        Assert.That(preview.CanConfirm, Is.False);
        Assert.That(preview.BlockReason, Is.EqualTo(reason));
        Assert.That(preview.Materials, Is.Empty, "no next step to price at the limit");
        Assert.That(NpcUpgradeMessages.For(preview), Is.EqualTo(message));
    }

    [Test]
    public void ArmorPlusThree_IsBlockedAtNpcLimit()
    {
        var preview = NpcUpgradeRules.Preview(TownMerchantType.Blacksmith, NpcUpgradeOperation.ArmorLevel,
            new NpcUpgradeSubject(Def("heavy_armor_breastplate"), null, 3), 9999, Counts(), Counts());
        Assert.That(preview.BlockReason, Is.EqualTo(NpcUpgradeBlockReason.LimitReached));
        Assert.That(preview.ProjectedArmorLevel, Is.EqualTo(3));
        Assert.That(NpcUpgradeMessages.For(preview), Is.EqualTo("Límite de mejora del NPC alcanzado (+3)"));
    }

    [Test]
    public void WrongMerchant_IsBlocked()
    {
        var preview = NpcUpgradeRules.Preview(TownMerchantType.Arcanist, NpcUpgradeOperation.PrimaryScaling,
            new NpcUpgradeSubject(Def("rapier"), null, 0), 999, Counts(("RES_RUNE_FRAGMENT", 9)), Counts());
        Assert.That(preview.BlockReason, Is.EqualTo(NpcUpgradeBlockReason.WrongMerchant));
        Assert.That(NpcUpgradeMessages.For(preview), Is.EqualTo("Este mercader no mejora este objeto"));
    }

    [Test]
    public void OperationsFor_WeaponsOfferScalingAndSocket_ArmorOffersLevel()
    {
        Assert.That(NpcUpgradeRules.OperationsFor(Def("rapier")),
            Is.EqualTo(new[] { NpcUpgradeOperation.PrimaryScaling, NpcUpgradeOperation.FirstSocket }));
        Assert.That(NpcUpgradeRules.OperationsFor(Def("heavy_armor_breastplate")), Is.EqualTo(new[] { NpcUpgradeOperation.ArmorLevel }));
        Assert.That(NpcUpgradeRules.OperationsFor(Def("RES_RUNE_FRAGMENT")), Is.Empty);
    }
}
#endif

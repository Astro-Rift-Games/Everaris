#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

/// <summary>TASK-303 T5 (S2, S3, S6, S8): what the Mejoras tab shows for each target and preview (Doc 23 §11).</summary>
public sealed class TownUpgradeViewModelTests
{
    private LootDefinitionCatalog _catalog;

    [OneTimeSetUp]
    public void LoadCatalog() => _catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
        "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");

    private LootDefinition Def(string id)
    {
        Assert.That(_catalog.TryGet(id, out var definition), Is.True, id);
        return definition;
    }

    private static Dictionary<string, int> Counts(params (string id, int amount)[] entries) =>
        entries.ToDictionary(e => e.id, e => e.amount);

    private NpcUpgradePreview Preview(string id, NpcUpgradeOperation operation, WeaponInstanceModifiers? weapon, int level, int gold,
        Dictionary<string, int> inventory, Dictionary<string, int> stash) =>
        NpcUpgradeRules.Preview(TownMerchantType.Blacksmith, operation, new NpcUpgradeSubject(Def(id), weapon, level), gold, inventory, stash);

    private static WeaponInstanceModifiers Weapon(WeaponScalingGrade grade, int sockets = 0)
    {
        WeaponScalingModifier primary = default;
        if (grade != WeaponScalingGrade.None) WeaponScalingModifier.TryCreate(CharacterAttribute.Dexterity, grade, out primary, out _);
        Assert.That(WeaponInstanceModifiers.TryCreate(primary, default, CharacterAttribute.Dexterity, sockets, default, default,
            out var modifiers, out string error), Is.True, error);
        return modifiers;
    }

    [Test]
    public void ArmorPreview_ShowsLevelDefenseValueGoldAndMaterialsAgainstInventory()
    {
        var definition = Def("heavy_armor_breastplate");
        var preview = Preview("heavy_armor_breastplate", NpcUpgradeOperation.ArmorLevel, null, 2, 500,
            Counts(("RES_BLACK_IRON", 4), ("RES_ANCIENT_ARMOR", 1)), Counts(("RES_ANCIENT_ARMOR", 3)));
        int physical = definition.ArmorDefinition.PhysicalDefense;
        int magical = definition.ArmorDefinition.MagicalDefense;

        var lines = TownUpgradeViewModel.PreviewLines(preview, _catalog);

        Assert.That(lines, Is.EqualTo(new[]
        {
            "Nivel: +2 -> +3",
            $"Defensa física: {physical * 110 / 100} -> {physical * 115 / 100}",
            $"Defensa mágica: {magical * 110 / 100} -> {magical * 115 / 100}",
            $"Valor: {definition.ObjectValue + 35} -> {definition.ObjectValue + 60}",
            "Oro: 140 (tienes 500)",
            $"{Def("RES_BLACK_IRON").DisplayName}: 4/4 en Inventario",
            $"{Def("RES_ANCIENT_ARMOR").DisplayName}: 1/2 en Inventario (3 en Stash, no se usan)",
            "Faltan materiales en el Inventario; los del Stash no se usan para mejorar."
        }));
    }

    [Test]
    public void WeaponScalingPreview_ShowsGradeTransition()
    {
        var preview = Preview("rapier", NpcUpgradeOperation.PrimaryScaling, null, 0, 60, Counts(("RES_RUNE_FRAGMENT", 2)), Counts());
        var lines = TownUpgradeViewModel.PreviewLines(preview, _catalog);
        Assert.That(lines[0], Is.EqualTo("Escalado principal: sin escalado -> E"));
        Assert.That(lines, Does.Contain("Oro: 60 (tienes 60)"));
        Assert.That(lines.Last(), Is.EqualTo($"{Def("RES_RUNE_FRAGMENT").DisplayName}: 2/2 en Inventario"));
    }

    [Test]
    public void SocketPreview_ShowsSocketTransition()
    {
        var preview = Preview("rapier", NpcUpgradeOperation.FirstSocket, Weapon(WeaponScalingGrade.E), 0, 999,
            Counts(("RES_ATTUNEMENT_GEM", 1), ("RES_RUNE_FRAGMENT", 2)), Counts());
        Assert.That(TownUpgradeViewModel.PreviewLines(preview, _catalog)[0], Is.EqualTo("Ranuras: 0 -> 1"));
    }

    [Test]
    public void LimitPreview_ShowsCurrentStateAndReasonWithoutCosts()
    {
        var preview = Preview("rapier", NpcUpgradeOperation.PrimaryScaling, Weapon(WeaponScalingGrade.C), 0, 999, Counts(), Counts());
        Assert.That(TownUpgradeViewModel.PreviewLines(preview, _catalog), Is.EqualTo(new[]
        {
            "Escalado principal: C",
            NpcUpgradeMessages.NpcLimit
        }));
    }

    [Test]
    public void Operation_DisabledAtLimitWithReason_EnabledWhenConfirmable()
    {
        var limit = Preview("heavy_armor_breastplate", NpcUpgradeOperation.ArmorLevel, null, 3, 999, Counts(), Counts());
        var blocked = TownUpgradeViewModel.Operation(limit);
        Assert.That(blocked.Label, Is.EqualTo("Reforzar armadura"));
        Assert.That(blocked.Enabled, Is.False);
        Assert.That(blocked.Reason, Is.EqualTo("Límite de mejora del NPC alcanzado (+3)"));

        var ready = TownUpgradeViewModel.Operation(Preview("rapier", NpcUpgradeOperation.FirstSocket, null, 0, 999,
            Counts(("RES_ATTUNEMENT_GEM", 1), ("RES_RUNE_FRAGMENT", 2)), Counts()));
        Assert.That(ready.Label, Is.EqualTo("Crear ranura"));
        Assert.That(ready.Enabled, Is.True);
        Assert.That(ready.Reason, Is.Empty);
        Assert.That(TownUpgradeViewModel.Operation(Preview("rapier", NpcUpgradeOperation.PrimaryScaling, null, 0, 0, Counts(), Counts())).Label,
            Is.EqualTo("Mejorar escalado"));
    }

    [Test]
    public void RowTitle_TagsEquippedAndCurrentState()
    {
        Assert.That(TownUpgradeViewModel.RowTitle(Def("heavy_armor_breastplate"), new EquipmentInstance(null, null, 2), true),
            Is.EqualTo($"{Def("heavy_armor_breastplate").DisplayName} +2 (Equipado)"));
        Assert.That(TownUpgradeViewModel.RowTitle(Def("rapier"), new EquipmentInstance(null, Weapon(WeaponScalingGrade.D, 1)), false),
            Is.EqualTo($"{Def("rapier").DisplayName} [D, 1 ranura]"));
        Assert.That(TownUpgradeViewModel.RowTitle(Def("rapier"), new EquipmentInstance(null), false),
            Is.EqualTo(Def("rapier").DisplayName));
    }
}
#endif

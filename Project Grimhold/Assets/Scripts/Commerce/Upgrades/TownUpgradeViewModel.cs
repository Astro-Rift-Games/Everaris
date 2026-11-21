using System.Collections.Generic;

/// <summary>
/// Pure presentation copy for the Mejoras tab: row titles, operation buttons and preview lines built from
/// NpcUpgradePreview. Holds no state and never mutates the instance (Doc 23 §11).
/// </summary>
public static class TownUpgradeViewModel
{
    public static string OperationLabel(NpcUpgradeOperation operation) => operation switch
    {
        NpcUpgradeOperation.PrimaryScaling => "Mejorar escalado",
        NpcUpgradeOperation.FirstSocket => "Crear ranura",
        _ => "Reforzar armadura"
    };

    public static TownUpgradeOperationViewModel Operation(NpcUpgradePreview preview) =>
        new(preview.Operation, OperationLabel(preview.Operation), preview.CanConfirm, NpcUpgradeMessages.For(preview));

    /// <summary>Display name plus current state, e.g. "Peto +2 (Equipado)" or "Estoque [D, 1 ranura]".</summary>
    public static string RowTitle(LootDefinition definition, EquipmentInstance instance, bool isEquipped)
    {
        string title = definition != null ? definition.DisplayName : string.Empty;
        if (instance != null && instance.ArmorUpgradeLevel > 0) title += $" +{instance.ArmorUpgradeLevel}";
        if (instance?.Modifiers is { } weapon)
        {
            var parts = new List<string>(2);
            if (weapon.HasPrimary && weapon.Primary.Grade != WeaponScalingGrade.None) parts.Add(weapon.Primary.Grade.ToString());
            if (weapon.GemSocketCount > 0) parts.Add(weapon.GemSocketCount == 1 ? "1 ranura" : $"{weapon.GemSocketCount} ranuras");
            if (parts.Count > 0) title += $" [{string.Join(", ", parts)}]";
        }
        return isEquipped ? title + " (Equipado)" : title;
    }

    /// <summary>Current -> projected state, then gold and Inventory materials, then the block reason if any.</summary>
    public static IReadOnlyList<string> PreviewLines(NpcUpgradePreview preview, LootDefinitionCatalog catalog)
    {
        var lines = new List<string>(8);
        if (preview == null) return lines;
        bool hasStep = preview.Materials.Count > 0;
        switch (preview.Operation)
        {
            case NpcUpgradeOperation.PrimaryScaling:
                lines.Add(hasStep ? $"Escalado principal: {Grade(preview.CurrentGrade)} -> {Grade(preview.ProjectedGrade)}"
                    : $"Escalado principal: {Grade(preview.CurrentGrade)}");
                break;
            case NpcUpgradeOperation.FirstSocket:
                lines.Add(hasStep ? $"Ranuras: {preview.CurrentSocketCount} -> {preview.ProjectedSocketCount}"
                    : $"Ranuras: {preview.CurrentSocketCount}");
                break;
            default:
                lines.Add(Transition("Nivel", $"+{preview.CurrentArmorLevel}", $"+{preview.ProjectedArmorLevel}", hasStep));
                lines.Add(Transition("Defensa física", preview.CurrentPhysicalDefense.ToString(), preview.ProjectedPhysicalDefense.ToString(), hasStep));
                lines.Add(Transition("Defensa mágica", preview.CurrentMagicalDefense.ToString(), preview.ProjectedMagicalDefense.ToString(), hasStep));
                break;
        }
        if (hasStep)
        {
            lines.Add($"Valor: {preview.CurrentObjectValue} -> {preview.ProjectedObjectValue}");
            lines.Add($"Oro: {preview.GoldRequired} (tienes {preview.GoldAvailable})");
            foreach (var material in preview.Materials) lines.Add(MaterialLine(material, catalog));
        }
        string reason = NpcUpgradeMessages.For(preview);
        if (!string.IsNullOrEmpty(reason)) lines.Add(reason);
        return lines;
    }

    private static string MaterialLine(in NpcUpgradeMaterialLine line, LootDefinitionCatalog catalog)
    {
        string name = catalog != null && catalog.TryGet(line.LootId, out var definition) ? definition.DisplayName : line.LootId;
        string text = $"{name}: {line.InInventory}/{line.Required} en Inventario";
        return !line.IsSatisfied && line.InStash > 0 ? text + $" ({line.InStash} en Stash, no se usan)" : text;
    }

    private static string Transition(string label, string current, string projected, bool hasStep) =>
        hasStep ? $"{label}: {current} -> {projected}" : $"{label}: {current}";

    private static string Grade(WeaponScalingGrade grade) => grade == WeaponScalingGrade.None ? "sin escalado" : grade.ToString();
}

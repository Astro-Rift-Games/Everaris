/// <summary>Pure full-state projection from equipped armor definitions to effective modifiers.</summary>
public static class EquipmentStatisticsCalculator
{
    /// <summary>Plain (+0) pieces; kept for callers without instance data (Raid avatar, TASK-303 L6).</summary>
    public static bool TryCalculate(
        ArmorDefinition helmet,
        ArmorDefinition armor,
        ArmorDefinition gloves,
        ArmorDefinition boots,
        out EquipmentStatisticsModifiers modifiers,
        out EquipmentStatisticsCalculationFailure failure) =>
        TryCalculate(new ArmorPieceStatistics(helmet), new ArmorPieceStatistics(armor), new ArmorPieceStatistics(gloves),
            new ArmorPieceStatistics(boots), out modifiers, out failure);

    /// <summary>Upgraded pieces contribute floor(base × (1 + bonus)) defense each (Doc 23 §9.2).</summary>
    public static bool TryCalculate(
        in ArmorPieceStatistics helmet,
        in ArmorPieceStatistics armor,
        in ArmorPieceStatistics gloves,
        in ArmorPieceStatistics boots,
        out EquipmentStatisticsModifiers modifiers,
        out EquipmentStatisticsCalculationFailure failure)
    {
        modifiers = default;
        failure = EquipmentStatisticsCalculationFailure.None;

        long physicalDefense = 0;
        long magicalDefense = 0;
        long maximumHealthModifier = 0;
        long maximumStaminaModifier = 0;
        long maximumManaModifier = 0;
        long knockbackResistance = 0;

        if (!TryAccumulate(helmet, ref physicalDefense, ref magicalDefense,
                ref maximumHealthModifier, ref maximumStaminaModifier, ref maximumManaModifier, ref knockbackResistance, out failure) ||
            !TryAccumulate(armor, ref physicalDefense, ref magicalDefense,
                ref maximumHealthModifier, ref maximumStaminaModifier, ref maximumManaModifier, ref knockbackResistance, out failure) ||
            !TryAccumulate(gloves, ref physicalDefense, ref magicalDefense,
                ref maximumHealthModifier, ref maximumStaminaModifier, ref maximumManaModifier, ref knockbackResistance, out failure) ||
            !TryAccumulate(boots, ref physicalDefense, ref magicalDefense,
                ref maximumHealthModifier, ref maximumStaminaModifier, ref maximumManaModifier, ref knockbackResistance, out failure))
        {
            return false;
        }

        if (physicalDefense > int.MaxValue)
        {
            failure = EquipmentStatisticsCalculationFailure.PhysicalDefenseOverflow;
            return false;
        }

        if (magicalDefense > int.MaxValue)
        {
            failure = EquipmentStatisticsCalculationFailure.MagicalDefenseOverflow;
            return false;
        }

        if (maximumHealthModifier > int.MaxValue)
        {
            failure = EquipmentStatisticsCalculationFailure.MaximumHealthModifierOverflow;
            return false;
        }

        if (maximumStaminaModifier > int.MaxValue)
        {
            failure = EquipmentStatisticsCalculationFailure.MaximumStaminaModifierOverflow;
            return false;
        }

        if (maximumManaModifier > int.MaxValue)
        {
            failure = EquipmentStatisticsCalculationFailure.MaximumManaModifierOverflow;
            return false;
        }

        if (knockbackResistance > int.MaxValue)
        {
            failure = EquipmentStatisticsCalculationFailure.KnockbackResistanceOverflow;
            return false;
        }

        modifiers = new EquipmentStatisticsModifiers(
            (int)physicalDefense,
            (int)magicalDefense,
            (int)maximumHealthModifier,
            (int)maximumStaminaModifier,
            (int)maximumManaModifier,
            (int)knockbackResistance);
        return true;
    }

    private static bool TryAccumulate(
        in ArmorPieceStatistics piece,
        ref long physicalDefense,
        ref long magicalDefense,
        ref long maximumHealthModifier,
        ref long maximumStaminaModifier,
        ref long maximumManaModifier,
        ref long knockbackResistance,
        out EquipmentStatisticsCalculationFailure failure)
    {
        failure = EquipmentStatisticsCalculationFailure.None;
        ArmorDefinition definition = piece.Definition;
        if (definition == null)
        {
            return true;
        }

        if (!definition.TryValidate(out _) || piece.UpgradeLevel < 0 || piece.UpgradeLevel > EquipmentInstance.MaxArmorUpgradeLevel)
        {
            failure = EquipmentStatisticsCalculationFailure.InvalidArmorDefinition;
            return false;
        }

        physicalDefense += ArmorUpgradeDefense.Apply(definition.PhysicalDefense, piece.UpgradeLevel);
        magicalDefense += ArmorUpgradeDefense.Apply(definition.MagicalDefense, piece.UpgradeLevel);
        knockbackResistance += definition.KnockbackResistance;

        MaximumResourceModifier resourceModifier = definition.MaximumResourceModifier;
        switch (resourceModifier.Resource)
        {
            case MaximumResourceType.Health:
                maximumHealthModifier += resourceModifier.Amount;
                break;
            case MaximumResourceType.Stamina:
                maximumStaminaModifier += resourceModifier.Amount;
                break;
            case MaximumResourceType.Mana:
                maximumManaModifier += resourceModifier.Amount;
                break;
        }

        return true;
    }
}

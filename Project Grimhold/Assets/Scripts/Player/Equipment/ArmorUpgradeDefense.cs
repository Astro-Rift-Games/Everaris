using System;

/// <summary>
/// Defense of an upgraded armor piece (Doc 23 §9.2): DefensaFinal = floor(DefensaBase × (1 + BonoMejora)),
/// cumulative on the base, never compounded. Integer arithmetic matches backend ItemUpgrades.upgradedDefense.
/// </summary>
public static class ArmorUpgradeDefense
{
    // parity:armor-defense-bonus-percent; index = upgrade level +0..+3.
    private static readonly int[] BonusPercent = { 0, 5, 10, 15 };

    public static int Apply(int baseDefense, int level)
    {
        if (baseDefense < 0) throw new ArgumentOutOfRangeException(nameof(baseDefense));
        if (level < 0 || level > EquipmentInstance.MaxArmorUpgradeLevel) throw new ArgumentOutOfRangeException(nameof(level));
        return (int)((long)baseDefense * (100 + BonusPercent[level]) / 100);
    }
}

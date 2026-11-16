/// <summary>Presentation identity committed by a player weapon attack; default is untagged.</summary>
public readonly struct WeaponAttackTag
{
    public EquipmentSlot SourceSlot { get; }
    public int AttackSequence { get; }
    public int WeaponCatalogIndexPlusOne { get; }
    public AttackType AttackType { get; }
    public bool HasWeaponTag => AttackSequence > 0;

    public WeaponAttackTag(EquipmentSlot sourceSlot, int attackSequence, int weaponCatalogIndexPlusOne, AttackType attackType)
    {
        SourceSlot = sourceSlot;
        AttackSequence = attackSequence;
        WeaponCatalogIndexPlusOne = weaponCatalogIndexPlusOne;
        AttackType = attackType;
    }
}

/// <summary>Read-only view of the item to preview. Previewing never mutates or reserves the instance.</summary>
public readonly struct NpcUpgradeSubject
{
    public LootDefinition Definition { get; }
    public WeaponInstanceModifiers? Weapon { get; }
    public int ArmorLevel { get; }

    public NpcUpgradeSubject(LootDefinition definition, WeaponInstanceModifiers? weapon, int armorLevel)
    {
        Definition = definition;
        Weapon = weapon;
        ArmorLevel = armorLevel;
    }
}

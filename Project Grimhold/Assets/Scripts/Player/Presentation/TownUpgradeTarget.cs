/// <summary>An upgradable equipment instance in the Inventory (loadout slot) or equipped (prepared slot).</summary>
public readonly struct TownUpgradeTarget
{
    public LootId LootId { get; }
    public EquipmentInstance Instance { get; }
    public bool IsEquipped { get; }
    public EquipmentSlot EquipmentSlot { get; }
    public int LoadoutSlot { get; }

    private TownUpgradeTarget(LootId lootId, EquipmentInstance instance, bool isEquipped, EquipmentSlot equipmentSlot, int loadoutSlot)
    {
        LootId = lootId;
        Instance = instance;
        IsEquipped = isEquipped;
        EquipmentSlot = equipmentSlot;
        LoadoutSlot = loadoutSlot;
    }

    public static TownUpgradeTarget Equipped(LootId lootId, EquipmentInstance instance, EquipmentSlot slot) =>
        new(lootId, instance, true, slot, -1);

    public static TownUpgradeTarget InInventory(LootId lootId, EquipmentInstance instance, int slot) =>
        new(lootId, instance, false, default, slot);
}

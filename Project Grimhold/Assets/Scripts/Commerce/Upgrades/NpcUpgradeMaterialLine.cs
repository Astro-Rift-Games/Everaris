/// <summary>Required versus available amount of one material. Only Inventory counts toward the upgrade (Doc 23 §9).</summary>
public readonly struct NpcUpgradeMaterialLine
{
    public string LootId { get; }
    public int Required { get; }
    public int InInventory { get; }
    public int InStash { get; }
    public bool IsSatisfied => InInventory >= Required;

    /// <summary>True when the Inventory is short but the Stash holds enough to cover it; the Stash is never consumed.</summary>
    public bool AvailableInStash => !IsSatisfied && InInventory + InStash >= Required;

    public NpcUpgradeMaterialLine(string lootId, int required, int inInventory, int inStash)
    {
        LootId = lootId;
        Required = required;
        InInventory = inInventory;
        InStash = inStash;
    }
}

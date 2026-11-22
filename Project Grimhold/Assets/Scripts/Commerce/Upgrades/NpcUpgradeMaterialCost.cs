/// <summary>One material requirement of an upgrade step.</summary>
public readonly struct NpcUpgradeMaterialCost
{
    public string LootId { get; }
    public int Amount { get; }

    public NpcUpgradeMaterialCost(string lootId, int amount)
    {
        LootId = lootId;
        Amount = amount;
    }
}

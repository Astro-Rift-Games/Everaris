/// <summary>One equipped armor piece for statistics: its static definition plus the instance upgrade level.</summary>
public readonly struct ArmorPieceStatistics
{
    public ArmorDefinition Definition { get; }
    public int UpgradeLevel { get; }

    public ArmorPieceStatistics(ArmorDefinition definition, int upgradeLevel = 0)
    {
        Definition = definition;
        UpgradeLevel = upgradeLevel;
    }
}

/// <summary>
/// Identifies the authoritative gameplay source that produced extraction progress.
/// </summary>
public enum ExtractionProgressSourceType
{
    None = 0,
    Defeat = 1,
    ContainerFirstOpen = 2
    // 3 was LootFirstAcquisition, retired by GD 06 §6 (loot never generates progress). Do not reuse.
}

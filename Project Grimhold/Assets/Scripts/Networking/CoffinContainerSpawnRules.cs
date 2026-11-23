/// <summary>
/// Chooses whether a standard loot container point shows up as a coffin, without consuming the container's
/// content roll or the locked-chest roll.
/// </summary>
public static class CoffinContainerSpawnRules
{
    private const ulong Domain = 0x434F4646494E5F5FUL;

    public static bool ShouldSpawnCoffin(ulong containerSeed, int chancePercent)
    {
        return SeededChanceRoll.Passes(containerSeed, Domain, chancePercent);
    }
}

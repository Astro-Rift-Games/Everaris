/// <summary>
/// Chooses whether a breakable point shows up as a jar instead of a crate, without consuming the
/// breakable's drop roll.
/// </summary>
public static class JarBreakableSpawnRules
{
    private const ulong Domain = 0x4A41525F42524B5FUL;

    public static bool ShouldSpawnJar(ulong breakableSeed, int chancePercent)
    {
        return SeededChanceRoll.Passes(breakableSeed, Domain, chancePercent);
    }
}

/// <summary>
/// Deterministic percentage roll from a spawn seed. Each caller passes its own domain constant so that rolls
/// made from the same seed stay independent of each other.
/// </summary>
public static class SeededChanceRoll
{
    public static bool Passes(ulong seed, ulong domain, int chancePercent)
    {
        if (chancePercent <= 0)
        {
            return false;
        }

        if (chancePercent >= 100)
        {
            return true;
        }

        // Domain-separated SplitMix64 avalanche, the same mixing LockedChestSpawnRules uses.
        unchecked
        {
            ulong roll = seed ^ domain;
            roll += 0x9E3779B97F4A7C15UL;
            roll = (roll ^ (roll >> 30)) * 0xBF58476D1CE4E5B9UL;
            roll = (roll ^ (roll >> 27)) * 0x94D049BB133111EBUL;
            roll ^= roll >> 31;
            return roll % 100UL < (ulong)chancePercent;
        }
    }
}

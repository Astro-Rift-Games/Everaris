using System;

/// <summary>
/// Deterministic per-room seeds for Encounter composition, independent of global random state and of the
/// loot seed streams (own domain constant, own mixing).
/// </summary>
public static class EncounterSeedRules
{
    // "ENCOUNTR": separates Encounter composition from every loot/breakable stream sharing the session seed.
    private const ulong Domain = 0x454E434F554E5452UL;

    // "REACTIVE": separates the stream of each reactivation cycle from the room's initial one.
    private const ulong CycleDomain = 0x5245414354495645UL;

    /// <summary>Derives the room seed from the host session seed, the scene load generation and the Encounter id.</summary>
    public static ulong Derive(ulong sessionSeed, int sceneLoadGeneration, EncounterId encounterId) =>
        Derive(sessionSeed, sceneLoadGeneration, encounterId, 0);

    /// <summary>
    /// Seed of reactivation <paramref name="cycle"/> of the room (0 = the initial activation, identical to the cycle-less overload).
    /// Every cycle gets its own composition stream, still reproducible from the session seed.
    /// </summary>
    public static ulong Derive(ulong sessionSeed, int sceneLoadGeneration, EncounterId encounterId, int cycle)
    {
        if (!encounterId.IsValid)
        {
            throw new ArgumentException("A valid EncounterId is required to derive a room seed.", nameof(encounterId));
        }

        if (cycle < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cycle), cycle, "The reactivation cycle cannot be negative.");
        }

        ulong identity = ((ulong)(uint)sceneLoadGeneration << 32) | (uint)encounterId.Value;
        ulong seed = Mix(sessionSeed ^ Mix(identity) ^ Mix(Domain));
        return cycle == 0 ? seed : Mix(seed ^ Mix((ulong)cycle ^ CycleDomain));
    }

    public static Random CreateRandom(ulong sessionSeed, int sceneLoadGeneration, EncounterId encounterId) =>
        CreateRandom(sessionSeed, sceneLoadGeneration, encounterId, 0);

    public static Random CreateRandom(ulong sessionSeed, int sceneLoadGeneration, EncounterId encounterId, int cycle)
    {
        ulong seed = Derive(sessionSeed, sceneLoadGeneration, encounterId, cycle);
        return new Random(unchecked((int)(seed ^ (seed >> 32))));
    }

    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}

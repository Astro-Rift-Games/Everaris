using System;

/// <summary>
/// Deterministic per-room seeds for Encounter composition, independent of global random state and of the
/// loot seed streams (own domain constant, own mixing).
/// </summary>
public static class EncounterSeedRules
{
    // "ENCOUNTR": separates Encounter composition from every loot/breakable stream sharing the session seed.
    private const ulong Domain = 0x454E434F554E5452UL;

    /// <summary>Derives the room seed from the host session seed, the scene load generation and the Encounter id.</summary>
    public static ulong Derive(ulong sessionSeed, int sceneLoadGeneration, EncounterId encounterId)
    {
        if (!encounterId.IsValid)
        {
            throw new ArgumentException("A valid EncounterId is required to derive a room seed.", nameof(encounterId));
        }

        ulong identity = ((ulong)(uint)sceneLoadGeneration << 32) | (uint)encounterId.Value;
        return Mix(sessionSeed ^ Mix(identity) ^ Mix(Domain));
    }

    public static Random CreateRandom(ulong sessionSeed, int sceneLoadGeneration, EncounterId encounterId)
    {
        ulong seed = Derive(sessionSeed, sceneLoadGeneration, encounterId);
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

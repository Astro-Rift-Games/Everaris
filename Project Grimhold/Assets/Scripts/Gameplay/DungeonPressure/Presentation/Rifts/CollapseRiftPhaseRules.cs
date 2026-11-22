public static class CollapseRiftPhaseRules
{
    public static bool IsFloorBatchVisible(CollapseRiftBatch batch, DungeonPressurePhase phase)
    {
        return phase switch
        {
            DungeonPressurePhase.Normal => false,
            DungeonPressurePhase.Reinforcements => batch == CollapseRiftBatch.Stage2Initial,
            DungeonPressurePhase.CriticalPressure => true,
            DungeonPressurePhase.Collapse => true,
            _ => false
        };
    }

    public static bool IsFloorBatchActivatable(CollapseRiftBatch batch, DungeonPressurePhase phase)
    {
        return phase switch
        {
            DungeonPressurePhase.Normal => false,
            DungeonPressurePhase.Reinforcements => false,
            DungeonPressurePhase.CriticalPressure => batch == CollapseRiftBatch.Stage2Initial,
            DungeonPressurePhase.Collapse => true,
            _ => false
        };
    }

    public static bool IsWallRiftVisible(DungeonPressurePhase phase)
    {
        return phase != DungeonPressurePhase.Normal;
    }

    public static int ComputeDeterministicSeed(string raidGenerationId, int fallbackSeed = 1337)
    {
        if (string.IsNullOrWhiteSpace(raidGenerationId))
        {
            return fallbackSeed;
        }

        // Stable FNV-1a 32-bit hash so Host and Clients always derive the exact same seed across platforms.
        unchecked
        {
            uint hash = 2166136261u;
            for (int i = 0; i < raidGenerationId.Length; i++)
            {
                hash ^= raidGenerationId[i];
                hash *= 16777619u;
            }
            return (int)hash;
        }
    }
}

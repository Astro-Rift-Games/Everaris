using System;
using System.Collections.Generic;

/// <summary>
/// Pure C# configuration for daily contract rotation size, global UTC reset anchor,
/// rank appearance weights, and anti-repetition history weighting (Doc 12 §12.1, §12.4, §17, §19).
/// </summary>
public sealed class DailyContractRotationConfig
{
    public const int DefaultOfferedContractCount = 6;
    public const int DefaultGlobalResetHourUtc = 0;
    public const int DefaultGlobalResetMinuteUtc = 0;
    public const float DefaultRankWeightValue = 1.0f;
    public const float DefaultRecentAppearanceWeightMultiplier = 0.5f;
    public const int DefaultRecentHistoryRotationCount = 2;

    public static readonly DailyContractRotationConfig Default = new(
        DefaultOfferedContractCount,
        DefaultGlobalResetHourUtc,
        DefaultGlobalResetMinuteUtc,
        DefaultRankWeightValue,
        new Dictionary<MissionRank, float>
        {
            { MissionRank.E, 1.0f },
            { MissionRank.D, 1.0f },
            { MissionRank.C, 1.0f }
        },
        DefaultRecentAppearanceWeightMultiplier,
        DefaultRecentHistoryRotationCount);

    private readonly Dictionary<MissionRank, float> _rankWeights;

    public int OfferedContractCount { get; }
    public int GlobalResetHourUtc { get; }
    public int GlobalResetMinuteUtc { get; }
    public float DefaultRankWeight { get; }
    public float RecentAppearanceWeightMultiplier { get; }
    public int RecentHistoryRotationCount { get; }

    public DailyContractRotationConfig(
        int offeredContractCount,
        int globalResetHourUtc,
        int globalResetMinuteUtc,
        float defaultRankWeight = DefaultRankWeightValue,
        IReadOnlyDictionary<MissionRank, float> rankWeights = null,
        float recentAppearanceWeightMultiplier = DefaultRecentAppearanceWeightMultiplier,
        int recentHistoryRotationCount = DefaultRecentHistoryRotationCount)
    {
        if (offeredContractCount < 1)
            throw new ArgumentOutOfRangeException(nameof(offeredContractCount), "Daily offered contract count must be at least 1.");
        if (globalResetHourUtc < 0 || globalResetHourUtc > 23)
            throw new ArgumentOutOfRangeException(nameof(globalResetHourUtc), "Global reset hour UTC must be between 0 and 23.");
        if (globalResetMinuteUtc < 0 || globalResetMinuteUtc > 59)
            throw new ArgumentOutOfRangeException(nameof(globalResetMinuteUtc), "Global reset minute UTC must be between 0 and 59.");
        if (defaultRankWeight <= 0f || float.IsNaN(defaultRankWeight) || float.IsInfinity(defaultRankWeight))
            throw new ArgumentOutOfRangeException(nameof(defaultRankWeight), "Default rank weight must be a positive finite number.");
        if (recentAppearanceWeightMultiplier <= 0f || recentAppearanceWeightMultiplier > 1f || float.IsNaN(recentAppearanceWeightMultiplier))
            throw new ArgumentOutOfRangeException(nameof(recentAppearanceWeightMultiplier), "Recent appearance weight multiplier must be in (0, 1].");
        if (recentHistoryRotationCount < 0)
            throw new ArgumentOutOfRangeException(nameof(recentHistoryRotationCount), "Recent history rotation count cannot be negative.");

        OfferedContractCount = offeredContractCount;
        GlobalResetHourUtc = globalResetHourUtc;
        GlobalResetMinuteUtc = globalResetMinuteUtc;
        DefaultRankWeight = defaultRankWeight;
        RecentAppearanceWeightMultiplier = recentAppearanceWeightMultiplier;
        RecentHistoryRotationCount = recentHistoryRotationCount;

        _rankWeights = new Dictionary<MissionRank, float>();
        if (rankWeights != null)
        {
            foreach (var kvp in rankWeights)
            {
                if (kvp.Value > 0f && !float.IsNaN(kvp.Value) && !float.IsInfinity(kvp.Value))
                {
                    _rankWeights[kvp.Key] = kvp.Value;
                }
            }
        }
    }

    public float GetRankWeight(MissionRank rank)
    {
        return _rankWeights.TryGetValue(rank, out float weight) ? weight : DefaultRankWeight;
    }
}

/// <summary>
/// Pure C# domain engine responsible for computing global daily rotation windows
/// and deterministically selecting daily contract offers from eligible catalog definitions.
/// </summary>
public static class DailyContractRotationEngine
{
    private const double MinEffectiveWeight = 0.0001d;

    /// <summary>
    /// Computes the active global 24-hour rotation window for <paramref name="utcNow"/>.
    /// Every player observing the same UTC instant shares the exact same cycle start and next reset.
    /// </summary>
    public static DailyRotationWindow ComputeWindow(
        DateTimeOffset utcNow,
        DailyContractRotationConfig config = null)
    {
        var effectiveConfig = config ?? DailyContractRotationConfig.Default;
        DateTime utc = utcNow.UtcDateTime;
        var todayAnchor = new DateTimeOffset(
            utc.Year,
            utc.Month,
            utc.Day,
            effectiveConfig.GlobalResetHourUtc,
            effectiveConfig.GlobalResetMinuteUtc,
            0,
            TimeSpan.Zero);

        DateTimeOffset cycleStartUtc = utcNow.ToUniversalTime() < todayAnchor
            ? todayAnchor.AddDays(-1)
            : todayAnchor;
        DateTimeOffset nextResetUtc = cycleStartUtc.AddDays(1);

        return new DailyRotationWindow(cycleStartUtc, nextResetUtc);
    }

    /// <summary>
    /// Selects up to <see cref="DailyContractRotationConfig.OfferedContractCount"/> daily contracts
    /// (<see cref="MissionType.Normal"/>) eligible for <paramref name="unlockedRank"/>.
    /// When fewer contracts are eligible than the configured offer count, all eligible contracts are returned.
    /// </summary>
    public static List<MissionId> SelectDailyOffers(
        IReadOnlyList<MissionDefinition> catalogDefinitions,
        MissionRank unlockedRank,
        DailyRotationWindow window,
        DailyContractRotationConfig config = null,
        IReadOnlyList<DailyRotationHistoryRecord> recentHistory = null,
        string seedSalt = null)
    {
        var effectiveConfig = config ?? DailyContractRotationConfig.Default;
        var result = new List<MissionId>();
        if (catalogDefinitions == null || catalogDefinitions.Count == 0)
        {
            return result;
        }

        var eligible = new List<MissionDefinition>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < catalogDefinitions.Count; i++)
        {
            var def = catalogDefinitions[i];
            if (def == null || !def.MissionId.IsValid) continue;
            if (def.Type != MissionType.Normal) continue;
            if (def.RankRequired > unlockedRank) continue;
            if (!seenIds.Add(def.Id)) continue;

            eligible.Add(def);
        }

        if (eligible.Count == 0)
        {
            return result;
        }

        eligible.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));

        if (eligible.Count <= effectiveConfig.OfferedContractCount)
        {
            for (int i = 0; i < eligible.Count; i++)
            {
                result.Add(eligible[i].MissionId);
            }
            return result;
        }

        Dictionary<string, int> recentAppearances = CountRecentAppearances(
            recentHistory,
            effectiveConfig.RecentHistoryRotationCount);

        var remaining = new List<MissionDefinition>(eligible);
        var weights = new List<double>(eligible.Count);
        for (int i = 0; i < remaining.Count; i++)
        {
            var def = remaining[i];
            double rankWeight = effectiveConfig.GetRankWeight(def.RankRequired);
            double templateWeight = def.AppearanceWeight;
            double historyMultiplier = 1.0d;
            if (recentAppearances.TryGetValue(def.Id, out int appearances) && appearances > 0)
            {
                historyMultiplier = Math.Pow(effectiveConfig.RecentAppearanceWeightMultiplier, appearances);
            }

            double effectiveWeight = Math.Max(MinEffectiveWeight, rankWeight * templateWeight * historyMultiplier);
            weights.Add(effectiveWeight);
        }

        ulong prngState = HashSeed($"{window.WindowId}|{(int)unlockedRank}|{seedSalt ?? string.Empty}");
        int targetCount = effectiveConfig.OfferedContractCount;

        for (int pick = 0; pick < targetCount && remaining.Count > 0; pick++)
        {
            double totalWeight = 0d;
            for (int i = 0; i < weights.Count; i++)
            {
                totalWeight += weights[i];
            }

            double roll = NextDouble(ref prngState) * totalWeight;
            double cumulative = 0d;
            int selectedIndex = remaining.Count - 1;
            for (int i = 0; i < weights.Count; i++)
            {
                cumulative += weights[i];
                if (roll < cumulative)
                {
                    selectedIndex = i;
                    break;
                }
            }

            result.Add(remaining[selectedIndex].MissionId);
            remaining.RemoveAt(selectedIndex);
            weights.RemoveAt(selectedIndex);
        }

        return result;
    }

    /// <summary>
    /// Advances <paramref name="rotationState"/> if its recorded global window differs from
    /// <paramref name="currentWindow"/> (or if no offer has been generated yet).
    /// Returns true when the state was mutated.
    /// </summary>
    public static bool TryAdvanceRotation(
        DailyMissionRotationState rotationState,
        DailyRotationWindow currentWindow,
        IReadOnlyList<MissionDefinition> catalogDefinitions,
        MissionRank unlockedRank,
        DailyContractRotationConfig config = null,
        string seedSalt = null)
    {
        if (rotationState == null) throw new ArgumentNullException(nameof(rotationState));

        var effectiveConfig = config ?? DailyContractRotationConfig.Default;
        bool sameWindow = string.Equals(rotationState.RotationWindowId, currentWindow.WindowId, StringComparison.Ordinal);
        if (sameWindow && rotationState.OfferedMissionIds.Count > 0)
        {
            return false;
        }

        if (!sameWindow && rotationState.HasActiveRotation && rotationState.OfferedMissionIds.Count > 0 && effectiveConfig.RecentHistoryRotationCount > 0)
        {
            rotationState.RecentRotations.Add(
                new DailyRotationHistoryRecord(rotationState.RotationWindowId, rotationState.OfferedMissionIds));

            while (rotationState.RecentRotations.Count > effectiveConfig.RecentHistoryRotationCount)
            {
                rotationState.RecentRotations.RemoveAt(0);
            }
        }
        else if (effectiveConfig.RecentHistoryRotationCount == 0)
        {
            rotationState.RecentRotations.Clear();
        }

        List<MissionId> newOffers = SelectDailyOffers(
            catalogDefinitions,
            unlockedRank,
            currentWindow,
            effectiveConfig,
            rotationState.RecentRotations,
            seedSalt);

        rotationState.RotationWindowId = currentWindow.WindowId;
        rotationState.OfferedMissionIds.Clear();
        rotationState.OfferedMissionIds.AddRange(newOffers);
        return true;
    }

    private static Dictionary<string, int> CountRecentAppearances(
        IReadOnlyList<DailyRotationHistoryRecord> recentHistory,
        int maxRotationsToInspect)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (recentHistory == null || recentHistory.Count == 0 || maxRotationsToInspect <= 0)
        {
            return counts;
        }

        int startIndex = Math.Max(0, recentHistory.Count - maxRotationsToInspect);
        for (int i = startIndex; i < recentHistory.Count; i++)
        {
            var record = recentHistory[i];
            if (record?.OfferedMissionIds == null) continue;

            for (int m = 0; m < record.OfferedMissionIds.Count; m++)
            {
                MissionId id = record.OfferedMissionIds[m];
                if (!id.IsValid) continue;

                counts.TryGetValue(id.Value, out int current);
                counts[id.Value] = current + 1;
            }
        }

        return counts;
    }

    private static ulong HashSeed(string seed)
    {
        // FNV-1a 64-bit hash
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offsetBasis;
        for (int i = 0; i < seed.Length; i++)
        {
            hash ^= seed[i];
            hash *= prime;
        }
        return hash == 0UL ? offsetBasis : hash;
    }

    private static double NextDouble(ref ulong state)
    {
        // SplitMix64 step producing uniform [0, 1)
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0d / (1UL << 53));
    }
}

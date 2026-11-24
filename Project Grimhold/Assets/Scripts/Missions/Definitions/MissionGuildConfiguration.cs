using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Inspector-configurable ScriptableObject defining Mission Guild parameters:
/// daily contract rotation size, global UTC reset time, rank reputation thresholds,
/// and rank/anti-repetition appearance weights (Doc 12 §11, §12, §17, §19).
/// </summary>
[CreateAssetMenu(fileName = "MissionGuildConfiguration", menuName = "Grimhold/Missions/Mission Guild Configuration")]
public sealed class MissionGuildConfiguration : ScriptableObject
{
    [Serializable]
    public struct GuildRankThresholdSetting
    {
        [Tooltip("Guild rank unlocked at this reputation threshold.")]
        public MissionRank Rank;

        [Tooltip("Minimum cumulative Guild Reputation required to reach this rank.")]
        [Min(0)]
        public int RequiredReputation;
    }

    [Serializable]
    public struct MissionRankWeightSetting
    {
        [Tooltip("Contract rank whose relative selection weight is configured.")]
        public MissionRank Rank;

        [Tooltip("Relative appearance weight in the daily rotation (Doc 12 §12.1, §17).")]
        [Min(0.01f)]
        public float Weight;
    }

    [Header("Daily Contract Rotation (Doc 12 §12.1, §12.4, §19)")]
    [Tooltip("Number of daily repeatable contracts offered per global rotation (default 6).")]
    [SerializeField, Min(1)]
    private int _dailyOfferedContracts = DailyContractRotationConfig.DefaultOfferedContractCount;

    [Tooltip("Global daily reset hour in UTC (0-23). Shared by all players regardless of login time.")]
    [SerializeField, Range(0, 23)]
    private int _globalResetHourUtc = DailyContractRotationConfig.DefaultGlobalResetHourUtc;

    [Tooltip("Global daily reset minute in UTC (0-59).")]
    [SerializeField, Range(0, 59)]
    private int _globalResetMinuteUtc = DailyContractRotationConfig.DefaultGlobalResetMinuteUtc;

    [Header("Guild Rank Reputation Thresholds (Doc 12 §11.1, §11.2, §19)")]
    [Tooltip("Ordered Guild Reputation thresholds. Must include Rank E at 0 RP and strictly increasing values for higher ranks (e.g., D=100, C=300).")]
    [SerializeField]
    private List<GuildRankThresholdSetting> _rankThresholds = new()
    {
        new GuildRankThresholdSetting { Rank = MissionRank.E, RequiredReputation = GuildRankProgressionConfig.DefaultRankEReputation },
        new GuildRankThresholdSetting { Rank = MissionRank.D, RequiredReputation = GuildRankProgressionConfig.DefaultRankDReputation },
        new GuildRankThresholdSetting { Rank = MissionRank.C, RequiredReputation = GuildRankProgressionConfig.DefaultRankCReputation }
    };

    [Header("Appearance Weights & Anti-Repetition (Doc 12 §12.1, §17)")]
    [Tooltip("Relative appearance weights per contract rank during daily offer generation.")]
    [SerializeField]
    private List<MissionRankWeightSetting> _rankWeights = new()
    {
        new MissionRankWeightSetting { Rank = MissionRank.E, Weight = 1.0f },
        new MissionRankWeightSetting { Rank = MissionRank.D, Weight = 1.0f },
        new MissionRankWeightSetting { Rank = MissionRank.C, Weight = 1.0f }
    };

    [Tooltip("Fallback appearance weight for any rank not explicitly listed in Rank Weights.")]
    [SerializeField, Min(0.01f)]
    private float _defaultRankWeight = DailyContractRotationConfig.DefaultRankWeightValue;

    [Tooltip("Weight multiplier applied per recent rotation appearance to reduce consecutive duplicates without prohibiting reappearance (Doc 12 §17).")]
    [SerializeField, Range(0.01f, 1.0f)]
    private float _recentAppearanceWeightMultiplier = DailyContractRotationConfig.DefaultRecentAppearanceWeightMultiplier;

    [Tooltip("Number of previous daily rotations tracked to reduce consecutive repetitions.")]
    [SerializeField, Range(0, 14)]
    private int _recentHistoryRotationCount = DailyContractRotationConfig.DefaultRecentHistoryRotationCount;

    public int DailyOfferedContracts => _dailyOfferedContracts;
    public int GlobalResetHourUtc => _globalResetHourUtc;
    public int GlobalResetMinuteUtc => _globalResetMinuteUtc;
    public float DefaultRankWeight => _defaultRankWeight;
    public float RecentAppearanceWeightMultiplier => _recentAppearanceWeightMultiplier;
    public int RecentHistoryRotationCount => _recentHistoryRotationCount;
    public IReadOnlyList<GuildRankThresholdSetting> RankThresholds => _rankThresholds;
    public IReadOnlyList<MissionRankWeightSetting> RankWeights => _rankWeights;

    /// <summary>
    /// Builds the current pure-domain rank progression config directly from Inspector values.
    /// Falls back to <see cref="GuildRankProgressionConfig.Default"/> if misconfigured.
    /// </summary>
    public GuildRankProgressionConfig BuildRankProgressionConfig()
    {
        if (TryBuildRankProgressionConfig(out var config, out string error))
        {
            return config;
        }

        Debug.LogWarning($"[{nameof(MissionGuildConfiguration)}] Invalid rank thresholds ({error}); falling back to default rank thresholds.", this);
        return GuildRankProgressionConfig.Default;
    }

    /// <summary>
    /// Builds the current pure-domain daily rotation config directly from Inspector values.
    /// </summary>
    public DailyContractRotationConfig BuildDailyRotationConfig()
    {
        int offeredCount = Mathf.Max(1, _dailyOfferedContracts);
        int resetHour = Mathf.Clamp(_globalResetHourUtc, 0, 23);
        int resetMinute = Mathf.Clamp(_globalResetMinuteUtc, 0, 59);
        float fallbackWeight = _defaultRankWeight > 0f ? _defaultRankWeight : DailyContractRotationConfig.DefaultRankWeightValue;
        float recentMultiplier = Mathf.Clamp(_recentAppearanceWeightMultiplier, 0.01f, 1.0f);
        int recentHistoryCount = Mathf.Max(0, _recentHistoryRotationCount);

        var weights = new Dictionary<MissionRank, float>();
        if (_rankWeights != null)
        {
            for (int i = 0; i < _rankWeights.Count; i++)
            {
                var entry = _rankWeights[i];
                if (entry.Weight > 0f && !float.IsNaN(entry.Weight) && !float.IsInfinity(entry.Weight))
                {
                    weights[entry.Rank] = entry.Weight;
                }
            }
        }

        return new DailyContractRotationConfig(
            offeredCount,
            resetHour,
            resetMinute,
            fallbackWeight,
            weights,
            recentMultiplier,
            recentHistoryCount);
    }

    public bool TryValidate(out string error)
    {
        if (_dailyOfferedContracts < 1)
        {
            error = "Daily offered contracts must be at least 1.";
            return false;
        }

        if (_globalResetHourUtc < 0 || _globalResetHourUtc > 23)
        {
            error = "Global reset hour UTC must be between 0 and 23.";
            return false;
        }

        if (_globalResetMinuteUtc < 0 || _globalResetMinuteUtc > 59)
        {
            error = "Global reset minute UTC must be between 0 and 59.";
            return false;
        }

        if (_defaultRankWeight <= 0f || float.IsNaN(_defaultRankWeight) || float.IsInfinity(_defaultRankWeight))
        {
            error = "Default rank weight must be positive.";
            return false;
        }

        if (_recentAppearanceWeightMultiplier <= 0f || _recentAppearanceWeightMultiplier > 1f || float.IsNaN(_recentAppearanceWeightMultiplier))
        {
            error = "Recent appearance weight multiplier must be within (0, 1].";
            return false;
        }

        if (_recentHistoryRotationCount < 0)
        {
            error = "Recent history rotation count cannot be negative.";
            return false;
        }

        if (!TryBuildRankProgressionConfig(out _, out error))
        {
            return false;
        }

        if (_rankWeights != null)
        {
            var seenWeightRanks = new HashSet<MissionRank>();
            for (int i = 0; i < _rankWeights.Count; i++)
            {
                var entry = _rankWeights[i];
                if (entry.Weight <= 0f || float.IsNaN(entry.Weight) || float.IsInfinity(entry.Weight))
                {
                    error = $"Rank weight for '{entry.Rank}' must be positive.";
                    return false;
                }

                if (!seenWeightRanks.Add(entry.Rank))
                {
                    error = $"Duplicate rank weight configured for '{entry.Rank}'.";
                    return false;
                }
            }
        }

        error = null;
        return true;
    }

    private bool TryBuildRankProgressionConfig(out GuildRankProgressionConfig config, out string error)
    {
        if (_rankThresholds == null || _rankThresholds.Count == 0)
        {
            config = null;
            error = "Rank thresholds list is empty.";
            return false;
        }

        var converted = new List<GuildRankThreshold>(_rankThresholds.Count);
        for (int i = 0; i < _rankThresholds.Count; i++)
        {
            converted.Add(new GuildRankThreshold(_rankThresholds[i].Rank, _rankThresholds[i].RequiredReputation));
        }

        return GuildRankProgressionConfig.TryCreate(converted, out config, out error);
    }

    private void OnValidate()
    {
        if (!TryValidate(out string error))
        {
            Debug.LogError($"[{nameof(MissionGuildConfiguration)}] Invalid configuration: {error}", this);
        }
    }
}

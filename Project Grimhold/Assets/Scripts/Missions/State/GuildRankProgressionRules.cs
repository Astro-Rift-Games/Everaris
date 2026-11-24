using System;
using System.Collections.Generic;

/// <summary>
/// Immutable reputation threshold pairing a <see cref="MissionRank"/> with the minimum
/// Guild Reputation required to unlock it.
/// </summary>
public readonly struct GuildRankThreshold : IEquatable<GuildRankThreshold>
{
    public MissionRank Rank { get; }
    public int RequiredReputation { get; }

    public GuildRankThreshold(MissionRank rank, int requiredReputation)
    {
        Rank = rank;
        RequiredReputation = requiredReputation;
    }

    public bool Equals(GuildRankThreshold other)
    {
        return Rank == other.Rank && RequiredReputation == other.RequiredReputation;
    }

    public override bool Equals(object obj) => obj is GuildRankThreshold other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((int)Rank, RequiredReputation);
}

/// <summary>
/// Resolved Guild rank and remaining reputation required to unlock the next configured rank.
/// </summary>
public readonly struct GuildRankProgress
{
    public MissionRank CurrentRank { get; }
    public bool HasNextRank { get; }
    public MissionRank NextRank { get; }
    public int RemainingReputation { get; }
    public int NextRankThreshold { get; }

    public GuildRankProgress(
        MissionRank currentRank,
        bool hasNextRank,
        MissionRank nextRank,
        int remainingReputation,
        int nextRankThreshold)
    {
        CurrentRank = currentRank;
        HasNextRank = hasNextRank;
        NextRank = nextRank;
        RemainingReputation = remainingReputation;
        NextRankThreshold = nextRankThreshold;
    }
}

/// <summary>
/// Pure C# configuration describing ordered reputation thresholds for Guild ranks.
/// </summary>
public sealed class GuildRankProgressionConfig
{
    public const int DefaultRankEReputation = 0;
    public const int DefaultRankDReputation = 100;
    public const int DefaultRankCReputation = 300;

    public static readonly GuildRankProgressionConfig Default = new(
        new[]
        {
            new GuildRankThreshold(MissionRank.E, DefaultRankEReputation),
            new GuildRankThreshold(MissionRank.D, DefaultRankDReputation),
            new GuildRankThreshold(MissionRank.C, DefaultRankCReputation)
        });

    private readonly GuildRankThreshold[] _orderedThresholds;

    public IReadOnlyList<GuildRankThreshold> Thresholds => _orderedThresholds;

    private GuildRankProgressionConfig(GuildRankThreshold[] orderedThresholds)
    {
        _orderedThresholds = orderedThresholds;
    }

    public static bool TryCreate(
        IEnumerable<GuildRankThreshold> thresholds,
        out GuildRankProgressionConfig config,
        out string error)
    {
        config = null;
        if (thresholds == null)
        {
            error = "Guild rank thresholds cannot be null.";
            return false;
        }

        var list = new List<GuildRankThreshold>(thresholds);
        if (list.Count == 0)
        {
            error = "At least one guild rank threshold must be configured.";
            return false;
        }

        var seenRanks = new HashSet<MissionRank>();
        for (int i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            if (entry.RequiredReputation < 0)
            {
                error = $"Guild rank '{entry.Rank}' cannot have a negative reputation threshold ({entry.RequiredReputation}).";
                return false;
            }

            if (!seenRanks.Add(entry.Rank))
            {
                error = $"Duplicate guild rank threshold configured for rank '{entry.Rank}'.";
                return false;
            }
        }

        list.Sort((left, right) => left.Rank.CompareTo(right.Rank));

        if (list[0].Rank != MissionRank.E || list[0].RequiredReputation != 0)
        {
            error = "Guild rank progression must start at Rank E with 0 required reputation.";
            return false;
        }

        for (int i = 1; i < list.Count; i++)
        {
            if (list[i].RequiredReputation <= list[i - 1].RequiredReputation)
            {
                error = $"Guild rank '{list[i].Rank}' threshold ({list[i].RequiredReputation}) must be strictly greater than '{list[i - 1].Rank}' ({list[i - 1].RequiredReputation}).";
                return false;
            }
        }

        config = new GuildRankProgressionConfig(list.ToArray());
        error = null;
        return true;
    }
}

/// <summary>
/// Pure domain rules that resolve Guild rank and next-rank progress from reputation and configuration.
/// </summary>
public static class GuildRankProgressionRules
{
    public static MissionRank ResolveRank(int reputation, GuildRankProgressionConfig config = null)
    {
        return GetProgress(reputation, config).CurrentRank;
    }

    public static GuildRankProgress GetProgress(int reputation, GuildRankProgressionConfig config = null)
    {
        var effectiveConfig = config ?? GuildRankProgressionConfig.Default;
        int sanitizedReputation = Math.Max(0, reputation);
        var thresholds = effectiveConfig.Thresholds;

        int currentIndex = 0;
        for (int i = 1; i < thresholds.Count; i++)
        {
            if (sanitizedReputation >= thresholds[i].RequiredReputation)
            {
                currentIndex = i;
            }
            else
            {
                break;
            }
        }

        MissionRank currentRank = thresholds[currentIndex].Rank;
        bool hasNext = currentIndex + 1 < thresholds.Count;
        if (!hasNext)
        {
            return new GuildRankProgress(currentRank, false, currentRank, 0, thresholds[currentIndex].RequiredReputation);
        }

        var next = thresholds[currentIndex + 1];
        int remaining = Math.Max(0, next.RequiredReputation - sanitizedReputation);
        return new GuildRankProgress(currentRank, true, next.Rank, remaining, next.RequiredReputation);
    }
}

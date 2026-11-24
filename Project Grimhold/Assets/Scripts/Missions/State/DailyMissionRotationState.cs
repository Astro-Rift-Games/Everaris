using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Represents one global daily contract rotation window anchored to a fixed UTC reset time.
/// </summary>
public readonly struct DailyRotationWindow : IEquatable<DailyRotationWindow>
{
    public string WindowId { get; }
    public DateTimeOffset CycleStartUtc { get; }
    public DateTimeOffset NextResetUtc { get; }

    public DailyRotationWindow(DateTimeOffset cycleStartUtc, DateTimeOffset nextResetUtc)
    {
        CycleStartUtc = cycleStartUtc.ToUniversalTime();
        NextResetUtc = nextResetUtc.ToUniversalTime();
        WindowId = FormatWindowId(CycleStartUtc);
    }

    public static string FormatWindowId(DateTimeOffset cycleStartUtc)
    {
        return cycleStartUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mmZ", CultureInfo.InvariantCulture);
    }

    public bool Equals(DailyRotationWindow other)
    {
        return string.Equals(WindowId, other.WindowId, StringComparison.Ordinal) &&
               CycleStartUtc.Equals(other.CycleStartUtc) &&
               NextResetUtc.Equals(other.NextResetUtc);
    }

    public override bool Equals(object obj) => obj is DailyRotationWindow other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(WindowId, CycleStartUtc, NextResetUtc);
}

/// <summary>
/// Historical record of contracts offered in a previous daily rotation, used to reduce
/// consecutive repetition weights without permanently excluding any mission (Doc 12 §17).
/// </summary>
public sealed class DailyRotationHistoryRecord
{
    public string WindowId { get; set; }
    public List<MissionId> OfferedMissionIds { get; } = new();

    public DailyRotationHistoryRecord()
    {
    }

    public DailyRotationHistoryRecord(string windowId, IEnumerable<MissionId> offeredMissionIds)
    {
        WindowId = windowId ?? string.Empty;
        if (offeredMissionIds != null)
        {
            OfferedMissionIds.AddRange(offeredMissionIds);
        }
    }

    public DailyRotationHistoryRecord Clone()
    {
        return new DailyRotationHistoryRecord(WindowId, OfferedMissionIds);
    }
}

/// <summary>
/// Mutable local-profile state for the player's current global daily contract offer and recent rotation history.
/// </summary>
public sealed class DailyMissionRotationState
{
    public string RotationWindowId { get; set; } = string.Empty;
    public List<MissionId> OfferedMissionIds { get; } = new();
    public List<DailyRotationHistoryRecord> RecentRotations { get; } = new();

    public bool HasActiveRotation => !string.IsNullOrWhiteSpace(RotationWindowId);

    public bool IsOffered(MissionId missionId)
    {
        if (!missionId.IsValid)
        {
            return false;
        }

        for (int i = 0; i < OfferedMissionIds.Count; i++)
        {
            if (OfferedMissionIds[i] == missionId)
            {
                return true;
            }
        }

        return false;
    }

    public DailyMissionRotationState Clone()
    {
        var clone = new DailyMissionRotationState
        {
            RotationWindowId = RotationWindowId ?? string.Empty
        };
        clone.OfferedMissionIds.AddRange(OfferedMissionIds);
        for (int i = 0; i < RecentRotations.Count; i++)
        {
            if (RecentRotations[i] != null)
            {
                clone.RecentRotations.Add(RecentRotations[i].Clone());
            }
        }
        return clone;
    }
}

using System;

/// <summary>
/// Clock abstraction for mission rotation scheduling so domain logic and tests
/// never depend directly on system wall-clock calls.
/// </summary>
public interface IMissionClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Production UTC clock backed by <see cref="DateTimeOffset.UtcNow"/>.
/// </summary>
public sealed class SystemUtcMissionClock : IMissionClock
{
    public static readonly SystemUtcMissionClock Instance = new();

    private SystemUtcMissionClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Adjustable UTC clock wrapper used for development/PlayMode reset simulation
/// while delegating base time to an inner <see cref="IMissionClock"/>.
/// </summary>
public sealed class AdjustableMissionClock : IMissionClock
{
    private readonly IMissionClock _baseClock;
    private TimeSpan _offset;

    public AdjustableMissionClock(IMissionClock baseClock = null)
    {
        _baseClock = baseClock ?? SystemUtcMissionClock.Instance;
        _offset = TimeSpan.Zero;
    }

    public TimeSpan Offset => _offset;
    public DateTimeOffset UtcNow => _baseClock.UtcNow + _offset;

    public void AdvanceBy(TimeSpan delta)
    {
        _offset += delta;
    }

    public void AdvanceTo(DateTimeOffset targetUtc)
    {
        DateTimeOffset now = UtcNow;
        if (targetUtc > now)
        {
            _offset += targetUtc - now;
        }
    }

    public void ResetOffset()
    {
        _offset = TimeSpan.Zero;
    }
}

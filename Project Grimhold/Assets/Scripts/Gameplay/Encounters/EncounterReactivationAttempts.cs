using System;

/// <summary>
/// Authority-local memory of reactivation attempts, so a room is neither retried nor logged on every cadence pass when an attempt
/// cannot succeed. Structural outcomes (the same seed would decide the same again, or the registry refused) park the room.
/// A transient spawn failure (seed or Luck unavailable, spawns failing) never parks: the room waits a bounded, exponentially
/// growing number of passes before the next attempt. Not replicated: a new Host starts fresh and tries once more.
/// </summary>
public sealed class EncounterReactivationAttempts
{
    /// <summary>Longest wait, in reactivation passes, after repeated transient spawn failures.</summary>
    public const int MaxBackoffPasses = 32;

    private readonly bool[] _parked;
    private readonly int[] _spawnFailures;
    private readonly int[] _passesToSkip;

    public EncounterReactivationAttempts(int roomCapacity)
    {
        if (roomCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(roomCapacity), roomCapacity, "The room capacity cannot be negative.");
        }

        _parked = new bool[roomCapacity];
        _spawnFailures = new int[roomCapacity];
        _passesToSkip = new int[roomCapacity];
    }

    public bool IsParked(int roomIndex) => IsRoom(roomIndex) && _parked[roomIndex];

    /// <summary>
    /// Call once per reactivation pass for a room that could be attempted. False for a parked room and while a spawn-failure backoff
    /// is still running (each false answer consumes one pass of the wait).
    /// </summary>
    public bool ShouldAttempt(int roomIndex)
    {
        if (!IsRoom(roomIndex) || _parked[roomIndex])
        {
            return false;
        }

        if (_passesToSkip[roomIndex] > 0)
        {
            _passesToSkip[roomIndex]--;
            return false;
        }

        return true;
    }

    /// <summary>Records an outcome. Returns true when it must be logged: a success, or the first time a problem of a streak is seen.</summary>
    public bool Record(int roomIndex, EncounterReactivationOutcome outcome)
    {
        if (!IsRoom(roomIndex))
        {
            return false;
        }

        switch (outcome)
        {
            case EncounterReactivationOutcome.Reactivated:
                _spawnFailures[roomIndex] = 0;
                _passesToSkip[roomIndex] = 0;
                return true;
            case EncounterReactivationOutcome.NoEncounter:
            case EncounterReactivationOutcome.InvalidComposition:
            case EncounterReactivationOutcome.ActivationRejected:
                bool firstTime = !_parked[roomIndex];
                _parked[roomIndex] = true;
                return firstTime;
            case EncounterReactivationOutcome.SpawnFailed:
                int failures = _spawnFailures[roomIndex] < 31 ? ++_spawnFailures[roomIndex] : _spawnFailures[roomIndex];
                _passesToSkip[roomIndex] = Math.Min(MaxBackoffPasses, 1 << (failures - 1));
                return failures == 1;
            default:
                return false;
        }
    }

    public void Reset()
    {
        Array.Clear(_parked, 0, _parked.Length);
        Array.Clear(_spawnFailures, 0, _spawnFailures.Length);
        Array.Clear(_passesToSkip, 0, _passesToSkip.Length);
    }

    private bool IsRoom(int roomIndex) => roomIndex >= 0 && roomIndex < _parked.Length;
}

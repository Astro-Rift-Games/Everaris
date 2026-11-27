using System;
using System.Collections.Generic;

/// <summary>
/// Value copy of the state of one <see cref="EncounterLifecycle"/>, used to replicate it and to restore it after a Host
/// Migration without duplicating the lifecycle rules. Member key lists are ascending.
/// </summary>
public readonly struct EncounterLifecycleSnapshot
{
    private readonly uint[] _memberKeys;
    private readonly uint[] _aliveMemberKeys;

    public EncounterLifecycleSnapshot(
        EncounterState state,
        int reactivationCount,
        double cooldownStartTime,
        uint[] memberKeys,
        uint[] aliveMemberKeys)
    {
        State = state;
        ReactivationCount = reactivationCount;
        CooldownStartTime = cooldownStartTime;
        _memberKeys = memberKeys;
        _aliveMemberKeys = aliveMemberKeys;
    }

    public EncounterState State { get; }
    public int ReactivationCount { get; }
    public double CooldownStartTime { get; }

    /// <summary>Every key that belongs to the current cycle, alive or already eliminated.</summary>
    public IReadOnlyList<uint> MemberKeys => _memberKeys ?? Array.Empty<uint>();

    /// <summary>Members of the current cycle that were not eliminated yet.</summary>
    public IReadOnlyList<uint> AliveMemberKeys => _aliveMemberKeys ?? Array.Empty<uint>();
}

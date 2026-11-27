using System;
using System.Collections.Generic;

/// <summary>
/// Authoritative lifecycle of one room Encounter (Doc 10 §9.2). Members are identified by stable keys and keep
/// belonging to the encounter regardless of where they are; the encounter resolves when every member was validly
/// eliminated, and the cooldown starts at that moment.
/// </summary>
public sealed class EncounterLifecycle
{
    private readonly EncounterLifecycleSettings _settings;
    private readonly HashSet<uint> _members = new HashSet<uint>();
    private readonly HashSet<uint> _aliveMembers = new HashSet<uint>();

    public EncounterLifecycle(EncounterId id, EncounterLifecycleSettings settings)
    {
        if (!id.IsValid)
        {
            throw new ArgumentException("An encounter requires a valid EncounterId.", nameof(id));
        }

        Id = id;
        _settings = settings;
        State = EncounterState.NotActivated;
    }

    public EncounterId Id { get; }
    public EncounterState State { get; private set; }
    public int ReactivationCount { get; private set; }
    public int AliveMemberCount => _aliveMembers.Count;
    public double CooldownStartTime { get; private set; }

    public bool IsMember(uint memberKey) => _members.Contains(memberKey);

    public bool IsAliveMember(uint memberKey) => _aliveMembers.Contains(memberKey);

    public bool TryActivate(IReadOnlyList<uint> memberKeys)
    {
        if (State != EncounterState.NotActivated || !AreValidMembers(memberKeys))
        {
            return false;
        }

        BeginCycle(memberKeys);
        return true;
    }

    public bool RegisterValidElimination(uint memberKey, double now)
    {
        if (State != EncounterState.Active || !_aliveMembers.Remove(memberKey))
        {
            return false;
        }

        if (_aliveMembers.Count == 0)
        {
            State = EncounterState.Resolved;
            CooldownStartTime = now;
        }

        return true;
    }

    public void Advance(double now)
    {
        if (State == EncounterState.Resolved)
        {
            State = EncounterState.Cooldown;
        }

        if (State == EncounterState.Cooldown && now - CooldownStartTime >= _settings.CooldownSeconds)
        {
            State = EncounterState.Reactivable;
        }
    }

    public EncounterReactivationBlock EvaluateReactivation(in EncounterReactivationContext context)
    {
        switch (State)
        {
            case EncounterState.NotActivated:
            case EncounterState.Active:
                return EncounterReactivationBlock.NotResolved;
            case EncounterState.Resolved:
            case EncounterState.Cooldown:
                return EncounterReactivationBlock.CooldownPending;
        }

        if (ReactivationCount >= _settings.MaxReactivations)
        {
            return EncounterReactivationBlock.MaxReactivationsReached;
        }

        if (context.PlayersInGenerationZone)
        {
            return EncounterReactivationBlock.PlayersInGenerationZone;
        }

        if (!context.GlobalCapacityAvailable)
        {
            return EncounterReactivationBlock.NoGlobalCapacity;
        }

        return context.DungeonPhaseAllowsGeneration
            ? EncounterReactivationBlock.None
            : EncounterReactivationBlock.DungeonPhaseForbidsGeneration;
    }

    public bool TryReactivate(in EncounterReactivationContext context, IReadOnlyList<uint> memberKeys)
    {
        if (EvaluateReactivation(context) != EncounterReactivationBlock.None || !AreValidMembers(memberKeys))
        {
            return false;
        }

        ReactivationCount++;
        BeginCycle(memberKeys);
        return true;
    }

    private void BeginCycle(IReadOnlyList<uint> memberKeys)
    {
        _members.Clear();
        _aliveMembers.Clear();
        for (int i = 0; i < memberKeys.Count; i++)
        {
            _members.Add(memberKeys[i]);
            _aliveMembers.Add(memberKeys[i]);
        }

        State = EncounterState.Active;
    }

    private static bool AreValidMembers(IReadOnlyList<uint> memberKeys)
    {
        if (memberKeys == null || memberKeys.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < memberKeys.Count; i++)
        {
            for (int j = i + 1; j < memberKeys.Count; j++)
            {
                if (memberKeys[i] == memberKeys[j])
                {
                    return false;
                }
            }
        }

        return true;
    }
}

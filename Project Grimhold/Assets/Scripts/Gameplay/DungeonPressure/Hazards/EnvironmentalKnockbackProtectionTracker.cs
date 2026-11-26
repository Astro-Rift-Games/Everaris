using System.Collections.Generic;

/// <summary>
/// Authoritative per-target protection window against consecutive environmental knockbacks (Doc 20 §5).
/// Damage still accumulates normally while environmental knockback is suppressed during the window.
/// </summary>
public sealed class EnvironmentalKnockbackProtectionTracker
{
    private readonly Dictionary<EntityId, int> _lastKnockbackTickByTarget = new();

    public bool CanApplyEnvironmentalKnockback(EntityId targetId, int currentTick, int protectionWindowTicks)
    {
        if (protectionWindowTicks <= 0 || targetId.Value == 0)
        {
            return true;
        }

        if (!_lastKnockbackTickByTarget.TryGetValue(targetId, out int lastTick))
        {
            return true;
        }

        // Guard against tick rollback or reset: only block while strictly inside [lastTick, lastTick + window).
        int elapsed = currentTick - lastTick;
        return elapsed < 0 || elapsed >= protectionWindowTicks;
    }

    public void RecordEnvironmentalKnockbackApplied(EntityId targetId, int currentTick)
    {
        if (targetId.Value == 0)
        {
            return;
        }

        _lastKnockbackTickByTarget[targetId] = currentTick;
    }

    public void Clear()
    {
        _lastKnockbackTickByTarget.Clear();
    }
}

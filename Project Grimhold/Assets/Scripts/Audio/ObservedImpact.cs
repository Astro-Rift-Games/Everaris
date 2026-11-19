using Fusion;
using UnityEngine;

/// <summary>
/// Minimal replicated snapshot of the latest confirmed weapon impact of one avatar, written only by its State Authority.
/// It carries exactly what weapon impact audio consumes (cue selection and position) so peers without Input Authority,
/// which never receive the feedback RPC, can present the impact. One snapshot per avatar: every impact recorded between
/// two snapshots an observer receives (same tick, packet loss, send interval or a frame rate below the tick rate)
/// coalesces into the newest one (accepted limitation, observers hear a single cue; the attacker still hears each).
/// </summary>
public struct ObservedImpactSnapshot : INetworkStruct
{
    /// <summary>Feedback sequence of the impact (the avatar's <c>CombatFeedbackSequence</c> when it was recorded); 0 = none.</summary>
    public int Sequence;
    /// <summary>Victim entity (character victims play Attack/Hit, anything else Block).</summary>
    public int TargetIdValue;
    /// <summary>World position where the impact was confirmed.</summary>
    public Vector2 HitPoint;
    /// <summary>Confirmed weapon identity used to resolve the weapon audio config; 0 = untagged (generic fallback).</summary>
    public int WeaponCatalogIndexPlusOne;

    public EntityId TargetId => new(TargetIdValue);
}

/// <summary>
/// Decides when a peer presents a replicated impact snapshot: only peers WITHOUT Input Authority (the attacker already
/// hears it through the feedback RPC), once per newer sequence, and never for a snapshot that existed when the avatar
/// spawned or was restored (late join, host migration).
/// </summary>
public sealed class ObservedImpactTracker
{
    private int _lastSequence;

    /// <summary>Marks the snapshot already present at spawn/restore as consumed.</summary>
    public void Seed(int sequence) => _lastSequence = sequence;

    /// <summary>
    /// True when a newer snapshot should be presented on this peer. The tracker always follows the replicated
    /// sequence (also while holding Input Authority, and when a restore lowers it), so an impact consumed or
    /// skipped here is never replayed later; only a strictly newer sequence on a peer without Input Authority plays.
    /// </summary>
    public bool TryAccept(bool hasInputAuthority, int sequence)
    {
        if (sequence == _lastSequence) return false;
        bool newer = sequence > _lastSequence;
        _lastSequence = sequence;
        return newer && !hasInputAuthority;
    }
}

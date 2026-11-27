using System;
using Fusion;

/// <summary>
/// Replicated network struct representing a single team's extraction countdown state.
/// Holds the active zone entity ID, countdown timer, and composition signature.
/// Capacity matches <see cref="RaidSessionRules.MaxTeams"/>.
/// </summary>
[System.Serializable]
public struct TeamExtractionSlot : INetworkStruct, IEquatable<TeamExtractionSlot>
{
    public int TeamIdRaw;
    public int ActiveZoneIdValue;
    public TickTimer CountdownTimer;
    public int ActiveSignatureValue;
    public byte LastCancelReasonRaw;
    public int LastBlockingParticipantIdRaw;

    public bool IsEmpty => TeamIdRaw == 0;
    public bool IsActive
    {
        get
        {
#if UNITY_EDITOR
            return TeamIdRaw != 0 && ActiveZoneIdValue != 0;
#else
            return TeamIdRaw != 0 && ActiveZoneIdValue != 0 && CountdownTimer.IsRunning;
#endif
        }
    }
    public RaidTeamId TeamId => RaidTeamId.TryCreate(TeamIdRaw, out RaidTeamId id) ? id : default;
    public EntityId ActiveZoneId => new EntityId(ActiveZoneIdValue);
    public TeamCompositionSignature ActiveSignature => new TeamCompositionSignature(ActiveSignatureValue);
    public TeamExtractionCancelReason LastCancelReason => (TeamExtractionCancelReason)LastCancelReasonRaw;
    public RaidParticipantId LastBlockingParticipantId =>
        RaidParticipantId.TryCreate(LastBlockingParticipantIdRaw, out RaidParticipantId id) ? id : default;

    public bool Equals(TeamExtractionSlot other)
    {
        return TeamIdRaw == other.TeamIdRaw &&
               ActiveZoneIdValue == other.ActiveZoneIdValue &&
               CountdownTimer.TargetTick == other.CountdownTimer.TargetTick &&
               ActiveSignatureValue == other.ActiveSignatureValue &&
               LastCancelReasonRaw == other.LastCancelReasonRaw &&
               LastBlockingParticipantIdRaw == other.LastBlockingParticipantIdRaw;
    }

    public override bool Equals(object obj) => obj is TeamExtractionSlot other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = TeamIdRaw;
            hash = (hash * 397) ^ ActiveZoneIdValue;
            hash = (hash * 397) ^ (CountdownTimer.TargetTick ?? 0);
            hash = (hash * 397) ^ ActiveSignatureValue;
            hash = (hash * 397) ^ LastCancelReasonRaw;
            hash = (hash * 397) ^ LastBlockingParticipantIdRaw;
            return hash;
        }
    }

    public static bool operator ==(TeamExtractionSlot left, TeamExtractionSlot right) => left.Equals(right);
    public static bool operator !=(TeamExtractionSlot left, TeamExtractionSlot right) => !left.Equals(right);
}

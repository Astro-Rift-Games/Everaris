/// <summary>
/// Immutable authoritative facts about one team member that the Accelerated Resolution rule needs
/// (DownedAndReviveArchitecture section 10). Built by the Host coordinator each tick; holds no state.
/// </summary>
internal readonly struct DownedTeamMemberRouteSnapshot
{
    internal DownedTeamMemberRouteSnapshot(
        bool isRaiding,
        bool hasAvatar,
        bool isDowned,
        bool isAlive,
        bool isConnected,
        bool canBeginSelf,
        RecoveryKind activeSessionKind)
    {
        IsKnown = true;
        IsRaiding = isRaiding;
        HasAvatar = hasAvatar;
        IsDowned = isDowned;
        IsAlive = isAlive;
        IsConnected = isConnected;
        CanBeginSelf = canBeginSelf;
        ActiveSessionKind = activeSessionKind;
    }

    /// <summary>
    /// The member's authoritative state was read this tick. A <c>default</c> snapshot is unknown (for
    /// example a participant that cannot be resolved yet), never "not Raiding": acceleration latches, so
    /// an unknown member must not be read as the absence of a recovery route.
    /// </summary>
    internal bool IsKnown { get; }

    /// <summary>The participant is still <see cref="RaidParticipantState.Raiding"/>.</summary>
    internal bool IsRaiding { get; }

    /// <summary>The participant's current avatar resolves.</summary>
    internal bool HasAvatar { get; }

    /// <summary>The avatar is in the Downed state.</summary>
    internal bool IsDowned { get; }

    /// <summary>The avatar is alive (a Downed avatar counts as alive).</summary>
    internal bool IsAlive { get; }

    /// <summary>The participant is not flagged disconnected; a missing connectivity component means connected.</summary>
    internal bool IsConnected { get; }

    /// <summary>The Downed avatar could start a Self-revive now (usable Self-reviver, no session running).</summary>
    internal bool CanBeginSelf { get; }

    /// <summary>Kind of the valid recovery session running on the Downed avatar, or None.</summary>
    internal RecoveryKind ActiveSessionKind { get; }

    /// <summary>A valid recovery session (assisted, Self or handoff) is running on the Downed avatar.</summary>
    internal bool HasRecoverySession => ActiveSessionKind != RecoveryKind.None;
}

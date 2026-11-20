/// <summary>
/// Pure rules for the FIX-95 raid start marker and the pre-start refund decision. "Effectively started"
/// is the moment the Host's match reaches <see cref="NetworkMatchController.MatchPhase.InProgress"/>;
/// everything before it is a pre-start failure that carries no loss (Game Design 01 §14).
/// </summary>
public static class RaidStartPolicy
{
    /// <summary>
    /// Whether the replicated match state proves the raid reached InProgress. A closure caused by a
    /// bootstrap failure or admission abort comes from Starting/Waiting, so it never started.
    /// </summary>
    public static bool HasEffectivelyStarted(NetworkMatchController.MatchPhase phase, RaidClosureReason closureReason)
    {
        switch (phase)
        {
            case NetworkMatchController.MatchPhase.InProgress:
                return true;
            case NetworkMatchController.MatchPhase.Closing:
            case NetworkMatchController.MatchPhase.Finished:
                return closureReason != RaidClosureReason.BootstrapFailure;
            default:
                return false;
        }
    }

    /// <summary>Only the State Authority reports the marker, once per raid, for an admitted reservation.</summary>
    public static bool ShouldReportRaidStarted(
        bool isStateAuthority, bool effectivelyStarted, bool alreadyReported, bool hasAdmittedReservation) =>
        isStateAuthority && effectivelyStarted && !alreadyReported && hasAdmittedReservation;

    /// <summary>
    /// A refund needs the admitted snapshot to restore and a raid that never started; any ending after
    /// InProgress (defeat, abandon, in-raid disconnect, closure) stays a loss.
    /// </summary>
    public static bool ShouldRequestPreStartRefund(bool hasAdmittedSnapshot, bool effectivelyStarted) =>
        hasAdmittedSnapshot && !effectivelyStarted;
}

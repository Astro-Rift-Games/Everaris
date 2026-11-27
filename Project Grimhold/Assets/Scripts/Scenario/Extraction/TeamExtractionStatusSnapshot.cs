/// <summary>
/// Read-only presentation snapshot of team extraction presence, current blocking reason, and last cancellation cause.
/// </summary>
public readonly struct TeamExtractionStatusSnapshot
{
    public int PresentCount { get; }
    public int RequiredCount { get; }
    public TeamExtractionCancelReason BlockReason { get; }
    public string BlockingMemberName { get; }
    public TeamExtractionCancelReason LastCancelReason { get; }
    public string LastCancelMemberName { get; }

    public TeamExtractionStatusSnapshot(
        int presentCount,
        int requiredCount,
        TeamExtractionCancelReason blockReason = TeamExtractionCancelReason.None,
        string blockingMemberName = null,
        TeamExtractionCancelReason lastCancelReason = TeamExtractionCancelReason.None,
        string lastCancelMemberName = null)
    {
        PresentCount = presentCount;
        RequiredCount = requiredCount;
        BlockReason = blockReason;
        BlockingMemberName = blockingMemberName;
        LastCancelReason = lastCancelReason;
        LastCancelMemberName = lastCancelMemberName;
    }
}

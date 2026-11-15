using Fusion;

/// <summary>Classifies distinguishable Fusion service failures without changing retry decisions.</summary>
public static class SessionStartFailurePolicy
{
    public static SessionTransitionResult Classify(ShutdownReason? reason) => reason switch
    {
        ShutdownReason.PhotonCloudTimeout or ShutdownReason.MaxCcuReached =>
            SessionTransitionResult.ServiceUnavailable,
        _ => SessionTransitionResult.ConnectionFailed
    };
}

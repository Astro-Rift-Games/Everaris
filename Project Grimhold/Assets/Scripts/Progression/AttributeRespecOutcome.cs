/// <summary>Result of one <see cref="AttributeRespecService.RespecAsync"/> attempt.</summary>
public readonly struct AttributeRespecOutcome
{
    public AttributeRespecStatus Status { get; }

    /// <summary>The backend error code that decided the attempt, or null when it succeeded.</summary>
    public string ErrorCode { get; }

    public string ErrorMessage { get; }

    /// <summary>
    /// True when the server's answer is known, so the transaction id can be discarded. False means the respec may
    /// have been applied, and the next attempt must reuse the same id so the server replays instead of charging again.
    /// </summary>
    public bool IsFinal { get; }

    public bool IsSuccess => Status == AttributeRespecStatus.Applied || Status == AttributeRespecStatus.AlreadyApplied;

    public AttributeRespecOutcome(AttributeRespecStatus status, string errorCode, string errorMessage, bool isFinal)
    {
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        IsFinal = isFinal;
    }
}

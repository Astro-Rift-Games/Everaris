/// <summary>Result of one tools/dev-grant run as reported by the script.</summary>
public readonly struct DevGrantOutcome
{
    public bool Success { get; }
    public string Message { get; }

    public DevGrantOutcome(bool success, string message)
    {
        Success = success;
        Message = message;
    }
}

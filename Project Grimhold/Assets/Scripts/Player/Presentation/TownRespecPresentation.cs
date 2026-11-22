/// <summary>Immutable Town projection of the attribute respec: cost, availability, confirmation and result text.</summary>
public readonly struct TownRespecPresentation
{
    /// <summary>The server's price in Gold, or null until it has been read.</summary>
    public int? Cost { get; }

    public TownRespecBlockReason BlockReason { get; }

    /// <summary>Why a respec cannot be requested, or null when it can.</summary>
    public string BlockMessage { get; }

    public bool IsConfirming { get; }
    public bool IsPending { get; }

    /// <summary>The explicit confirmation question while <see cref="IsConfirming"/>, otherwise null.</summary>
    public string ConfirmationText { get; }

    /// <summary>The last progress, result or rejection text, or null.</summary>
    public string Message { get; }

    public string CostText => TownRespecMessages.CostText(Cost);

    /// <summary>True when the player may request, or confirm, a respec now.</summary>
    public bool CanRespec => BlockReason == TownRespecBlockReason.None && !IsPending;

    /// <summary>What the panel shows under the button: the latest result, else the reason it is blocked.</summary>
    public string DisplayMessage => !string.IsNullOrEmpty(Message) ? Message : BlockMessage;

    public TownRespecPresentation(
        int? cost,
        TownRespecBlockReason blockReason,
        string blockMessage,
        bool isConfirming,
        bool isPending,
        string confirmationText,
        string message)
    {
        Cost = cost;
        BlockReason = blockReason;
        BlockMessage = blockMessage;
        IsConfirming = isConfirming;
        IsPending = isPending;
        ConfirmationText = confirmationText;
        Message = message;
    }
}

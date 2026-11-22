/// <summary>Why the Town attribute respec cannot be requested right now (TASK-304).</summary>
public enum TownRespecBlockReason
{
    None,

    /// <summary>No respec service is wired for this profile.</summary>
    Unavailable,

    /// <summary>Another attribute operation is in flight.</summary>
    Busy,

    /// <summary>Ready locks the build; Doc 04 §11 allows a respec only while No Listo.</summary>
    BuildLocked,

    /// <summary>A Raid reservation is pending, which the backend refuses to rewrite around.</summary>
    ReservationPending,

    /// <summary>The server's respec price has not been read yet.</summary>
    CostUnavailable,

    /// <summary>Every attribute is 0, so there is nothing to redistribute.</summary>
    NoAssignedPoints,

    /// <summary>The balance is below the server's respec price.</summary>
    InsufficientGold
}

/// <summary>How one attribute respec attempt ended, from the client's point of view (TASK-304).</summary>
public enum AttributeRespecStatus
{
    /// <summary>The server applied the respec and the local profile took its snapshot.</summary>
    Applied,

    /// <summary>The server replayed an earlier respec (<c>alreadySecured</c>) and the local profile took its snapshot.</summary>
    AlreadyApplied,

    /// <summary>The server refused with a definitive code; nothing was charged or changed.</summary>
    Rejected,

    /// <summary>The server applied the respec but the local profile refused the snapshot; a reread restored it.</summary>
    Diverged,

    /// <summary>No answer was received, so the respec may or may not have been applied; a reread ran.</summary>
    Unconfirmed,

    /// <summary>The attempt needed a reread of the backend profile and that reread failed.</summary>
    RecoveryFailed,

    /// <summary>No respec service is available; nothing was sent.</summary>
    Unavailable
}

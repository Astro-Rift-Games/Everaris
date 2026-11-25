public enum SanctuaryAssignmentFailureReason
{
    None,
    ServiceNotInitialized,
    InvalidPlayer,
    NoAuthority,
    OutsideSimulation,
    AssignmentNotFound,
    ProgressReaderUnavailable,
    InvalidProgressSnapshot,
    QuotaIncomplete,
    AssignmentNotRequested,
    PlayerUnavailable,
    NoSanctuariesConfigured,
    /// <summary>No longer produced: teams may share a Sanctuary (GD 10 section 8). Kept to preserve the enum contract.</summary>
    NoFreeSanctuary,
    SanctuaryRegistryInconsistent,
    DuplicateExistingAssignment,
    ReservationConflict,
    InvalidTeam
}

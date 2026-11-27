/// <summary>Outcome reported to a Town player when a Raid preparation action is rejected.</summary>
public enum TownRaidPreparationActionResult : byte
{
    Succeeded = 0,
    DirectoryUnavailable = 1,
    AlreadyInPreparation = 2,
    AwaitingPartyCompletion = 3,
    PartyUnavailable = 4,
    InvalidCode = 5,
    CodeNotFound = 6,
    PreparationFull = 7,
    PreparationLaunching = 8,
    NotInPreparation = 9,
    NotHost = 10,
    NotAllMembersReady = 11,
    Failed = 12
}

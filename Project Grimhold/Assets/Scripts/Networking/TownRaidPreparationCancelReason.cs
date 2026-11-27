/// <summary>Authoritative cause reported to affected members when a Town Raid preparation or launch is cancelled.</summary>
public enum TownRaidPreparationCancelReason : byte
{
    None = 0,
    HostLeft = 1,
    HostDisconnected = 2,
    AcknowledgeTimeout = 3,
    ReleaseTimeout = 4,
    MemberDisconnected = 5,
    MemberRejectedLaunch = 6,
    ReleaseFailed = 7
}

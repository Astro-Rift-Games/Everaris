/// <summary>Result of one reactivation attempt of a Reactivable room.</summary>
public enum EncounterReactivationOutcome : byte
{
    /// <summary>A Doc 10 §9.2 condition is not met (cooldown, MaxReactivations, players in the zone, capacity, phase). Normal, retried at the cadence.</summary>
    Blocked = 0,
    /// <summary>The room spawned its new composition and is Active again (possibly with fewer members than planned).</summary>
    Reactivated = 1,
    /// <summary>The initial weighted validation (Doc 10 §9.1) decided the room has no Encounter this cycle. No reactivation is consumed.</summary>
    NoEncounter = 2,
    /// <summary>No composition satisfies the room's limits. No reactivation is consumed.</summary>
    InvalidComposition = 3,
    /// <summary>The composition was planned but no enemy could be spawned. No reactivation is consumed.</summary>
    SpawnFailed = 4,
    /// <summary>The enemies were spawned but the registry refused the reactivation; they were despawned again.</summary>
    ActivationRejected = 5
}

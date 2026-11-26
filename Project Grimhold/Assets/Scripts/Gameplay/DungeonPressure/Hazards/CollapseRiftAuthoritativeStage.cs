/// <summary>
/// Authoritative lifecycle stage for a single floor collapse rift (Doc 20 §3, §5, §11).
/// Replicated from State Authority so Host, Clients, late-joiners, and Host Migration peers
/// observe identical rift states without local simulation drift.
/// </summary>
public enum CollapseRiftAuthoritativeStage : byte
{
    Hidden = 0,
    Spawning = 1,
    Dormant = 2,
    Telegraph = 3,
    Erupting = 4,
    Cooldown = 5,
    Disabled = 6
}

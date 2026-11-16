/// <summary>
/// Synchronous State Authority notifications for the lifetime of one Downed cycle.
/// Authoritative collaborators use direct calls, never presentation events.
/// </summary>
public interface IDownedCycleObserver
{
    /// <summary>
    /// <paramref name="causerId"/> is the credit causer: the direct attacker, or the still-valid causal pusher
    /// of environmental hazard damage (Doc 19 §8.1). Default means no player is responsible.
    /// <paramref name="empowerSourceId"/> is the supporter who empowered the downing hit (Doc 19 §6, FIX-80).
    /// </summary>
    void NotifyDownedEntered(int cycle, EntityId causerId, int simulationTick, RaidParticipantId empowerSourceId = default);
    void NotifyDownedCycleDefeated(int cycle);
    void NotifyDownedCycleRecovered(int cycle);
}

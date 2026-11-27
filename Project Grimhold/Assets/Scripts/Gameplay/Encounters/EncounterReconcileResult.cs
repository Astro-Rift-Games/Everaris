using System.Collections.Generic;

/// <summary>Outcome of comparing the restored registry state with the restored enemies.</summary>
public sealed class EncounterReconcileResult
{
    /// <summary>Alive in the registry, restored dead: the death was not recorded. The only mismatch that is corrected.</summary>
    public List<EncounterMemberObservation> UnrecordedDeaths { get; } = new List<EncounterMemberObservation>();

    /// <summary>Alive in the registry (encounter, key) with no restored enemy at all.</summary>
    public List<EncounterMemberObservation> MissingMembers { get; } = new List<EncounterMemberObservation>();

    /// <summary>A restored living enemy the registry does not expect alive (unknown room, inactive room or key not alive).</summary>
    public List<EncounterMemberObservation> UnexpectedLiving { get; } = new List<EncounterMemberObservation>();

    public bool IsConsistent => UnrecordedDeaths.Count == 0 && MissingMembers.Count == 0 && UnexpectedLiving.Count == 0;

    public string Log { get; set; } = string.Empty;
}

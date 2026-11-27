/// <summary>Doc 10 §9.2 basic cycle: No activada → Activa → Resuelta → Cooldown → Reactivable.</summary>
public enum EncounterState : byte
{
    NotActivated = 0,
    Active = 1,
    Resolved = 2,
    Cooldown = 3,
    Reactivable = 4
}

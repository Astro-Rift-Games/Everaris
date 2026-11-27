/// <summary>What an enemy death means for the Encounter registry.</summary>
public enum EncounterDeathRoute : byte
{
    /// <summary>The enemy belongs to no Encounter.</summary>
    NotEncounterMember = 0,

    /// <summary>The death must be registered as a valid elimination of the member.</summary>
    Notify = 1,

    /// <summary>The enemy names an Encounter but has no member key: authoring/spawn error.</summary>
    InvalidMember = 2
}

/// <summary>Routing rule for <c>EnemyCharacter.HandleDeath</c> (TASK-330 T4).</summary>
public static class EncounterDeathRouting
{
    /// <summary>
    /// Decides from the enemy's replicated Encounter identity whether its death is a member elimination. Applies to every
    /// death on State Authority: despawn and cleanup never reach <c>HandleDeath</c> and so never route here (TASK-330 L8).
    /// </summary>
    public static EncounterDeathRoute Resolve(int encounterIdValue, uint memberKey, out EncounterId encounterId)
    {
        encounterId = EncounterId.None;
        if (encounterIdValue <= 0)
        {
            return EncounterDeathRoute.NotEncounterMember;
        }

        if (memberKey == 0u)
        {
            return EncounterDeathRoute.InvalidMember;
        }

        encounterId = new EncounterId(encounterIdValue);
        return EncounterDeathRoute.Notify;
    }
}

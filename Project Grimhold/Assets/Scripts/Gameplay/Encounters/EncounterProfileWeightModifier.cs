using System;
using UnityEngine;

/// <summary>Weight multiplier an Encounter Profile applies to one enemy archetype (Doc 10 §9.1).</summary>
[Serializable]
public struct EncounterProfileWeightModifier
{
    [Tooltip("Enemy archetype id, as declared by EnemySpawnMetadata.")]
    public string EnemyId;

    [Min(0f), Tooltip("Multiplier applied to the archetype's Spawn Weight. 0 removes it from this profile.")]
    public float WeightMultiplier;

    public EncounterProfileWeightModifier(string enemyId, float weightMultiplier)
    {
        EnemyId = enemyId;
        WeightMultiplier = weightMultiplier;
    }
}

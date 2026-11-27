using System;
using UnityEngine;

/// <summary>
/// Encounter Profile (Doc 10 §9.1): modifies composition weights of a room Encounter, e.g. swarm, melee, ranged,
/// mixed or special. Archetypes without a modifier keep their Spawn Weight. Concrete profiles and values belong to
/// Game Design/Balance; the default asset is neutral.
/// </summary>
[CreateAssetMenu(fileName = "EncounterProfile", menuName = "Grimhold/Gameplay/Encounter Profile")]
public sealed class EncounterProfile : ScriptableObject
{
    [SerializeField] private EncounterProfileWeightModifier[] _weightModifiers = Array.Empty<EncounterProfileWeightModifier>();

    /// <summary>Returns the archetype's Spawn Weight under this profile; 0 excludes it.</summary>
    public int ApplyWeight(string enemyId, int baseWeight)
    {
        if (baseWeight <= 0)
        {
            return 0;
        }

        for (int i = 0; i < _weightModifiers.Length; i++)
        {
            EncounterProfileWeightModifier modifier = _weightModifiers[i];
            if (!string.Equals(modifier.EnemyId, enemyId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            float multiplier = modifier.WeightMultiplier;
            if (float.IsNaN(multiplier) || multiplier <= 0f)
            {
                return 0;
            }

            double weighted = Math.Round(baseWeight * (double)multiplier, MidpointRounding.AwayFromZero);
            return (int)Math.Max(1d, Math.Min(int.MaxValue, weighted));
        }

        return baseWeight;
    }
}

using System;
using UnityEngine;

/// <summary>Per-room composition parameters of Doc 10 §9.1.</summary>
[Serializable]
public sealed class EncounterCompositionSpec
{
    [SerializeField, Range(0f, 1f), Tooltip("Initial weighted validation: probability that the room has an Encounter.")]
    private float _encounterChance = 1f;

    [SerializeField, Min(1)] private int _minEnemies = 1;
    [SerializeField, Min(1)] private int _maxEnemies = 1;

    [SerializeField, Min(0), Tooltip("Local Threat Budget (Doc 10 §10.3). 0 means only MaxEnemies limits the composition.")]
    private int _threatBudget;

    [SerializeField, Tooltip("Allowed enemy archetype ids. Empty allows every eligible archetype.")]
    private string[] _allowedEnemyIds = Array.Empty<string>();

    [SerializeField, Tooltip("Optional Encounter Profile. Empty keeps base Spawn Weights.")]
    private EncounterProfile _profile;

    public EncounterCompositionSpec()
    {
    }

    public EncounterCompositionSpec(float encounterChance, int minEnemies, int maxEnemies, int threatBudget, string[] allowedEnemyIds, EncounterProfile profile)
    {
        _encounterChance = encounterChance;
        _minEnemies = minEnemies;
        _maxEnemies = maxEnemies;
        _threatBudget = threatBudget;
        _allowedEnemyIds = allowedEnemyIds ?? Array.Empty<string>();
        _profile = profile;
    }

    public float EncounterChance => _encounterChance;
    public int MinEnemies => _minEnemies;
    public int MaxEnemies => _maxEnemies;
    public int ThreatBudget => _threatBudget;
    public string[] AllowedEnemyIds => _allowedEnemyIds;
    public EncounterProfile Profile => _profile;

    public bool IsAllowed(string enemyId)
    {
        if (_allowedEnemyIds == null || _allowedEnemyIds.Length == 0)
        {
            return true;
        }

        for (int i = 0; i < _allowedEnemyIds.Length; i++)
        {
            if (string.Equals(_allowedEnemyIds[i], enemyId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

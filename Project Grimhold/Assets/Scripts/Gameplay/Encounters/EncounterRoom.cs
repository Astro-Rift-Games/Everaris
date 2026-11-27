using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authored Dungeon room that can host an Encounter (Doc 10 §9.1/§9.2): its identity, generation zone, enemy spawn
/// markers, composition parameters and Balance parameters. Static scene configuration only; runtime encounter
/// state lives elsewhere.
/// </summary>
[DisallowMultipleComponent]
public sealed class EncounterRoom : MonoBehaviour
{
    [SerializeField, Min(0), Tooltip("Unique positive Encounter id within the scene. 0 is not valid.")]
    private int _encounterId;

    [SerializeField, Tooltip("Generation zone. Players inside it block Encounter reactivation (Doc 10 §9.2).")]
    private Collider2D _generationZone;

    [SerializeField, Tooltip("Existing enemy spawn markers owned by this room. Referenced, never reparented. A marker may belong to one room only.")]
    private Transform[] _spawnPoints = Array.Empty<Transform>();

    [SerializeField]
    private EncounterCompositionSpec _composition = new EncounterCompositionSpec();

    [SerializeField, Min(0f), Tooltip("Seconds between resolution and reactivability (non-GD default, pending Balance).")]
    private float _cooldownSeconds = 120f;

    [SerializeField, Min(0), Tooltip("Maximum reactivations after the first activation. 0 never reactivates, today's behavior (non-GD default, pending Balance).")]
    private int _maxReactivations;

    public EncounterId Id => _encounterId > 0 ? new EncounterId(_encounterId) : EncounterId.None;
    public Collider2D GenerationZone => _generationZone;
    public IReadOnlyList<Transform> SpawnPoints => _spawnPoints;
    public EncounterCompositionSpec Composition => _composition;

    /// <summary>Balance parameters. Only valid once <see cref="Validate"/> succeeds.</summary>
    public EncounterLifecycleSettings LifecycleSettings => new EncounterLifecycleSettings(_cooldownSeconds, _maxReactivations);

    /// <summary>Checks the authored data. The failure names this room so the Inspector context is clear.</summary>
    public bool Validate(out string failure)
    {
        if (_encounterId <= 0)
        {
            return Fail($"has an invalid EncounterId ({_encounterId}); it must be positive.", out failure);
        }

        if (_generationZone == null)
        {
            return Fail("has no generation zone collider assigned.", out failure);
        }

        if (_composition == null)
        {
            return Fail("has no composition spec.", out failure);
        }

        if (_composition.MinEnemies < 1 || _composition.MaxEnemies < _composition.MinEnemies)
        {
            return Fail($"has inconsistent enemy counts: MinEnemies ({_composition.MinEnemies}) must be at least 1 and not greater than MaxEnemies ({_composition.MaxEnemies}).", out failure);
        }

        if (_composition.MaxEnemies > EncounterLimits.MaxMembersPerRoom)
        {
            return Fail($"has a MaxEnemies ({_composition.MaxEnemies}) above the replicated member cap ({EncounterLimits.MaxMembersPerRoom}).", out failure);
        }

        float chance = _composition.EncounterChance;
        if (float.IsNaN(chance) || chance < 0f || chance > 1f)
        {
            return Fail($"has an EncounterChance ({chance}) outside 0..1.", out failure);
        }

        if (float.IsNaN(_cooldownSeconds) || _cooldownSeconds < 0f)
        {
            return Fail($"has an invalid cooldown ({_cooldownSeconds}); it must be zero or positive.", out failure);
        }

        if (_maxReactivations < 0)
        {
            return Fail($"has an invalid MaxReactivations ({_maxReactivations}); it must be zero or positive.", out failure);
        }

        if (_spawnPoints == null || _spawnPoints.Length == 0)
        {
            return Fail("has no spawn points.", out failure);
        }

        var seen = new HashSet<Transform>();
        for (int i = 0; i < _spawnPoints.Length; i++)
        {
            Transform point = _spawnPoints[i];
            if (point == null)
            {
                return Fail($"has a null spawn point at index {i}.", out failure);
            }

            if (!seen.Add(point))
            {
                return Fail($"has a duplicate spawn point '{point.name}' at index {i}.", out failure);
            }
        }

        if (_composition.MinEnemies > _spawnPoints.Length)
        {
            return Fail($"requires MinEnemies ({_composition.MinEnemies}) but has only {_spawnPoints.Length} spawn points.", out failure);
        }

        failure = string.Empty;
        return true;
    }

    private bool Fail(string reason, out string failure)
    {
        failure = $"Encounter room '{name}' {reason}";
        return false;
    }
}

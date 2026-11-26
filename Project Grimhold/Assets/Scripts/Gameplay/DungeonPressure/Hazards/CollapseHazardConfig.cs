using UnityEngine;

/// <summary>
/// Inspector-configurable gameplay parameters for authoritative Collapse floor rifts,
/// environmental knockback protection, and global rupture waves (Doc 20 §4-§6, §10-§12).
/// </summary>
[CreateAssetMenu(fileName = "DefaultCollapseHazardConfig", menuName = "Grimhold/Dungeon Pressure/Collapse Hazard Config")]
public sealed class CollapseHazardConfig : ScriptableObject
{
    [Header("Floor Rift Placement Reference")]
    [Tooltip("Shared placement & sprite timing config used so authority and presentation match 100%.")]
    [SerializeField] private CollapseRiftConfig _riftConfig;

    [Header("Floor Rift Eruption Combat (TASK-335)")]
    [Tooltip("Radius in world units where a floor rift reports exposure and applies eruption damage/knockback.")]
    [Min(0.1f)] [SerializeField] private float _eruptionRadius = 1.25f;

    [Tooltip("Direct environmental damage inflicted once per eruption cycle to each valid target inside the radius.")]
    [Min(0f)] [SerializeField] private float _eruptionDamage = 15f;

    [Tooltip("Damage type used by floor rift eruptions.")]
    [SerializeField] private DamageType _eruptionDamageType = DamageType.Magical;

    [Tooltip("Original knockback power (PE, Doc 18) applied radially away from the rift center.")]
    [Min(0f)] [SerializeField] private float _eruptionKnockbackForce = 12f;

    [Tooltip("Brief protection window (seconds) blocking consecutive environmental knockbacks on the same target while damage still accumulates.")]
    [Min(0f)] [SerializeField] private float _environmentalKnockbackProtectionSeconds = 1.5f;

    [Tooltip("Physics layers queried for valid damageable targets (typically Character).")]
    [SerializeField] private LayerMask _targetLayerMask = ~0;

    [Tooltip("Pre-allocated overlap buffer size for non-allocating queries.")]
    [Min(4)] [SerializeField] private int _maxTargetsPerQuery = 32;

    [Header("Floor Rift Cycle Timings (TASK-335)")]
    [Tooltip("Local warning duration in seconds before an activated rift erupts.")]
    [Min(0.1f)] [SerializeField] private float _telegraphDurationSeconds = 2.0f;

    [Tooltip("Duration in seconds the rift remains in Erupting before transitioning to Cooldown.")]
    [Min(0.05f)] [SerializeField] private float _eruptionDurationSeconds = 0.6f;

    [Tooltip("Recovery duration in seconds before a rift returns to Dormant and can be activated again.")]
    [Min(0.1f)] [SerializeField] private float _cooldownDurationSeconds = 3.0f;

    [Header("Global Rupture Waves (TASK-336)")]
    [Tooltip("Global audiovisual warning duration (seconds) before the wave activates available rifts.")]
    [Min(0.1f)] [SerializeField] private float _globalWaveWarningSeconds = 1.5f;

    [Tooltip("Min/Max interval (seconds) between global waves during CriticalPressure (Deterioro crítico).")]
    [SerializeField] private Vector2 _criticalPressureWaveIntervalRange = new Vector2(18f, 24f);

    [Tooltip("Min/Max interval (seconds) between global waves during Collapse (Colapso).")]
    [SerializeField] private Vector2 _collapseWaveIntervalRange = new Vector2(9f, 13f);

    [Tooltip("Maximum number of rifts allowed to be simultaneously Telegraphing/Erupting per wave (0 = no cap).")]
    [Min(0)] [SerializeField] private int _maxConcurrentActiveRifts = 0;

    [Tooltip("Minimum world distance between rifts triggered in the same wave to prevent unavoidable overlaps (Doc 20 §4).")]
    [Min(0f)] [SerializeField] private float _minSimultaneousRiftSpacing = 3.0f;

    [Header("Global Wave Presentation Cue (TASK-336)")]
    [Min(0f)] [SerializeField] private float _waveWarningShakeIntensity = 0.35f;
    [Min(0f)] [SerializeField] private float _waveWarningShakeDuration = 1.2f;

    public CollapseRiftConfig RiftConfig => _riftConfig;
    public float EruptionRadius => _eruptionRadius;
    public float EruptionDamage => _eruptionDamage;
    public DamageType EruptionDamageType => _eruptionDamageType;
    public float EruptionKnockbackForce => _eruptionKnockbackForce;
    public float EnvironmentalKnockbackProtectionSeconds => _environmentalKnockbackProtectionSeconds;
    public LayerMask TargetLayerMask => _targetLayerMask;
    public int MaxTargetsPerQuery => _maxTargetsPerQuery;

    public float TelegraphDurationSeconds => _telegraphDurationSeconds;
    public float EruptionDurationSeconds => _eruptionDurationSeconds;
    public float CooldownDurationSeconds => _cooldownDurationSeconds;

    public float GlobalWaveWarningSeconds => _globalWaveWarningSeconds;
    public Vector2 CriticalPressureWaveIntervalRange => _criticalPressureWaveIntervalRange;
    public Vector2 CollapseWaveIntervalRange => _collapseWaveIntervalRange;
    public int MaxConcurrentActiveRifts => _maxConcurrentActiveRifts;
    public float MinSimultaneousRiftSpacing => _minSimultaneousRiftSpacing;

    public float WaveWarningShakeIntensity => _waveWarningShakeIntensity;
    public float WaveWarningShakeDuration => _waveWarningShakeDuration;

    public float SpawnDurationSeconds => _riftConfig != null
        ? (_riftConfig.FloorSpawnFrames != null && _riftConfig.FloorSpawnFrames.Length > 0
            ? _riftConfig.FloorSpawnFrames.Length / Mathf.Max(0.5f, _riftConfig.FloorSpawnFps)
            : 7f / Mathf.Max(0.5f, _riftConfig.FloorSpawnFps))
        : 0.6f;

    public bool Validate(out string error)
    {
        if (_eruptionRadius <= 0f || float.IsNaN(_eruptionRadius) || float.IsInfinity(_eruptionRadius))
        {
            error = "EruptionRadius must be > 0.";
            return false;
        }
        if (_eruptionDamage < 0f || float.IsNaN(_eruptionDamage) || float.IsInfinity(_eruptionDamage))
        {
            error = "EruptionDamage must be >= 0.";
            return false;
        }
        if (_eruptionKnockbackForce < 0f || float.IsNaN(_eruptionKnockbackForce) || float.IsInfinity(_eruptionKnockbackForce))
        {
            error = "EruptionKnockbackForce must be >= 0.";
            return false;
        }
        if (_environmentalKnockbackProtectionSeconds < 0f || float.IsNaN(_environmentalKnockbackProtectionSeconds))
        {
            error = "EnvironmentalKnockbackProtectionSeconds must be >= 0.";
            return false;
        }
        if (_telegraphDurationSeconds <= 0f || _eruptionDurationSeconds <= 0f || _cooldownDurationSeconds <= 0f)
        {
            error = "Rift cycle durations (telegraph, eruption, cooldown) must be > 0.";
            return false;
        }
        if (_globalWaveWarningSeconds <= 0f)
        {
            error = "GlobalWaveWarningSeconds must be > 0.";
            return false;
        }
        if (_criticalPressureWaveIntervalRange.x <= 0f || _criticalPressureWaveIntervalRange.y < _criticalPressureWaveIntervalRange.x)
        {
            error = "CriticalPressureWaveIntervalRange must have min > 0 and max >= min.";
            return false;
        }
        if (_collapseWaveIntervalRange.x <= 0f || _collapseWaveIntervalRange.y < _collapseWaveIntervalRange.x)
        {
            error = "CollapseWaveIntervalRange must have min > 0 and max >= min.";
            return false;
        }
        if (_collapseWaveIntervalRange.x > _criticalPressureWaveIntervalRange.x)
        {
            error = "Collapse wave min interval must be <= CriticalPressure wave min interval (waves intensify in Collapse).";
            return false;
        }
        if (_maxTargetsPerQuery < 4)
        {
            error = "MaxTargetsPerQuery must be >= 4.";
            return false;
        }

        error = null;
        return true;
    }

    public void ConfigureForTests(
        CollapseRiftConfig riftConfig,
        float eruptionRadius = 1.25f,
        float eruptionDamage = 15f,
        DamageType damageType = DamageType.Magical,
        float eruptionKnockbackForce = 12f,
        float environmentalKnockbackProtectionSeconds = 1.5f,
        float telegraphDurationSeconds = 0.5f,
        float eruptionDurationSeconds = 0.3f,
        float cooldownDurationSeconds = 0.5f,
        float globalWaveWarningSeconds = 0.25f,
        Vector2? criticalWaveRange = null,
        Vector2? collapseWaveRange = null,
        int maxConcurrentActiveRifts = 0,
        float minSimultaneousRiftSpacing = 0f,
        int targetLayerMask = ~0)
    {
        _riftConfig = riftConfig;
        _eruptionRadius = eruptionRadius;
        _eruptionDamage = eruptionDamage;
        _eruptionDamageType = damageType;
        _eruptionKnockbackForce = eruptionKnockbackForce;
        _environmentalKnockbackProtectionSeconds = environmentalKnockbackProtectionSeconds;
        _telegraphDurationSeconds = telegraphDurationSeconds;
        _eruptionDurationSeconds = eruptionDurationSeconds;
        _cooldownDurationSeconds = cooldownDurationSeconds;
        _globalWaveWarningSeconds = globalWaveWarningSeconds;
        _criticalPressureWaveIntervalRange = criticalWaveRange ?? new Vector2(10f, 12f);
        _collapseWaveIntervalRange = collapseWaveRange ?? new Vector2(5f, 6f);
        _maxConcurrentActiveRifts = maxConcurrentActiveRifts;
        _minSimultaneousRiftSpacing = minSimultaneousRiftSpacing;
        _targetLayerMask = targetLayerMask;
        _maxTargetsPerQuery = 32;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        _eruptionRadius = Mathf.Max(0.1f, _eruptionRadius);
        _eruptionDamage = Mathf.Max(0f, _eruptionDamage);
        _eruptionKnockbackForce = Mathf.Max(0f, _eruptionKnockbackForce);
        _environmentalKnockbackProtectionSeconds = Mathf.Max(0f, _environmentalKnockbackProtectionSeconds);
        _telegraphDurationSeconds = Mathf.Max(0.1f, _telegraphDurationSeconds);
        _eruptionDurationSeconds = Mathf.Max(0.05f, _eruptionDurationSeconds);
        _cooldownDurationSeconds = Mathf.Max(0.1f, _cooldownDurationSeconds);
        _globalWaveWarningSeconds = Mathf.Max(0.1f, _globalWaveWarningSeconds);
        _criticalPressureWaveIntervalRange.x = Mathf.Max(1f, _criticalPressureWaveIntervalRange.x);
        _criticalPressureWaveIntervalRange.y = Mathf.Max(_criticalPressureWaveIntervalRange.x, _criticalPressureWaveIntervalRange.y);
        _collapseWaveIntervalRange.x = Mathf.Max(1f, _collapseWaveIntervalRange.x);
        _collapseWaveIntervalRange.y = Mathf.Max(_collapseWaveIntervalRange.x, _collapseWaveIntervalRange.y);
        _maxTargetsPerQuery = Mathf.Max(4, _maxTargetsPerQuery);
    }
#endif
}

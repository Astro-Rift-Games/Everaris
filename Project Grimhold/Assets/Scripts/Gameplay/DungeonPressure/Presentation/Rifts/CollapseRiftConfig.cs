using UnityEngine;

/// <summary>
/// Shared static configuration for floor and wall collapse rifts (TASK-307 / #839).
/// Stores sprite sequences, timings, inspector-configurable wall frame interval,
/// placement margins/densities, sorting layers, and emissive Collapse lighting parameters.
/// </summary>
[CreateAssetMenu(fileName = "CollapseRiftConfig", menuName = "Grimhold/Dungeon Pressure/Collapse Rift Config")]
public sealed class CollapseRiftConfig : ScriptableObject
{
    [Header("Prefabs (Optional Overrides)")]
    [SerializeField] private CollapseFloorRiftVisual _floorRiftPrefab;
    [SerializeField] private CollapseWallRiftVisual _wallRiftPrefab;

    [Header("Floor Rift Sprites")]
    [SerializeField] private Sprite[] _floorSpawnFrames;
    [SerializeField] private Sprite _floorDormantSprite;
    [SerializeField] private Sprite[] _floorTelegraphFrames;
    [SerializeField] private Sprite[] _floorEruptionFrames;
    [SerializeField] private Sprite[] _floorCooldownFrames;

    [Header("Floor Rift Animation Timings")]
    [Min(0.5f)] [SerializeField] private float _floorSpawnFps = 12f;
    [Min(0.5f)] [SerializeField] private float _floorTelegraphFps = 8f;
    [Min(0.1f)] [SerializeField] private float _floorTelegraphDurationSeconds = 2.0f;
    [Min(0.5f)] [SerializeField] private float _floorEruptionFps = 12f;
    [Min(0.5f)] [SerializeField] private float _floorCooldownFps = 10f;

    [Header("Wall Rift Sprites")]
    [SerializeField] private Sprite[] _wallSpawnFrames;
    [SerializeField] private Sprite[] _wallStage1Frames;
    [SerializeField] private Sprite[] _wallStage2Frames;
    [SerializeField] private Sprite[] _wallStage3Frames;
    [SerializeField] private Sprite[] _wallStage4Frames;

    [Header("Wall Rift Animation Timing")]
    [Tooltip("Time in seconds between consecutive frames for wall rift animations (Etapa 2..4).")]
    [Min(0.02f)] [SerializeField] private float _wallFrameIntervalSeconds = 0.15f;

    [Header("Random Placement & Surface Margins")]
    [Min(0)] [SerializeField] private int _stage2FloorRiftCount = 24;
    [Min(0)] [SerializeField] private int _stage3FloorRiftCount = 24;
    [Min(0)] [SerializeField] private int _wallRiftCount = 28;
    [Min(0)] [SerializeField] private int _floorMarginCells = 1;
    [Min(1)] [SerializeField] private int _wallWidthCells = 2;
    [Min(1)] [SerializeField] private int _wallFaceHeightCells = 2;
    [Min(0)] [SerializeField] private int _wallHorizontalMarginCells = 1;
    [Min(0)] [SerializeField] private int _wallTopMarginCells = 1;
    [Min(0f)] [SerializeField] private float _minFloorRiftSpacing = 5.5f;
    [Min(0f)] [SerializeField] private float _minWallRiftSpacing = 6.0f;
    [Min(0f)] [SerializeField] private float _sanctuaryExclusionRadius = 4.5f;
    [Min(0f)] [SerializeField] private float _wallPropExclusionRadius = 2.0f;

    [Header("Visual Activation Cycles (Pre-NetworkFissure Presentation)")]
    [Tooltip("When enabled, active floor rifts trigger staggered periodic visual cycles (Stage2 -> Stage3 -> Stage4 -> Stage1).")]
    [SerializeField] private bool _autoTriggerFloorVisualCycles = true;
    [SerializeField] private Vector2 _stage3CycleIntervalRange = new Vector2(10f, 18f);
    [SerializeField] private Vector2 _stage4CycleIntervalRange = new Vector2(5f, 10f);

    [Header("Emissive Material & Local Point Lighting")]
    [SerializeField] private Material _riftMaterial;
    [SerializeField] private Color _emissiveLightColor = new Color(0.24f, 0.92f, 0.78f, 1f);
    [Min(0f)] [SerializeField] private float _telegraphEmissionStrength = 1.2f;
    [Min(0f)] [SerializeField] private float _eruptionEmissionStrength = 2.2f;
    [Min(0f)] [SerializeField] private float _cooldownEmissionStrength = 0.65f;
    [Min(0f)] [SerializeField] private float _telegraphLightIntensity = 0.9f;
    [Min(0.1f)] [SerializeField] private float _telegraphLightRadius = 2.4f;
    [Min(0f)] [SerializeField] private float _eruptionLightIntensity = 1.75f;
    [Min(0.1f)] [SerializeField] private float _eruptionLightRadius = 3.6f;
    [Min(0f)] [SerializeField] private float _cooldownLightIntensity = 0.45f;
    [Min(0.1f)] [SerializeField] private float _cooldownLightRadius = 2.0f;
    [SerializeField] private bool _enableSubtleParticles = true;

    [Header("Sorting Layers")]
    [SerializeField] private string _floorSortingLayerName = "Floor";
    [SerializeField] private int _floorSortingOrder = 2;
    [SerializeField] private string _wallSortingLayerName = "Walls";
    [SerializeField] private int _wallSortingOrder = 3;
    [SerializeField] private string _particleSortingLayerName = "VFX";
    [SerializeField] private int _particleSortingOrder = 2;

    public CollapseFloorRiftVisual FloorRiftPrefab => _floorRiftPrefab;
    public CollapseWallRiftVisual WallRiftPrefab => _wallRiftPrefab;

    public Sprite[] FloorSpawnFrames => _floorSpawnFrames;
    public Sprite FloorDormantSprite => _floorDormantSprite;
    public Sprite[] FloorTelegraphFrames => _floorTelegraphFrames;
    public Sprite[] FloorEruptionFrames => _floorEruptionFrames;
    public Sprite[] FloorCooldownFrames => _floorCooldownFrames;

    public float FloorSpawnFps => _floorSpawnFps;
    public float FloorTelegraphFps => _floorTelegraphFps;
    public float FloorTelegraphDurationSeconds => _floorTelegraphDurationSeconds;
    public float FloorEruptionFps => _floorEruptionFps;
    public float FloorCooldownFps => _floorCooldownFps;

    public Sprite[] WallSpawnFrames => _wallSpawnFrames;
    public Sprite[] WallStage1Frames => _wallStage1Frames;
    public Sprite[] WallStage2Frames => _wallStage2Frames;
    public Sprite[] WallStage3Frames => _wallStage3Frames;
    public Sprite[] WallStage4Frames => _wallStage4Frames;
    public float WallFrameIntervalSeconds => _wallFrameIntervalSeconds;

    public int Stage2FloorRiftCount => _stage2FloorRiftCount;
    public int Stage3FloorRiftCount => _stage3FloorRiftCount;
    public int WallRiftCount => _wallRiftCount;
    public bool AutoTriggerFloorVisualCycles => _autoTriggerFloorVisualCycles;
    public Vector2 Stage3CycleIntervalRange => _stage3CycleIntervalRange;
    public Vector2 Stage4CycleIntervalRange => _stage4CycleIntervalRange;

    public Material RiftMaterial => _riftMaterial;
    public Color EmissiveLightColor => _emissiveLightColor;
    public float TelegraphEmissionStrength => _telegraphEmissionStrength;
    public float EruptionEmissionStrength => _eruptionEmissionStrength;
    public float CooldownEmissionStrength => _cooldownEmissionStrength;
    public float TelegraphLightIntensity => _telegraphLightIntensity;
    public float TelegraphLightRadius => _telegraphLightRadius;
    public float EruptionLightIntensity => _eruptionLightIntensity;
    public float EruptionLightRadius => _eruptionLightRadius;
    public float CooldownLightIntensity => _cooldownLightIntensity;
    public float CooldownLightRadius => _cooldownLightRadius;
    public bool EnableSubtleParticles => _enableSubtleParticles;

    public string FloorSortingLayerName => _floorSortingLayerName;
    public int FloorSortingOrder => _floorSortingOrder;
    public string WallSortingLayerName => _wallSortingLayerName;
    public int WallSortingOrder => _wallSortingOrder;
    public string ParticleSortingLayerName => _particleSortingLayerName;
    public int ParticleSortingOrder => _particleSortingOrder;

    public CollapseRiftPlacementSettings BuildPlacementSettings()
    {
        return new CollapseRiftPlacementSettings(
            _stage2FloorRiftCount,
            _stage3FloorRiftCount,
            _wallRiftCount,
            _floorMarginCells,
            _wallWidthCells,
            _wallFaceHeightCells,
            _wallHorizontalMarginCells,
            _wallTopMarginCells,
            _minFloorRiftSpacing,
            _minWallRiftSpacing,
            _sanctuaryExclusionRadius,
            _wallPropExclusionRadius);
    }

    public CollapseRiftTimingSettings BuildFloorTimingSettings(float phaseOffsetSeconds)
    {
        return new CollapseRiftTimingSettings(
            _floorSpawnFrames != null && _floorSpawnFrames.Length > 0 ? _floorSpawnFrames.Length : 7,
            _floorSpawnFps,
            _floorTelegraphFrames != null && _floorTelegraphFrames.Length > 0 ? _floorTelegraphFrames.Length : 7,
            _floorTelegraphFps,
            _floorTelegraphDurationSeconds,
            _floorEruptionFrames != null && _floorEruptionFrames.Length > 0 ? _floorEruptionFrames.Length : 7,
            _floorEruptionFps,
            _floorCooldownFrames != null && _floorCooldownFrames.Length > 0 ? _floorCooldownFrames.Length : 8,
            _floorCooldownFps,
            phaseOffsetSeconds);
    }

    public Sprite GetFloorSprite(CollapseRiftVisualState state, int frameIndex)
    {
        return state switch
        {
            CollapseRiftVisualState.Spawning => GetClampedSprite(_floorSpawnFrames, frameIndex),
            CollapseRiftVisualState.Dormant => _floorDormantSprite != null
                ? _floorDormantSprite
                : GetClampedSprite(_floorSpawnFrames, _floorSpawnFrames != null ? _floorSpawnFrames.Length - 1 : 0),
            CollapseRiftVisualState.Telegraph => GetClampedSprite(_floorTelegraphFrames, frameIndex),
            CollapseRiftVisualState.Erupting => GetClampedSprite(_floorEruptionFrames, frameIndex),
            CollapseRiftVisualState.Cooldown => GetClampedSprite(_floorCooldownFrames, frameIndex),
            _ => null
        };
    }

    public Sprite GetWallSprite(CollapseRiftVisualState stage, int frameIndex)
    {
        return stage switch
        {
            CollapseRiftVisualState.Spawning => GetClampedSprite(_wallSpawnFrames, frameIndex),
            CollapseRiftVisualState.Dormant => GetClampedSprite(_wallStage1Frames, frameIndex),
            CollapseRiftVisualState.Telegraph => GetClampedSprite(_wallStage2Frames, frameIndex),
            CollapseRiftVisualState.Erupting => GetClampedSprite(_wallStage3Frames, frameIndex),
            CollapseRiftVisualState.Cooldown => GetClampedSprite(_wallStage4Frames, frameIndex),
            _ => null
        };
    }

    public void ConfigureForTests(
        Sprite[] floorSpawn,
        Sprite floorDormant,
        Sprite[] floorTelegraph,
        Sprite[] floorEruption,
        Sprite[] floorCooldown,
        Sprite[] wallSpawn,
        Sprite[] wallStage1,
        Sprite[] wallStage2,
        Sprite[] wallStage3,
        Sprite[] wallStage4,
        int stage2FloorCount,
        int stage3FloorCount,
        int wallCount,
        float wallFrameIntervalSeconds = 0.15f,
        int floorMarginCells = 1,
        int wallHorizontalMarginCells = 1,
        int wallTopMarginCells = 1,
        float minFloorSpacing = 2.0f,
        float minWallSpacing = 2.5f)
    {
        _floorSpawnFrames = floorSpawn;
        _floorDormantSprite = floorDormant;
        _floorTelegraphFrames = floorTelegraph;
        _floorEruptionFrames = floorEruption;
        _floorCooldownFrames = floorCooldown;
        _wallSpawnFrames = wallSpawn;
        _wallStage1Frames = wallStage1;
        _wallStage2Frames = wallStage2;
        _wallStage3Frames = wallStage3;
        _wallStage4Frames = wallStage4;
        _stage2FloorRiftCount = stage2FloorCount;
        _stage3FloorRiftCount = stage3FloorCount;
        _wallRiftCount = wallCount;
        _wallFrameIntervalSeconds = Mathf.Max(0.02f, wallFrameIntervalSeconds);
        _floorMarginCells = floorMarginCells;
        _wallHorizontalMarginCells = wallHorizontalMarginCells;
        _wallTopMarginCells = wallTopMarginCells;
        _minFloorRiftSpacing = minFloorSpacing;
        _minWallRiftSpacing = minWallSpacing;
        _enableSubtleParticles = false;
    }

    private static Sprite GetClampedSprite(Sprite[] sprites, int frameIndex)
    {
        if (sprites == null || sprites.Length == 0)
        {
            return null;
        }

        int clamped = Mathf.Clamp(frameIndex, 0, sprites.Length - 1);
        return sprites[clamped];
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_wallFrameIntervalSeconds < 0.02f) _wallFrameIntervalSeconds = 0.02f;
        if (_stage3CycleIntervalRange.x < 1f) _stage3CycleIntervalRange.x = 1f;
        if (_stage3CycleIntervalRange.y < _stage3CycleIntervalRange.x) _stage3CycleIntervalRange.y = _stage3CycleIntervalRange.x;
        if (_stage4CycleIntervalRange.x < 1f) _stage4CycleIntervalRange.x = 1f;
        if (_stage4CycleIntervalRange.y < _stage4CycleIntervalRange.x) _stage4CycleIntervalRange.y = _stage4CycleIntervalRange.x;
    }
#endif
}

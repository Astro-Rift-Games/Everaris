using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Visual presentation component for a single wall collapse rift.
/// Driven by <see cref="CollapseWallRiftStepper"/> with Inspector-configurable frame interval:
/// - Stage 1 (Normal): hidden
/// - Stage 2 (Reinforcements): Stage0-Spawn -> Stage1
/// - Stage 3 (CriticalPressure): Stage1 + Stage2 loop
/// - Stage 4 (Collapse): Stage2 + Stage3 + Stage4 loop with emissive glow and local light.
/// </summary>
[DisallowMultipleComponent]
public sealed class CollapseWallRiftVisual : MonoBehaviour
{
    private static readonly int EmissionStrengthId = Shader.PropertyToID("_EmissionStrength");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [SerializeField] private SpriteRenderer _spriteRenderer;
    [SerializeField] private Light2D _localLight;

    private CollapseRiftConfig _config;
    private CollapseWallRiftStepper _stepper;
    private MaterialPropertyBlock _propertyBlock;
    private bool _useProceduralShader;
    private Sprite _proceduralSprite;

    public CollapseRiftVisualState VisualState => _stepper != null ? _stepper.CurrentStage : CollapseRiftVisualState.Hidden;
    public bool UsesProceduralShader => _useProceduralShader;
    public int CurrentStageFrameIndex => _stepper != null ? _stepper.CurrentStageFrameIndex : 0;
    public float FrameIntervalSeconds => _stepper != null ? _stepper.FrameIntervalSeconds : 0f;
    public SpriteRenderer Renderer => _spriteRenderer;
    public Light2D LocalLight => _localLight;

    public void Initialize(
        CollapseRiftConfig config,
        float phaseOffsetSeconds,
        float frameIntervalOverrideSeconds = 0f)
    {
        _config = config;
        EnsureComponents();

        float interval = frameIntervalOverrideSeconds > 0f
            ? frameIntervalOverrideSeconds
            : config.WallFrameIntervalSeconds;

        _stepper = new CollapseWallRiftStepper(
            spawnFrameCount: config.WallSpawnFrames != null && config.WallSpawnFrames.Length > 0 ? config.WallSpawnFrames.Length : 7,
            stage1FrameCount: config.WallStage1Frames != null && config.WallStage1Frames.Length > 0 ? config.WallStage1Frames.Length : 1,
            stage2FrameCount: config.WallStage2Frames != null && config.WallStage2Frames.Length > 0 ? config.WallStage2Frames.Length : 7,
            stage3FrameCount: config.WallStage3Frames != null && config.WallStage3Frames.Length > 0 ? config.WallStage3Frames.Length : 7,
            stage4FrameCount: config.WallStage4Frames != null && config.WallStage4Frames.Length > 0 ? config.WallStage4Frames.Length : 8,
            frameIntervalSeconds: interval,
            phaseOffsetSeconds: phaseOffsetSeconds);

        ApplyVisualOutputs();
    }

    /// <summary>
    /// Binds a baked per-raid atlas sprite and shared procedural material once (TASK-341).
    /// Clears any MaterialPropertyBlock so wall rifts batch cleanly via SpriteRenderer.color.
    /// </summary>
    public void SetProceduralVisual(Sprite bakedSprite, Material sharedProceduralMaterial)
    {
        EnsureComponents();
        _useProceduralShader = bakedSprite != null;
        _proceduralSprite = bakedSprite;
        if (_useProceduralShader && _spriteRenderer != null)
        {
            _spriteRenderer.sprite = _proceduralSprite;
            if (sharedProceduralMaterial != null)
            {
                _spriteRenderer.sharedMaterial = sharedProceduralMaterial;
            }
            _spriteRenderer.SetPropertyBlock(null);
        }
        ApplyVisualOutputs();
    }

    public void SetFrameIntervalSeconds(float seconds)
    {
        if (_stepper == null || seconds <= 0f)
        {
            return;
        }

        if (!Mathf.Approximately(_stepper.FrameIntervalSeconds, seconds))
        {
            _stepper.SetFrameIntervalSeconds(seconds);
            ApplyVisualOutputs();
        }
    }

    public void ApplyDungeonPhase(DungeonPressurePhase phase, bool immediate)
    {
        if (_stepper == null)
        {
            return;
        }

        _stepper.ApplyDungeonPhase(phase, immediate);
        ApplyVisualOutputs();
    }

    public void TickVisual(float deltaTime)
    {
        if (_stepper == null || deltaTime <= 0f)
        {
            return;
        }

        _stepper.Tick(deltaTime);
        ApplyVisualOutputs();
    }

    private void EnsureComponents()
    {
        if (_spriteRenderer == null)
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_spriteRenderer == null)
            {
                _spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            }
        }

        if (_config != null)
        {
            _spriteRenderer.sortingLayerName = !string.IsNullOrEmpty(_config.WallSortingLayerName)
                ? _config.WallSortingLayerName
                : "Walls";
            _spriteRenderer.sortingOrder = _config.WallSortingOrder;

            if (_config.RiftMaterial != null)
            {
                _spriteRenderer.sharedMaterial = _config.RiftMaterial;
            }
        }

        if (_localLight == null)
        {
            _localLight = GetComponentInChildren<Light2D>(includeInactive: true);
            if (_localLight == null)
            {
                var lightGo = new GameObject("WallRiftLight2D");
                lightGo.transform.SetParent(transform, false);
                // Center point light over the 32px vertical wall crack (pixels y=[16..46], ~0.5u below 48x48 center).
                lightGo.transform.localPosition = new Vector3(0f, -0.5f, 0f);
                _localLight = lightGo.AddComponent<Light2D>();
                _localLight.lightType = Light2D.LightType.Point;
                _localLight.pointLightInnerRadius = 0.2f;
                _localLight.falloffIntensity = 0.65f;
            }
        }

        _propertyBlock ??= new MaterialPropertyBlock();
    }

    private void ApplyVisualOutputs()
    {
        if (_stepper == null || _config == null || _spriteRenderer == null)
        {
            return;
        }

        CollapseRiftVisualState stage = _stepper.CurrentStage;
        bool visible = stage != CollapseRiftVisualState.Hidden;
        _spriteRenderer.enabled = visible;

        if (!visible)
        {
            if (_useProceduralShader)
            {
                _spriteRenderer.color = Color.clear;
            }
            if (_localLight != null)
            {
                _localLight.enabled = false;
                _localLight.intensity = 0f;
            }
            return;
        }

        float emissionStrength = 0f;
        float lightIntensity = 0f;
        float lightRadius = _config.TelegraphLightRadius;

        switch (stage)
        {
            case CollapseRiftVisualState.Telegraph:
                emissionStrength = _config.TelegraphEmissionStrength;
                lightIntensity = _config.TelegraphLightIntensity * 0.85f;
                lightRadius = _config.TelegraphLightRadius;
                break;

            case CollapseRiftVisualState.Erupting:
                emissionStrength = _config.EruptionEmissionStrength;
                lightIntensity = _config.EruptionLightIntensity * 0.9f;
                lightRadius = _config.EruptionLightRadius;
                break;

            case CollapseRiftVisualState.Cooldown:
            {
                float remaining = 1f - _stepper.CurrentStageNormalizedProgress;
                emissionStrength = _config.CooldownEmissionStrength * remaining;
                lightIntensity = _config.CooldownLightIntensity * remaining;
                lightRadius = _config.CooldownLightRadius;
                break;
            }
        }

        if (_useProceduralShader)
        {
            if (_spriteRenderer.sprite != _proceduralSprite)
            {
                _spriteRenderer.sprite = _proceduralSprite;
            }

            float pulse = stage == CollapseRiftVisualState.Telegraph
                ? 0.72f + 0.28f * Mathf.Sin(_stepper.CurrentStageNormalizedProgress * Mathf.PI * 2f)
                : 1f;

            _spriteRenderer.color = CollapseRiftShaderParameterMapper.Evaluate(
                stage,
                _stepper.CurrentStageNormalizedProgress,
                pulse,
                _config.TelegraphEmissionStrength,
                _config.EruptionEmissionStrength,
                _config.CooldownEmissionStrength,
                _config.SpawnFrontEmissionStrength);
        }
        else
        {
            _spriteRenderer.sprite = _config.GetWallSprite(stage, _stepper.CurrentStageFrameIndex);
            _spriteRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(EmissionStrengthId, emissionStrength);
            _propertyBlock.SetColor(EmissionColorId, _config.EmissiveLightColor);
            _spriteRenderer.SetPropertyBlock(_propertyBlock);
        }

        if (_localLight != null)
        {
            bool lightActive = lightIntensity > 0.01f;
            _localLight.enabled = lightActive;
            _localLight.color = _config.EmissiveLightColor;
            _localLight.intensity = lightIntensity;
            _localLight.pointLightOuterRadius = lightRadius;
        }
    }
}

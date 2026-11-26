using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Visual presentation component for a single floor collapse rift.
/// Drives sprite frames, selective turquoise emission, local 2D point lighting, and optional eruption particles
/// from <see cref="CollapseRiftStateMachine"/> without adding physics colliders or blocking navigation.
/// Exposes <see cref="TryTriggerActivation"/> and <see cref="ForceVisualState"/> for future NetworkFissure integration.
/// </summary>
[DisallowMultipleComponent]
public sealed class CollapseFloorRiftVisual : MonoBehaviour
{
    private static readonly int EmissionStrengthId = Shader.PropertyToID("_EmissionStrength");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [SerializeField] private SpriteRenderer _spriteRenderer;
    [SerializeField] private Light2D _localLight;
    [SerializeField] private ParticleSystem _eruptionParticles;

    private CollapseRiftConfig _config;
    private CollapseRiftStateMachine _stateMachine;
    private MaterialPropertyBlock _propertyBlock;
    private DungeonPressurePhase _currentPhase = DungeonPressurePhase.Normal;
    private CollapseRiftVisualState _lastAppliedState = CollapseRiftVisualState.Hidden;
    private float _autoCycleTimer;
    private float _autoCycleDuration;
    private float _phaseOffsetSeconds;

    public CollapseRiftVisualState VisualState => _stateMachine != null ? _stateMachine.State : CollapseRiftVisualState.Hidden;
    public CollapseRiftBatch Batch => _stateMachine != null ? _stateMachine.Batch : CollapseRiftBatch.Stage2Initial;
    public bool IsActivatable => _stateMachine != null && _stateMachine.IsActivatable;
    public SpriteRenderer Renderer => _spriteRenderer;
    public Light2D LocalLight => _localLight;

    public void Initialize(
        CollapseRiftConfig config,
        CollapseRiftBatch batch,
        float phaseOffsetSeconds,
        float initialAutoCycleDelaySeconds)
    {
        _config = config;
        _phaseOffsetSeconds = Mathf.Max(0f, phaseOffsetSeconds);
        _autoCycleDuration = Mathf.Max(1f, initialAutoCycleDelaySeconds);
        _autoCycleTimer = 0f;

        EnsureComponents();

        _stateMachine = new CollapseRiftStateMachine(batch, config.BuildFloorTimingSettings(_phaseOffsetSeconds));
        _lastAppliedState = CollapseRiftVisualState.Hidden;
        ApplyVisualOutputs(isStateTransition: false);
    }

    public void ApplyDungeonPhase(DungeonPressurePhase phase, bool immediate)
    {
        _currentPhase = phase;
        if (_stateMachine == null)
        {
            return;
        }

        CollapseRiftVisualState previous = _stateMachine.State;
        _stateMachine.ApplyDungeonPhase(phase, immediate);
        RefreshAutoCycleDuration(phase);

        if (immediate)
        {
            _autoCycleTimer = _phaseOffsetSeconds % Mathf.Max(1f, _autoCycleDuration);
        }

        ApplyVisualOutputs(isStateTransition: !immediate && _stateMachine.State != previous);
    }

    public bool TryTriggerActivation()
    {
        if (_stateMachine == null)
        {
            return false;
        }

        bool triggered = _stateMachine.TryTriggerActivation();
        if (triggered)
        {
            ApplyVisualOutputs(isStateTransition: true);
        }
        return triggered;
    }

    public void ForceVisualState(CollapseRiftVisualState state)
    {
        if (_stateMachine == null)
        {
            return;
        }

        bool changed = _stateMachine.State != state;
        _stateMachine.ForceVisualState(state);
        ApplyVisualOutputs(isStateTransition: changed);
    }

    /// <summary>
    /// Mirrors authoritative state from <see cref="CollapseHazardDirector"/> (TASK-335).
    /// Suppresses particle bursts during late-join / reconnect / Host Migration snaps.
    /// </summary>
    public void SyncFromAuthority(
        CollapseRiftVisualState authoritativeState,
        float stateElapsedSeconds,
        bool isActivatable,
        bool suppressTransitionEffects)
    {
        if (_stateMachine == null)
        {
            return;
        }

        CollapseRiftVisualState previous = _stateMachine.State;
        _stateMachine.SyncFromAuthority(authoritativeState, stateElapsedSeconds, isActivatable);
        ApplyVisualOutputs(isStateTransition: !suppressTransitionEffects && _stateMachine.State != previous);
    }

    public void TickVisual(float deltaTime)
    {
        if (_stateMachine == null || _config == null || deltaTime <= 0f)
        {
            return;
        }

        if (_config.AutoTriggerFloorVisualCycles &&
            _stateMachine.IsActivatable &&
            _stateMachine.State == CollapseRiftVisualState.Dormant)
        {
            _autoCycleTimer += deltaTime;
            if (_autoCycleTimer >= _autoCycleDuration)
            {
                _autoCycleTimer = 0f;
                _stateMachine.TryTriggerActivation();
            }
        }

        CollapseRiftVisualState previous = _stateMachine.State;
        _stateMachine.Tick(deltaTime);
        ApplyVisualOutputs(isStateTransition: _stateMachine.State != previous);
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
            _spriteRenderer.sortingLayerName = !string.IsNullOrEmpty(_config.FloorSortingLayerName)
                ? _config.FloorSortingLayerName
                : "Floor";
            _spriteRenderer.sortingOrder = _config.FloorSortingOrder;

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
                var lightGo = new GameObject("RiftLight2D");
                lightGo.transform.SetParent(transform, false);
                _localLight = lightGo.AddComponent<Light2D>();
                _localLight.lightType = Light2D.LightType.Point;
                _localLight.pointLightInnerRadius = 0.2f;
                _localLight.falloffIntensity = 0.65f;
            }
        }

        if (_config != null && _config.EnableSubtleParticles && _eruptionParticles == null)
        {
            _eruptionParticles = CreateSubtleEruptionParticles();
        }

        _propertyBlock ??= new MaterialPropertyBlock();
    }

    private ParticleSystem CreateSubtleEruptionParticles()
    {
        var go = new GameObject("EruptionParticles");
        go.transform.SetParent(transform, false);
        go.SetActive(false);

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.55f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startColor = _config != null ? _config.EmissiveLightColor : new Color(0.24f, 0.92f, 0.78f, 0.85f);
        main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 16;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.35f;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.sortingLayerName = _config != null && !string.IsNullOrEmpty(_config.ParticleSortingLayerName)
                ? _config.ParticleSortingLayerName
                : "VFX";
            renderer.sortingOrder = _config != null ? _config.ParticleSortingOrder : 2;
            Material circleMat = DungeonPressureAmbientPresenter.GetOrCreateCircleMaterial();
            if (circleMat != null)
            {
                renderer.sharedMaterial = circleMat;
            }
        }

        go.SetActive(true);
        return ps;
    }

    private void RefreshAutoCycleDuration(DungeonPressurePhase phase)
    {
        if (_config == null)
        {
            return;
        }

        Vector2 range = phase == DungeonPressurePhase.Collapse
            ? _config.Stage4CycleIntervalRange
            : _config.Stage3CycleIntervalRange;

        float t = (_phaseOffsetSeconds % 1f);
        _autoCycleDuration = Mathf.Lerp(Mathf.Max(1f, range.x), Mathf.Max(range.x, range.y), t);
    }

    private void ApplyVisualOutputs(bool isStateTransition)
    {
        if (_stateMachine == null || _config == null || _spriteRenderer == null)
        {
            return;
        }

        CollapseRiftVisualState state = _stateMachine.State;
        bool visible = state != CollapseRiftVisualState.Hidden;
        _spriteRenderer.enabled = visible;

        if (!visible)
        {
            if (_localLight != null)
            {
                _localLight.enabled = false;
                _localLight.intensity = 0f;
            }
            _lastAppliedState = state;
            return;
        }

        _spriteRenderer.sprite = _config.GetFloorSprite(state, _stateMachine.CurrentFrameIndex);

        float emissionStrength = 0f;
        float lightIntensity = 0f;
        float lightRadius = _config.TelegraphLightRadius;

        switch (state)
        {
            case CollapseRiftVisualState.Telegraph:
            {
                float pulse = 0.72f + 0.28f * Mathf.Sin((_stateMachine.StateElapsedSeconds + _phaseOffsetSeconds) * 6.5f);
                emissionStrength = _config.TelegraphEmissionStrength * pulse;
                lightIntensity = _config.TelegraphLightIntensity * pulse;
                lightRadius = _config.TelegraphLightRadius;
                break;
            }

            case CollapseRiftVisualState.Erupting:
                emissionStrength = _config.EruptionEmissionStrength;
                lightIntensity = _config.EruptionLightIntensity;
                lightRadius = _config.EruptionLightRadius;
                break;

            case CollapseRiftVisualState.Cooldown:
            {
                int frameCount = _config.FloorCooldownFrames != null && _config.FloorCooldownFrames.Length > 0
                    ? _config.FloorCooldownFrames.Length
                    : 8;
                float remaining = 1f - Mathf.Clamp01((float)_stateMachine.CurrentFrameIndex / Mathf.Max(1, frameCount - 1));
                emissionStrength = _config.CooldownEmissionStrength * remaining;
                lightIntensity = _config.CooldownLightIntensity * remaining;
                lightRadius = _config.CooldownLightRadius;
                break;
            }
        }

        _spriteRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetFloat(EmissionStrengthId, emissionStrength);
        _propertyBlock.SetColor(EmissionColorId, _config.EmissiveLightColor);
        _spriteRenderer.SetPropertyBlock(_propertyBlock);

        if (_localLight != null)
        {
            bool lightActive = lightIntensity > 0.01f;
            _localLight.enabled = lightActive;
            _localLight.color = _config.EmissiveLightColor;
            _localLight.intensity = lightIntensity;
            _localLight.pointLightOuterRadius = lightRadius;
        }

        if (isStateTransition && state != _lastAppliedState && _eruptionParticles != null && Application.isPlaying)
        {
            if (state == CollapseRiftVisualState.Erupting)
            {
                _eruptionParticles.Emit(10);
            }
            else if (state == CollapseRiftVisualState.Spawning)
            {
                _eruptionParticles.Emit(4);
            }
        }

        _lastAppliedState = state;
    }
}

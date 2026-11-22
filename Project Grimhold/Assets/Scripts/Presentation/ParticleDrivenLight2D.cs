using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Visual-only 2D light that fades in while a particle system emits and fades out when it stops,
/// so charge, trail and impact effects light their surroundings without owning any gameplay state.
/// </summary>
[DisallowMultipleComponent]
public sealed class ParticleDrivenLight2D : MonoBehaviour
{
    [SerializeField] private Light2D _light;
    [Tooltip("Emitter that drives the light. The light is lit only while this system is emitting.")]
    [SerializeField] private ParticleSystem _particles;
    [SerializeField, Min(0f)] private float _fadeInSeconds = .05f;
    [SerializeField, Min(0f)] private float _fadeOutSeconds = .15f;

    private float _authoredIntensity;
    private float _level;
    private bool _initialized;

    /// <summary>Assigns the dependencies and restarts the fade from dark, keeping the light's current intensity as its peak.</summary>
    public void Configure(Light2D light, ParticleSystem particles, float fadeInSeconds, float fadeOutSeconds)
    {
        _light = light;
        _particles = particles;
        _fadeInSeconds = Mathf.Max(0f, fadeInSeconds);
        _fadeOutSeconds = Mathf.Max(0f, fadeOutSeconds);
        _initialized = false;
        Initialize();
    }

    private void Awake() => Initialize();

    private void LateUpdate() => Advance(Time.deltaTime);

    /// <summary>Advances the fade by the given time and applies it to the light.</summary>
    public void Advance(float deltaSeconds)
    {
        Initialize();
        if (_light == null) return;
        bool lit = _particles != null && _particles.isEmitting;
        _level = ParticleLightLevel.Step(_level, lit, deltaSeconds, _fadeInSeconds, _fadeOutSeconds);
        Apply();
    }

    private void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        if (_light == null) _light = GetComponent<Light2D>();
        if (_light == null)
        {
            Debug.LogError("Particle-driven light requires a Light2D.", this);
            enabled = false;
            return;
        }
        _authoredIntensity = _light.intensity;
        _level = 0f;
        Apply();
    }

    private void Apply()
    {
        _light.intensity = _authoredIntensity * _level;
        _light.enabled = _level > 0f;
    }
}

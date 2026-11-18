using UnityEngine;

/// <summary>Optional local flight visuals. Never changes shared ranged attack simulation.</summary>
[CreateAssetMenu(fileName = "ProjectileVisualProfile", menuName = "Grimhold/Combat/Projectile Visual Profile")]
public sealed class ProjectileVisualProfile : ScriptableObject
{
    [SerializeField] private Sprite _sprite;
    [SerializeField] private RuntimeAnimatorController _animatorController;
    [SerializeField] private bool _overrideTint;
    [SerializeField] private Color _tint = Color.white;
    [SerializeField] private bool _useDamageTypeTint;
    [Tooltip("Visual-only prefab containing TrailRenderer and/or ParticleSystem children. No NetworkObject or gameplay components.")]
    [SerializeField] private GameObject _trailPrefab;
    [SerializeField] private ParticleSystem _fizzleParticles;
    [Tooltip("World-visible impact on geometry AND characters. Supersedes the projectile default, not attacker-local weapon feedback.")]
    [SerializeField] private ParticleSystem _impactParticles;

    [Header("Stick On Impact")]
    [Tooltip("Leaves a local copy of the flight sprite embedded where the projectile hit a wall, floor or body.")]
    [SerializeField] private bool _stickOnImpact;
    [Tooltip("World units the cut edge of the stuck copy sits past the impact point along the flight direction.")]
    [SerializeField, Min(0f)] private float _stickPenetration;
    [Tooltip("Visible width of the projectile at the cut edge, in world units. Oblique hits sink the cut edge by up to this much so both of its ends reach the surface. 0 disables the sinking.")]
    [SerializeField, Min(0f)] private float _stickEdgeWidth;
    [Tooltip("Fraction of the sprite length left visible outside the surface; the front (tip) part is cut so the arrow looks embedded.")]
    [SerializeField, Range(0.05f, 1f)] private float _stickVisibleFraction = 0.5f;
    [Tooltip("Seconds the stuck copy stays fully visible before fading.")]
    [SerializeField, Min(0f)] private float _stickHoldSeconds = 4.5f;
    [Tooltip("Seconds the stuck copy takes to fade out before it is destroyed.")]
    [SerializeField, Min(0f)] private float _stickFadeSeconds = 0.5f;
    [Tooltip("Sorting layer used when stuck to a body so it draws over the character. Empty keeps the flight sorting.")]
    [SerializeField] private string _stickBodySortingLayer = "VFX";

    public Sprite Sprite => _sprite;
    public RuntimeAnimatorController AnimatorController => _animatorController;
    public bool OverrideTint => _overrideTint;
    public Color Tint => _tint;
    public bool UseDamageTypeTint => _useDamageTypeTint;
    public GameObject TrailPrefab => _trailPrefab;
    public ParticleSystem FizzleParticles => _fizzleParticles;
    public ParticleSystem ImpactParticles => _impactParticles;
    public bool StickOnImpact => _stickOnImpact;
    public float StickPenetration => _stickPenetration;
    public float StickEdgeWidth => _stickEdgeWidth;
    public float StickVisibleFraction => _stickVisibleFraction;
    public float StickHoldSeconds => _stickHoldSeconds;
    public float StickFadeSeconds => _stickFadeSeconds;
    public string StickBodySortingLayer => _stickBodySortingLayer;
}

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

    public Sprite Sprite => _sprite;
    public RuntimeAnimatorController AnimatorController => _animatorController;
    public bool OverrideTint => _overrideTint;
    public Color Tint => _tint;
    public bool UseDamageTypeTint => _useDamageTypeTint;
    public GameObject TrailPrefab => _trailPrefab;
    public ParticleSystem FizzleParticles => _fizzleParticles;
    public ParticleSystem ImpactParticles => _impactParticles;
}

using UnityEngine;

/// <summary>
/// World-visible impact on characters AND geometry (intentional for all projectiles).
/// Independent of attacker-only WeaponDefinition.Presentation.ImpactParticles; leave that optional
/// layer unassigned on ranged weapons when a single world-visible burst is desired.
/// </summary>
[DisallowMultipleComponent]
public sealed class ProjectileImpactParticlePresenter : MonoBehaviour
{
    [SerializeField] private NetworkProjectile _projectile;
    [Tooltip("Optional one-shot default impact particles. A flight profile's impact supersedes this.")]
    [SerializeField] private ParticleSystem _impactParticles;
    [Tooltip("Optional damage-type tint; null preserves the particle template colors.")]
    [SerializeField] private AttackVfxTintPalette _tintPalette;
    private ProjectileVisualProfilePresenter _visual;
    private DamageType _damageType;
    private void Awake()
    {
        if (_projectile == null) _projectile = GetComponent<NetworkProjectile>();
        _visual = GetComponent<ProjectileVisualProfilePresenter>();
    }
    private void OnEnable() { if (_projectile != null) _projectile.ImpactResolved += PresentImpact; }
    private void OnDisable() { if (_projectile != null) _projectile.ImpactResolved -= PresentImpact; }
    public void BeginFlight(DamageType damageType) => _damageType = damageType;
    public void PresentImpact(Vector2 position, bool isGeometryImpact)
    {
        if (_visual == null) _visual = GetComponent<ProjectileVisualProfilePresenter>();
        if (_impactParticles == null || (_visual != null && _visual.HasImpactOverride)) return;
        Color? tint = _tintPalette != null && _tintPalette.TryGetTint(_damageType, out Color color) ? color : (Color?)null;
        ProjectileParticlePlayer.Play(_impactParticles, position, transform.rotation, tint);
    }
}

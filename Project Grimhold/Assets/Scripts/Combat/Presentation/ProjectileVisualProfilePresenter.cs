using UnityEngine;

/// <summary>
/// Per-flight local visuals resolved from replicated weapon identity, on every peer.
/// NetworkProjectile owns BeginFlight / RestoreDefaults ordering around its terminal events.
/// Wire a catalog or equipment catalog resolver explicitly; missing or stale identities are silent.
/// </summary>
[DisallowMultipleComponent]
public sealed class ProjectileVisualProfilePresenter : MonoBehaviour
{
    [SerializeField] private PlayerWeaponEquipmentNetworkController _equipmentSource;
    [Tooltip("Optional prefab-assignable catalog. Uses the same deterministic catalog lookup as Equipment; no live player reference is required.")]
    [SerializeField] private LootDefinitionCatalog _catalog;
    [SerializeField] private SpriteRenderer _spriteRenderer;
    [SerializeField] private Animator _animator;
    [SerializeField] private AttackVfxTintPalette _tintPalette;
    [SerializeField] private ProjectileTrailDetacher _trailDetacher;
    private NetworkProjectile _projectile;
    private ProjectileVisualProfile _profile;
    private Color? _tint;
    private Sprite _originalSprite;
    private Color _originalColor;
    private RuntimeAnimatorController _originalController;
    private bool _originalAnimatorEnabled;
    private bool _applied;

    public bool HasImpactOverride => isActiveAndEnabled && _profile != null && _profile.ImpactParticles != null;
    public bool HasFizzleOverride => isActiveAndEnabled && _profile != null && _profile.FizzleParticles != null;

    private void Awake()
    {
        _projectile = GetComponent<NetworkProjectile>();
        if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
        if (_animator == null) _animator = GetComponent<Animator>();
        if (_trailDetacher == null) _trailDetacher = GetComponent<ProjectileTrailDetacher>();
    }
    private void OnEnable()
    {
        if (_projectile == null) return;
        _projectile.ImpactResolved += PresentImpact;
        _projectile.Expired += PresentExpiry;
    }
    private void OnDisable()
    {
        if (_projectile != null)
        {
            _projectile.ImpactResolved -= PresentImpact;
            _projectile.Expired -= PresentExpiry;
        }
        RestoreDefaults();
    }

    public void BeginFlight(WeaponAttackTag tag, DamageType damageType)
    {
        RestoreDefaults();
        if (!tag.HasWeaponTag || tag.WeaponCatalogIndexPlusOne <= 0) return;
        LootDefinition loot;
        if (_catalog != null)
        {
            if (!_catalog.TryGetByIndex(tag.WeaponCatalogIndexPlusOne - 1, out loot) ||
                loot == null || loot.WeaponDefinition == null) return;
        }
        else if (_equipmentSource == null ||
            !_equipmentSource.TryGetWeaponByCatalogIndexPlusOne(tag.WeaponCatalogIndexPlusOne, out loot)) return;
        _profile = loot.WeaponDefinition.ProjectileVisual;
        if (_profile == null) return;
        _applied = true;
        if (_spriteRenderer != null)
        {
            _originalSprite = _spriteRenderer.sprite;
            _originalColor = _spriteRenderer.color;
        }
        if (_animator != null)
        {
            _originalController = _animator.runtimeAnimatorController;
            _originalAnimatorEnabled = _animator.enabled;
            if (_profile.AnimatorController != null)
            {
                _animator.runtimeAnimatorController = _profile.AnimatorController;
                _animator.enabled = true;
            }
            else if (_profile.Sprite != null) _animator.enabled = false;
        }
        _tint = _profile.OverrideTint ? _profile.Tint : (Color?)null;
        if (_profile.UseDamageTypeTint && _tintPalette != null && _tintPalette.TryGetTint(damageType, out Color tint))
            _tint = tint;
        if (_spriteRenderer != null)
        {
            if (_profile.Sprite != null) _spriteRenderer.sprite = _profile.Sprite;
            if (_tint.HasValue) _spriteRenderer.color = _tint.Value;
        }
        if (_profile.TrailPrefab != null && _trailDetacher != null)
        {
            GameObject trail = Instantiate(_profile.TrailPrefab, transform, false);
            _trailDetacher.Register(trail);
            if (_tint.HasValue)
            {
                foreach (TrailRenderer renderer in trail.GetComponentsInChildren<TrailRenderer>(true))
                { renderer.startColor = _tint.Value; renderer.endColor = _tint.Value; }
                foreach (ParticleSystem particles in trail.GetComponentsInChildren<ParticleSystem>(true))
                { var main = particles.main; main.startColor = _tint.Value; }
            }
        }
    }

    public void RestoreDefaults()
    {
        if (_applied)
        {
            if (_animator != null)
            {
                _animator.runtimeAnimatorController = _originalController;
                _animator.enabled = _originalAnimatorEnabled;
            }
            if (_spriteRenderer != null)
            { _spriteRenderer.sprite = _originalSprite; _spriteRenderer.color = _originalColor; }
        }
        _applied = false;
        _profile = null;
        _tint = null;
    }
    public void PresentImpact(Vector2 position, bool geometry)
    {
        if (HasImpactOverride) ProjectileParticlePlayer.Play(_profile.ImpactParticles, position, transform.rotation, _tint, oneShot: true);
    }
    public void PresentExpiry(Vector2 position, Vector2 direction)
    {
        if (!HasFizzleOverride) return;
        Quaternion rotation = direction.sqrMagnitude > .0001f
            ? Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg) : transform.rotation;
        ProjectileParticlePlayer.Play(_profile.FizzleParticles, position, rotation, _tint, oneShot: true);
    }
}

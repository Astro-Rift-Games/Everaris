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
    private readonly Collider2D[] _anchorBuffer = new Collider2D[8];
    private readonly RaycastHit2D[] _normalBuffer = new RaycastHit2D[8];
    private ContactFilter2D _anchorFilter;
    // Probe depth past the contact surface along the flight, so the point lands inside the struck collider.
    private const float AnchorProbeDepth = 0.02f;
    // The surface normal ray starts this far before the contact point and spans twice that distance.
    private const float NormalProbeBackoff = 0.1f;

    public bool HasImpactOverride => isActiveAndEnabled && _profile != null && _profile.ImpactParticles != null;
    public bool HasFizzleOverride => isActiveAndEnabled && _profile != null && _profile.FizzleParticles != null;
    public bool HasStickOverride => isActiveAndEnabled && _profile != null && _profile.StickOnImpact;

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
        if (!HasStickOverride) return;
        // A projectile stopped by a defensive interceptor has no surface to stick into.
        if (_projectile != null && _projectile.WasImpactIntercepted) return;
        Transform target = null;
        Vector2 targetOffset = default;
        Vector2 surface = position;
        int layerMask = Physics2D.AllLayers;
        if (_projectile != null)
        {
            // The authority's contact surface replaces the projectile center, which sits a collider radius short.
            if (_projectile.TryGetImpactSurfacePoint(out Vector2 recorded)) surface = recorded;
            layerMask = _projectile.ImpactLayerMask;
            if (!geometry) _projectile.TryGetImpactTarget(out target, out targetOffset);
        }
        PresentStick(surface, target, targetOffset, layerMask);
    }

    /// <summary>
    /// Leaves a local stuck copy of the current flight sprite with its tip cut, so only the trailing
    /// <see cref="ProjectileVisualProfile.StickVisibleFraction"/> shows outside the surface.
    /// <paramref name="position"/> is the contact surface point. With a body, the replicated offset from the
    /// body root replaces it so the copy lands on the body on every peer. The collider found under the stuck
    /// point on <paramref name="anchorLayerMask"/> (inside the body when there is one) anchors the copy, so it
    /// ends with a broken or removed object instead of floating in empty space.
    /// </summary>
    public StuckProjectileVisual PresentStick(Vector2 position, Transform target, Vector2 targetOffset,
        int anchorLayerMask = Physics2D.AllLayers)
    {
        if (!HasStickOverride || _spriteRenderer == null || _spriteRenderer.sprite == null) return null;
        // A body that is not solid right now (for example an incorporeal specter) takes no stuck copy.
        if (target != null && !StuckProjectileVisual.BodyHoldsStuckProjectiles(target)) return null;
        Vector2 impact = target != null ? (Vector2)target.position + targetOffset : position;
        Vector2 flight = transform.right;
        // The cut edge lands on the surface, so only the renderer offset across the flight keeps its meaning.
        Vector3 rendererOffset = _spriteRenderer.transform.position - transform.position;
        rendererOffset -= (Vector3)(flight * Vector2.Dot(rendererOffset, flight));
        int sortingLayerId = _spriteRenderer.sortingLayerID;
        if (target != null && !string.IsNullOrEmpty(_profile.StickBodySortingLayer))
        {
            int bodyLayerId = SortingLayer.NameToID(_profile.StickBodySortingLayer);
            if (SortingLayer.IsValid(bodyLayerId)) sortingLayerId = bodyLayerId;
            else Debug.LogWarning($"{nameof(ProjectileVisualProfile)} stick body sorting layer '{_profile.StickBodySortingLayer}' does not exist; the flight sorting layer is used.", _profile);
        }
        Collider2D anchor = ResolveAnchor(impact + flight * AnchorProbeDepth, target, anchorLayerMask);
        // An oblique hit sinks the cut edge so both of its ends reach the surface; the kept length grows to match.
        float extraDepth = ResolveObliqueDepth(impact, flight, anchor, anchorLayerMask);
        Vector2 embedded = impact + flight * (_profile.StickPenetration + extraDepth);
        return StuckProjectileVisual.Create(_spriteRenderer, (Vector3)embedded + rendererOffset, flight,
            _profile.StickVisibleFraction, target, _profile.StickHoldSeconds, _profile.StickFadeSeconds, sortingLayerId,
            anchor, extraDepth);
    }

    /// <summary>
    /// Extra depth along the flight for the profile edge width, from the local surface normal at the contact
    /// point. Zero when the profile has no edge width or no surface is found (treated as a head-on hit).
    /// </summary>
    private float ResolveObliqueDepth(Vector2 surface, Vector2 flight, Collider2D anchor, int layerMask)
    {
        if (_profile.StickEdgeWidth <= 0f) return 0f;
        if (!TryGetSurfaceNormal(surface, flight, anchor, layerMask, out Vector2 normal)) return 0f;
        return StuckProjectileVisual.ComputeObliqueDepth(flight, normal, _profile.StickEdgeWidth);
    }

    /// <summary>
    /// Casts a short ray along the flight across the contact point and returns the normal of the first surface
    /// it crosses, preferring <paramref name="anchor"/>. Works on any collider shape, including composite walls.
    /// </summary>
    private bool TryGetSurfaceNormal(Vector2 surface, Vector2 flight, Collider2D anchor, int layerMask, out Vector2 normal)
    {
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(layerMask);
        Vector2 origin = surface - flight * NormalProbeBackoff;
        int count = Physics2D.Raycast(origin, flight, filter, _normalBuffer, NormalProbeBackoff * 2f);
        normal = default;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = _normalBuffer[i];
            if (hit.collider == null || IsIgnoredSurface(hit.collider)) continue;
            if (!found || hit.collider == anchor)
            {
                normal = hit.normal;
                found = true;
            }
            if (anchor == null || hit.collider == anchor) break;
        }
        System.Array.Clear(_normalBuffer, 0, count);
        return found;
    }

    /// <summary>This projectile's own colliders, other projectiles and protective orbs are never a surface.</summary>
    private bool IsIgnoredSurface(Collider2D candidate) =>
        candidate.transform.IsChildOf(transform) ||
        candidate.GetComponentInParent<NetworkProjectile>() != null ||
        candidate.GetComponent<ProtectiveOrbCollider>() != null;

    /// <summary>
    /// Finds the collider holding the stuck point once per impact. Skips this projectile's own colliders and
    /// protective orbs; a body impact only accepts colliders inside the body. Null leaves the visual unanchored.
    /// </summary>
    private Collider2D ResolveAnchor(Vector2 point, Transform target, int layerMask)
    {
        _anchorFilter = new ContactFilter2D { useTriggers = true };
        _anchorFilter.SetLayerMask(layerMask);
        int count = Physics2D.OverlapPoint(point, _anchorFilter, _anchorBuffer);
        Collider2D found = null;
        for (int i = 0; i < count; i++)
        {
            Collider2D candidate = _anchorBuffer[i];
            if (candidate == null || IsIgnoredSurface(candidate)) continue;
            if (target == null) { found = candidate; break; }
            if (candidate.transform.IsChildOf(target)) { found = candidate; break; }
        }
        System.Array.Clear(_anchorBuffer, 0, count);
        return found;
    }
    public void PresentExpiry(Vector2 position, Vector2 direction)
    {
        if (!HasFizzleOverride) return;
        Quaternion rotation = direction.sqrMagnitude > .0001f
            ? Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg) : transform.rotation;
        ProjectileParticlePlayer.Play(_profile.FizzleParticles, position, rotation, _tint, oneShot: true);
    }
}

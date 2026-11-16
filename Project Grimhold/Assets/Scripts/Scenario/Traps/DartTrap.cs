using Fusion;
using UnityEngine;

/// <summary>
/// Trampa de dardos que utiliza FusionProjectileSpawner y RangedAttackConfig
/// para disparar proyectiles de red de forma autoritativa.
///
/// Utiliza exactamente el mismo sistema de proyectiles que RangeEnemy y RangePlayer
/// (<see cref="NetworkProjectile"/>), desplazando el volumen físico del Collider2D
/// e infligiendo daño mediante <see cref="IDamageResolver"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class DartTrap : BaseTrap
{
    [Header("Configuración de Dirección y Disparo")]
    [SerializeField] private Vector2 _direction = Vector2.right;
    [SerializeField] private int _dartsAmount = 3;
    [SerializeField] private float _dartInterval = 0.5f;
    [SerializeField] private Transform _refPoint;

    [Header("Configuración de Ataque y Spawner")]
    [SerializeField] private RangedAttackConfig _attackConfig;
    [SerializeField] private FusionProjectileSpawner _projectileSpawner;
    [SerializeField, Min(0.1f)] private float _maximumRange = 5f;

    [Header("Área de Trayectoria y Detección Causal")]
    [SerializeField] private Collider2D _trajectoryCollider;
    [SerializeField] private LayerMask _targetLayerMask;
    [SerializeField] private int _maxTargetsBuffer = 32;

    [Networked] private int DartsShot { get; set; }
    [Networked] private TickTimer DartIntervalTimer { get; set; }

    private Collider2D[] _colliderBuffer;
    private ContactFilter2D _contactFilter;
    private EntityRegistry _registry;
    private readonly System.Collections.Generic.List<EntityId> _processedTargets = new();

    /// <summary>Identidad estable del peligro para la atribución causal ambiental (Doc 19 §8.2).</summary>
    public EntityId HazardId => Object != null && Object.IsValid ? new EntityId(unchecked((int)Object.Id.Raw)) : default;

    public override void Spawned()
    {
        base.Spawned();

        _colliderBuffer = new Collider2D[_maxTargetsBuffer];
        _contactFilter = new ContactFilter2D
        {
            useLayerMask = true,
            useTriggers = true
        };
        LayerMask mask = _targetLayerMask.value != 0 ? _targetLayerMask : LayerMask.GetMask("Character");
        _contactFilter.SetLayerMask(mask);

        if (Runner != null)
        {
            _registry = Runner.GetComponent<EntityRegistry>();
        }

        // Buscar automáticamente el spawner en la jerarquía si no se asignó en el Inspector
        if (_projectileSpawner == null)
        {
            _projectileSpawner = GetComponent<FusionProjectileSpawner>() ?? GetComponentInChildren<FusionProjectileSpawner>();
        }
    }

    /// <summary>
    /// Reporta cada tick la exposición de los jugadores en la trayectoria del dardo para que
    /// un empuje hacia la línea de fuego conserve su causalidad (Doc 19 §8.1-§8.3, TASK-247, FIX-89).
    /// </summary>
    protected override void OnAuthorityTick()
    {
        if (_registry == null)
        {
            if (Runner != null) _registry = Runner.GetComponent<EntityRegistry>();
            if (_registry == null) return;
        }

        Collider2D areaCollider = _trajectoryCollider != null ? _trajectoryCollider : _detectionCol;
        if (areaCollider == null) return;

        _processedTargets.Clear();
        int hitCount = areaCollider.Overlap(_contactFilter, _colliderBuffer);
        EntityId hazardId = HazardId;
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D col = _colliderBuffer[i];
            if (TryResolveTarget(col, out _, out IDamageable damageable) &&
                damageable is PlayerCharacter player && player.HazardExposureCausality != null)
            {
                player.HazardExposureCausality.ReportHazardPresence(hazardId);
            }
        }

        System.Array.Clear(_colliderBuffer, 0, hitCount);
    }

    private bool TryResolveTarget(Collider2D col, out EntityId targetId, out IDamageable damageable)
    {
        damageable = null;
        targetId = default;
        if (col == null || !_registry.TryGetEntityId(col, out targetId) || targetId.Value == 0 ||
            !_registry.IsDamageCollider(targetId, col) || _processedTargets.Contains(targetId))
        {
            return false;
        }

        _processedTargets.Add(targetId);
        return _registry.TryGetDamageable(targetId, out damageable) &&
            (damageable is not ICharacter character || character.IsAlive);
    }

    protected override void OnEnterActive()
    {
        base.OnEnterActive();
        // Mantiene habilitado el collider de trayectoria para que los jugadores sigan reportando exposición durante los disparos.
        if (_detectionCol != null)
        {
            _detectionCol.enabled = true;
        }
        DartsShot = 0;
        DartIntervalTimer = default;
    }

    protected override void UpdateActive()
    {
        if (DartsShot >= _dartsAmount) return;
        if (!DartIntervalTimer.ExpiredOrNotRunning(Runner)) return;

        ShootDart();
        DartsShot++;

        if (DartsShot < _dartsAmount)
        {
            DartIntervalTimer = TickTimer.CreateFromSeconds(Runner, _dartInterval);
        }
    }

    /// <summary>
    /// Emite la solicitud de spawn del proyectil al FusionProjectileSpawner de forma autoritativa.
    /// Utiliza la dirección Vector2 especificada en el Inspector.
    /// </summary>
    private void ShootDart()
    {
        if (_projectileSpawner == null)
        {
            Debug.LogWarning($"{nameof(DartTrap)}: Falta la referencia a FusionProjectileSpawner en {gameObject.name}.", this);
            return;
        }

        if (_attackConfig == null || trapInfo == null)
        {
            Debug.LogWarning($"{nameof(DartTrap)}: Falta la configuración RangedAttackConfig en {gameObject.name}.", this);
            return;
        }

        Transform originTransform = _refPoint != null ? _refPoint : transform;
        Vector2 dir = _direction.sqrMagnitude > 0f ? _direction.normalized : Vector2.right;

        Vector2 spawnOrigin = (Vector2)originTransform.position + dir * _attackConfig.ProjectileSpawnOffset;

        // EntityId(0) identifica a la trampa/escenario como el origen de daño ambiental, con causalidad en HazardId
        ProjectileSpawnRequest request = new ProjectileSpawnRequest(
            new EntityId(0),
            spawnOrigin,
            dir,
            trapInfo.damage,
            trapInfo.DamageType,
            _attackConfig.ProjectileSpeed,
            _attackConfig.LifetimeSeconds,
            _maximumRange,
            Runner.Tick,
            environmentalHazardId: HazardId
        );

        _projectileSpawner.Spawn(in request);
    }
}

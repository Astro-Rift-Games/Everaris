using System;
using Fusion;
using UnityEngine;

/// <summary>
/// Network component responsible for processing enemy AI combat decisions
/// and delegating attack execution to the active attack strategy.
///
/// Authoritative timing (TASK-41 / TASK-316):
/// - State Authority owns a configurable <see cref="TickTimer"/> anticipation window before
///   executing damage or spawning projectiles.
/// - Animation and presentation layers observe replicated anticipation state
///   (<see cref="IsPreparingAttack"/>, <see cref="PreparedAttackDirection"/>,
///   <see cref="PreparationSequence"/>) and NEVER decide when damage executes.
/// - Disabling Animator or presenters has zero impact on combat cadence or hit timing.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyCombatAIController : NetworkBehaviour, ICombatController
{
    [Header("Authoritative Anticipation")]
    [Tooltip("Configurable wind-up window in seconds before damage or projectile release (driven by Fusion TickTimer).")]
    [SerializeField, Min(0.05f)]
    private float _anticipationDurationSeconds = 0.35f;

    [Tooltip("When false (default per TASK-41), canceling a prepared attack before release does not consume full cooldown.")]
    [SerializeField]
    private bool _consumeCooldownOnCancel = false;

    [Header("Dependencies")]
    [SerializeField]
    private MonoBehaviour _characterSource;

    [SerializeField]
    private Transform _attackOrigin;

    [SerializeField]
    private MonoBehaviour _activeAttackSource;

    [SerializeField]
    private EnemyMovementAIController _movementController;

    private EnemyStunNetworkEffect _stun;
    private ICharacter _character;
    private IAttack _activeAttack;
    private EntityRegistry _entityRegistry;
    private bool _dependenciesValid;
    private int _lastObservedSequence;

    // Pending attack payload frozen on State Authority at preparation start and committed when AnticipationTimer expires.
    private AttackRequest _pendingDamageRequest;
    private EntityId _pendingTargetId;
    private bool _hasPendingDamage;

    [Networked]
    private TickTimer AttackCooldown { get; set; }

    [Networked]
    private TickTimer AnticipationTimer { get; set; }

    [Networked]
    public NetworkBool IsAttackEnabled { get; private set; }

    /// <summary>True while an authoritative anticipation window is active before hit/shot release.</summary>
    [Networked]
    public NetworkBool IsPreparingAttack { get; private set; }

    /// <summary>Normalized direction of the attack currently being prepared (readable by proxies).</summary>
    [Networked]
    public Vector2 PreparedAttackDirection { get; private set; }

    /// <summary>Monotonic counter incremented each time a new anticipation window begins.</summary>
    [Networked]
    public int PreparationSequence { get; private set; }

    /// <summary>Serialized <see cref="AttackType"/> value of the prepared attack (Melee vs Ranged).</summary>
    [Networked]
    private int PreparedAttackTypeValue { get; set; }

    // Replicated state for presentation layers on execution
    [Networked]
    private int AttackSequence { get; set; }

    [Networked]
    private Vector2 LastAttackOrigin { get; set; }

    [Networked]
    private Vector2 LastAttackDirection { get; set; }

    [Networked]
    private int LastAttackTypeValue { get; set; }

    [Networked]
    private int LastAttackTick { get; set; }

    /// <summary>Configured anticipation duration in seconds.</summary>
    public float AnticipationDurationSeconds => _anticipationDurationSeconds;

    /// <summary>Attack type currently being prepared (for differentiated Melee/Ranged presentation).</summary>
    public AttackType PreparedAttackType => (AttackType)PreparedAttackTypeValue;

    /// <summary>World-space transform used as the attack origin.</summary>
    public Transform AttackOrigin => _attackOrigin != null ? _attackOrigin : transform;

    /// <summary>
    /// Normalized [0..1] anticipation progress derived from the replicated <see cref="AnticipationTimer"/>.
    /// Safe to read from presentation on Host, Client, and Proxies.
    /// </summary>
    public float PreparationProgress
    {
        get
        {
            if (Object == null || !Object.IsValid || !IsPreparingAttack || Runner == null)
            {
                return 0f;
            }

            if (AnticipationTimer.ExpiredOrNotRunning(Runner))
            {
                return 1f;
            }

            float remaining = AnticipationTimer.RemainingTime(Runner) ?? 0f;
            return EnemyAttackAnticipationRules.ComputeProgress(remaining, _anticipationDurationSeconds);
        }
    }

    /// <summary>
    /// Local event raised during Render when a successful attack execution is detected in simulation.
    /// </summary>
    public event Action<AttackPerformedEvent> AttackPerformed;

    private void Awake()
    {
        CacheDependencies();
    }

    public override void Spawned()
    {
        // Fusion invokes Spawned even on disabled behaviours. Variants such as
        // Specter replace standard combat and intentionally have no IAttack.
        if (!enabled)
        {
            _dependenciesValid = false;
            return;
        }

        CacheDependencies();
        _entityRegistry = Runner != null ? Runner.GetComponent<EntityRegistry>() : null;
        _dependenciesValid = ValidateDependencies();

        _lastObservedSequence = AttackSequence;

        if (HasStateAuthority)
        {
            if (!HostMigrationRestoreUtility.IsRestoreSpawn(this))
            {
                IsAttackEnabled = false;
                ClearPreparationState();
            }
            else
            {
                AttackCooldown = TickTimer.None;
                ClearPreparationState();
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!_dependenciesValid || !HasStateAuthority)
        {
            return;
        }

        if (_movementController == null || _character == null)
        {
            return;
        }

        bool isAlive = _character.IsAlive;
        bool isStunned = _stun != null && _stun.IsActive;
        bool attackEnabled = IsAttackEnabled;
        bool attackRequested = _movementController.IsAttacking;
        bool isPreparing = IsPreparingAttack && _hasPendingDamage;

        // Reconcile stale replicated preparation flag if local payload is absent (e.g. after restore).
        if (IsPreparingAttack && !_hasPendingDamage)
        {
            ClearPreparationState();
            isPreparing = false;
        }

        EntityId targetId = default;
        Transform targetTransform = null;
        bool hasCurrentTarget = _movementController.TryGetCurrentTarget(out targetId, out targetTransform);
        if (!hasCurrentTarget && targetId.Value != 0)
        {
            _movementController.TryInvalidateCurrentTarget(targetId);
        }

        bool targetMatchesPending = !isPreparing || targetId == _pendingTargetId;
        bool targetEligible = hasCurrentTarget && targetMatchesPending && TryResolveEligibleTarget(targetId);
        if (hasCurrentTarget && !targetEligible && targetId.Value != 0)
        {
            _movementController.TryInvalidateCurrentTarget(targetId);
        }

        bool hasLOS = targetEligible && targetTransform != null &&
                      _movementController.HasLineOfSightTo(targetTransform.position);

        bool anticipationExpired = isPreparing && AnticipationTimer.ExpiredOrNotRunning(Runner);
        bool cooldownReady = AttackCooldown.ExpiredOrNotRunning(Runner);

        EnemyAnticipationStepOutcome outcome = EnemyAttackAnticipationRules.EvaluateStep(
            isAlive,
            isStunned,
            attackEnabled,
            attackRequested,
            isPreparing,
            targetEligible,
            hasLOS,
            anticipationExpired,
            cooldownReady);

        switch (outcome)
        {
            case EnemyAnticipationStepOutcome.StartPreparation:
                BeginPreparation(ResolveAimDirection(targetTransform), targetId);
                break;

            case EnemyAnticipationStepOutcome.Execute:
                ExecutePreparedAttack();
                break;

            case EnemyAnticipationStepOutcome.Cancel:
                CancelPreparation(applyCooldownIfConfigured: true);
                break;
        }
    }

    public override void Render()
    {
        if (!_dependenciesValid)
        {
            return;
        }

        if (AttackSequence != _lastObservedSequence)
        {
            AttackPerformedEvent performedEvent = new AttackPerformedEvent(
                _character.Id,
                (AttackType)LastAttackTypeValue,
                LastAttackOrigin,
                LastAttackDirection,
                LastAttackTick,
                scheduledWindupSeconds: _anticipationDurationSeconds
            );

            AttackPerformed?.Invoke(performedEvent);
            _lastObservedSequence = AttackSequence;
        }
    }

    private Vector2 ResolveAimDirection(Transform targetTransform)
    {
        Vector2 facingDirection = _movementController.FacingDirection;

        if (targetTransform == null || _activeAttack == null || _activeAttack.Type != AttackType.Ranged)
        {
            return facingDirection;
        }

        Vector2 originPosition = _attackOrigin != null
            ? (Vector2)_attackOrigin.position
            : (Vector2)transform.position;

        return PlayerAimMath.TryResolveDirection(originPosition, targetTransform.position, out Vector2 aimDirection)
            ? aimDirection
            : facingDirection;
    }

    private void BeginPreparation(Vector2 aimDirection, EntityId targetId)
    {
        if (aimDirection.sqrMagnitude < 0.0001f)
        {
            aimDirection = Vector2.down;
        }

        Vector2 normalizedDirection = aimDirection.normalized;
        Vector2 originPos = _attackOrigin != null ? (Vector2)_attackOrigin.position : (Vector2)transform.position;

        _pendingDamageRequest = new AttackRequest(
            _character.Id,
            originPos,
            normalizedDirection,
            (int)Runner.Tick
        );
        _pendingTargetId = targetId;
        _hasPendingDamage = true;

        IsPreparingAttack = true;
        PreparedAttackDirection = normalizedDirection;
        PreparedAttackTypeValue = (int)_activeAttack.Type;
        PreparationSequence++;
        AnticipationTimer = TickTimer.CreateFromSeconds(Runner, _anticipationDurationSeconds);
    }

    private void ExecutePreparedAttack()
    {
        if (!HasStateAuthority || !_hasPendingDamage)
        {
            ClearPreparationState();
            return;
        }

        // Refresh origin to current world pose while keeping the committed aim direction frozen.
        Vector2 currentOrigin = _attackOrigin != null ? (Vector2)_attackOrigin.position : (Vector2)transform.position;
        AttackRequest request = new AttackRequest(
            _pendingDamageRequest.AttackerId,
            currentOrigin,
            _pendingDamageRequest.Direction,
            (int)Runner.Tick
        );

        ClearPreparationState();

        float cooldownSeconds = _activeAttack.CooldownSeconds;
        AttackCooldown = cooldownSeconds > 0f
            ? TickTimer.CreateFromSeconds(Runner, cooldownSeconds)
            : TickTimer.None;

        CommitAttack(in request);
        _activeAttack.Execute(in request);
    }

    private void CancelPreparation(bool applyCooldownIfConfigured)
    {
        ClearPreparationState();

        if (applyCooldownIfConfigured && _consumeCooldownOnCancel && _activeAttack != null)
        {
            float cooldownSeconds = _activeAttack.CooldownSeconds;
            AttackCooldown = cooldownSeconds > 0f
                ? TickTimer.CreateFromSeconds(Runner, cooldownSeconds)
                : TickTimer.None;
        }
    }

    /// <summary>
    /// Legacy Animation Event hook retained as a no-op so existing AnimationClips do not log
    /// MissingReceiver warnings. Authoritative attack timing is driven exclusively by
    /// <see cref="AnticipationTimer"/> in <see cref="FixedUpdateNetwork"/> (TASK-41 / TASK-316).
    /// </summary>
    public void ExecutePendingDamage()
    {
        // Intentionally empty: animation never decides when authoritative damage or projectiles fire.
    }

    public bool TrySetAttackEnabled(bool enabled)
    {
        if (!HasStateAuthority)
        {
            return false;
        }

        IsAttackEnabled = enabled;
        if (!enabled)
        {
            ClearPreparationState();
        }
        return true;
    }

    public bool TrySetActiveAttack(MonoBehaviour attackSource)
    {
        if (!HasStateAuthority)
        {
            return false;
        }

        if (attackSource == null)
        {
            Debug.LogError($"{nameof(EnemyCombatAIController)}: Cannot set active attack to null.", this);
            return false;
        }

        if (attackSource is IAttack newAttack)
        {
            _activeAttackSource = attackSource;
            _activeAttack = newAttack;
            return true;
        }

        Debug.LogError($"{nameof(EnemyCombatAIController)}: Component {attackSource.name} does not implement {nameof(IAttack)}.", this);
        return false;
    }

    private void CacheDependencies()
    {
        if (_stun == null) _stun = GetComponent<EnemyStunNetworkEffect>();
        if (_characterSource != null)
        {
            _character = _characterSource as ICharacter;
        }

        if (_character == null)
        {
            _character = GetComponent<ICharacter>() ?? GetComponentInParent<ICharacter>();
        }

        if (_activeAttackSource != null)
        {
            _activeAttack = _activeAttackSource as IAttack;
        }

        if (_activeAttack == null)
        {
            _activeAttack = GetComponent<IAttack>() ?? GetComponentInChildren<IAttack>();
            if (_activeAttack is MonoBehaviour attackMb)
            {
                _activeAttackSource = attackMb;
            }
        }

        if (_attackOrigin == null)
        {
            _attackOrigin = transform;
        }

        if (_movementController == null)
        {
            _movementController = GetComponent<EnemyMovementAIController>();
        }
    }

    private bool ValidateDependencies()
    {
        if (_character == null)
        {
            Debug.LogError($"{nameof(EnemyCombatAIController)} requires a component implementing {nameof(ICharacter)}.", this);
            return false;
        }

        if (_attackOrigin == null)
        {
            Debug.LogError($"{nameof(EnemyCombatAIController)} requires an assigned {nameof(_attackOrigin)} Transform.", this);
            return false;
        }

        if (_activeAttack == null)
        {
            Debug.LogError($"{nameof(EnemyCombatAIController)} requires a component implementing {nameof(IAttack)}.", this);
            return false;
        }

        if (_movementController == null)
        {
            Debug.LogError($"{nameof(EnemyCombatAIController)} requires an assigned {nameof(EnemyMovementAIController)}.", this);
            return false;
        }

        if (_entityRegistry == null)
        {
            Debug.LogError($"{nameof(EnemyCombatAIController)} requires a runner-scoped {nameof(EntityRegistry)}.", this);
            return false;
        }

        return true;
    }

    private bool TryResolveEligibleTarget(EntityId targetId)
    {
        return targetId.Value != 0 &&
            _entityRegistry != null &&
            _entityRegistry.TryGetCharacter(targetId, out ICharacter character) &&
            character != null &&
            character is PlayerCharacter &&
            character.IsAlive &&
            _entityRegistry.TryGetDamageable(targetId, out IDamageable damageable) &&
            damageable != null &&
            damageable.CanReceiveDamage;
    }

    /// <summary>Immediately discard wind-up on authoritative Stun application.</summary>
    internal void CancelAttackForStun()
    {
        if (!HasStateAuthority || Runner == null || !Runner.IsSimulationUpdating || !Runner.IsForward) return;
        CancelPreparation(applyCooldownIfConfigured: false);
    }

    private void ClearPreparationState()
    {
        _hasPendingDamage = false;
        _pendingTargetId = default;
        if (Object != null && Object.IsValid && HasStateAuthority)
        {
            IsPreparingAttack = false;
            AnticipationTimer = TickTimer.None;
        }
    }

    private void CommitAttack(in AttackRequest request)
    {
        LastAttackOrigin = request.Origin;
        LastAttackDirection = request.Direction;
        LastAttackTypeValue = (int)_activeAttack.Type;
        LastAttackTick = request.SimulationTick;
        AttackSequence++;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_characterSource == null)
        {
            _characterSource = GetComponent<MonoBehaviour>() as ICharacter != null ? GetComponent<MonoBehaviour>() : null;
        }

        if (_movementController == null)
        {
            _movementController = GetComponent<EnemyMovementAIController>();
        }

        if (_activeAttackSource == null)
        {
            IAttack foundAttack = GetComponent<IAttack>() ?? GetComponentInChildren<IAttack>();
            if (foundAttack is MonoBehaviour attackMb)
            {
                _activeAttackSource = attackMb;
            }
        }

        CacheDependencies();
    }
#endif
}

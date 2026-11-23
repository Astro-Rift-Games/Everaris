using UnityEngine;

/// <summary>
/// Concrete animator view component for enemy entities, inheriting core animation logic from <see cref="CharacterAnimatorView"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyAnimatorView : CharacterAnimatorView
{
    private EnemyMovementAIController _enemyMovement;
    private EnemyCombatAIController _enemyCombat;
    private int _isAttackingHash;
    private bool _supportsIsAttacking;

    protected override void Awake()
    {
        base.Awake();
        _isAttackingHash = Animator.StringToHash("IsAttacking");
        _supportsIsAttacking = HasBoolParameter(_isAttackingHash);
        CacheEnemyDependencies();
    }

    private void CacheEnemyDependencies()
    {
        if (_enemyMovement == null)
        {
            _enemyMovement = GetComponentInParent<EnemyMovementAIController>();
        }

        if (_enemyCombat == null)
        {
            _enemyCombat = GetComponentInParent<EnemyCombatAIController>();
        }
    }

    protected override void Update()
    {
        base.Update();

        if (AnimatorInstance == null)
        {
            return;
        }

        if (_enemyMovement == null || _enemyCombat == null)
        {
            CacheEnemyDependencies();
        }

        if (_supportsIsAttacking)
        {
            bool isPreparing = _enemyCombat != null && _enemyCombat.Object != null && _enemyCombat.Object.IsValid && _enemyCombat.IsPreparingAttack;
            bool isAttacking = (_enemyMovement != null && _enemyMovement.Object != null && _enemyMovement.Object.IsValid && _enemyMovement.IsAttacking) || isPreparing;
            AnimatorInstance.SetBool(_isAttackingHash, isAttacking);
        }
    }

    private bool HasBoolParameter(int parameterHash)
    {
        if (AnimatorInstance == null)
        {
            return false;
        }

        AnimatorControllerParameter[] parameters = AnimatorInstance.parameters;
        for (int index = 0; index < parameters.Length; index++)
        {
            AnimatorControllerParameter parameter = parameters[index];
            if (parameter.nameHash == parameterHash && parameter.type == AnimatorControllerParameterType.Bool)
            {
                return true;
            }
        }

        return false;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        CacheEnemyDependencies();
    }

    protected override void Reset()
    {
        base.Reset();
        CacheEnemyDependencies();
    }
#endif
}

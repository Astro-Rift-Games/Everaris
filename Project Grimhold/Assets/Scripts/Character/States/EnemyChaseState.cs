using UnityEngine;

/// <summary>
/// Enemy state active during target pursuit.
/// Enables movement AI and monitors distance to target.
/// </summary>
public sealed class EnemyChaseState : IEnemyState
{
    public EnemyStateType Type => EnemyStateType.Chase;

    public void Enter(EnemyFSM fsm)
    {
        if (fsm.MovementController != null)
        {
            fsm.MovementController.TrySetControlEnabled(true);
        }

        if (fsm.CombatController != null)
        {
            fsm.CombatController.TrySetAttackEnabled(false);
        }
    }

    public void FixedUpdateNetwork(EnemyFSM fsm)
    {
        if (!fsm.Character.IsAlive)
        {
            fsm.TransitionTo(EnemyStateType.Dead);
            return;
        }

        if (fsm.MovementController.IsAttacking)
        {
            fsm.TransitionTo(EnemyStateType.Attack);
            return;
        }

        if (!fsm.MovementController.IsOnPursuit)
        {
            if (fsm.MovementController.IsReturningHome)
            {
                fsm.TransitionTo(EnemyStateType.ReturnHome);
            }
            else if (fsm.MovementController.HasPatrolRoute)
            {
                fsm.TransitionTo(EnemyStateType.Patrol);
            }
            else
            {
                fsm.TransitionTo(EnemyStateType.Idle);
            }
            return;
        }
    }

    public void Exit(EnemyFSM fsm)
    {
    }
}

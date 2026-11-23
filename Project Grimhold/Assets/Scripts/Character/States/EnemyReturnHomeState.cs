/// <summary>
/// Enemy state active while returning to its Home Area after exceeding Leash or losing its target.
/// Enables navigation control and disables combat until arrival or valid re-engagement.
/// </summary>
public sealed class EnemyReturnHomeState : IEnemyState
{
    public EnemyStateType Type => EnemyStateType.ReturnHome;

    public void Enter(EnemyFSM fsm)
    {
        if (fsm.MovementController != null)
        {
            fsm.MovementController.IsPatrolActive = false;
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

        if (fsm.MovementController.IsOnPursuit)
        {
            fsm.TransitionTo(EnemyStateType.Chase);
            return;
        }

        if (!fsm.MovementController.IsReturningHome)
        {
            if (fsm.MovementController.HasPatrolRoute)
            {
                fsm.TransitionTo(EnemyStateType.Patrol);
            }
            else
            {
                fsm.TransitionTo(EnemyStateType.Idle);
            }
        }
    }

    public void Exit(EnemyFSM fsm)
    {
    }
}

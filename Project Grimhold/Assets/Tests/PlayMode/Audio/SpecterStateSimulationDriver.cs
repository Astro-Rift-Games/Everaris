#if UNITY_INCLUDE_TESTS
using Fusion;
using ProjectGrimhold.Gameplay.Enemies.Specter;
using UnityEngine;

/// <summary>
/// Moves a Specter to a requested state from Fusion simulation (forward ticks only), the same authoritative path the
/// AI uses, so PlayMode audio tests observe replicated state exactly as presentation does.
/// </summary>
public sealed class SpecterStateSimulationDriver : SimulationBehaviour
{
    public SpecterAIController Target { get; set; }
    public Vector2 AttackTargetPosition { get; set; }
    public bool IsRequested { get; private set; }

    private SpecterStateType _requested;

    public void Request(SpecterStateType state)
    {
        _requested = state;
        IsRequested = true;
    }

    public override void FixedUpdateNetwork()
    {
        if (!IsRequested || Target == null || !Runner.IsForward) return;

        IsRequested = false;
        Target.AttackTargetPosition = AttackTargetPosition;
        Target.TransitionTo(_requested);
    }
}
#endif

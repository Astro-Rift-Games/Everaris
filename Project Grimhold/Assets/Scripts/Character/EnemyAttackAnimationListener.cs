using UnityEngine;

/// <summary>
/// Presentation-layer receiver retained for legacy <c>OnAttackHit</c> Animation Events on enemy
/// attack clips so Unity does not log MissingReceiver warnings.
///
/// Authoritative attack timing is owned exclusively by <see cref="EnemyCombatAIController"/> via
/// Fusion <c>TickTimer</c> (TASK-41 / TASK-316); animation events never trigger or gate damage.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyAttackAnimationListener : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField]
    private EnemyCombatAIController _combatController;

    private void Awake()
    {
        CacheDependencies();
    }

    /// <summary>
    /// No-op receiver for legacy AnimationClip events. Simulation timing is authoritative.
    /// </summary>
    public void OnAttackHit()
    {
        // Intentionally does not trigger authoritative damage (TASK-41 / TASK-316).
    }

    private void CacheDependencies()
    {
        if (_combatController == null)
        {
            _combatController = GetComponentInParent<EnemyCombatAIController>();
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CacheDependencies();
    }
#endif
}

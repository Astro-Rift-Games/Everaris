using UnityEngine;

/// <summary>
/// Deterministic step verdict for authoritative enemy attack anticipation (TASK-41 / TASK-316).
/// </summary>
public enum EnemyAnticipationStepOutcome
{
    None = 0,
    StartPreparation = 1,
    ContinuePreparation = 2,
    Execute = 3,
    Cancel = 4
}

/// <summary>
/// Pure simulation rules governing enemy attack anticipation timing, cancellation, and progress.
/// Guarantees authoritative tick-driven timing completely decoupled from Animator or Animation Events.
/// </summary>
public static class EnemyAttackAnticipationRules
{
    public static EnemyAnticipationStepOutcome EvaluateStep(
        bool isAlive,
        bool isStunned,
        bool isAttackEnabled,
        bool isAttackRequested,
        bool isPreparing,
        bool hasValidTarget,
        bool hasLineOfSight,
        bool anticipationExpired,
        bool cooldownReady)
    {
        if (!isAlive || isStunned || !isAttackEnabled)
        {
            return isPreparing ? EnemyAnticipationStepOutcome.Cancel : EnemyAnticipationStepOutcome.None;
        }

        if (isPreparing)
        {
            if (!hasValidTarget || !hasLineOfSight)
            {
                return EnemyAnticipationStepOutcome.Cancel;
            }

            return anticipationExpired
                ? EnemyAnticipationStepOutcome.Execute
                : EnemyAnticipationStepOutcome.ContinuePreparation;
        }

        if (isAttackRequested && cooldownReady && hasValidTarget && hasLineOfSight)
        {
            return EnemyAnticipationStepOutcome.StartPreparation;
        }

        return EnemyAnticipationStepOutcome.None;
    }

    public static float ComputeProgress(float remainingSeconds, float totalDurationSeconds)
    {
        if (totalDurationSeconds <= 0f)
        {
            return 1f;
        }

        return Mathf.Clamp01(1f - (remainingSeconds / totalDurationSeconds));
    }
}

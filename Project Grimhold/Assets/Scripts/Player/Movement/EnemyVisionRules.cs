using UnityEngine;

/// <summary>
/// Pure deterministic decision returned by <see cref="EnemyVisionRules.EvaluateTargetEngagement"/>.
/// </summary>
public readonly struct EnemyTargetEngagementDecision
{
    public bool DropTarget { get; }
    public bool IsOnPursuit { get; }
    public bool IsAttacking { get; }
    public int NextPursuitLostTickCount { get; }
    public bool ShouldClearAggro { get; }

    public EnemyTargetEngagementDecision(
        bool dropTarget,
        bool isOnPursuit,
        bool isAttacking,
        int nextPursuitLostTickCount,
        bool shouldClearAggro)
    {
        DropTarget = dropTarget;
        IsOnPursuit = isOnPursuit;
        IsAttacking = isAttacking;
        NextPursuitLostTickCount = nextPursuitLostTickCount;
        ShouldClearAggro = shouldClearAggro;
    }
}

/// <summary>
/// Pure line-of-sight and target engagement visibility rules shared by enemy AI controllers.
/// Centralizes the canonical solid-wall vision mask (<c>WorldCollision | Obstacles</c>) and
/// ensures neither melee nor ranged enemies detect, hold pursuit without grace/aggro, or attack
/// through solid walls.
/// </summary>
public static class EnemyVisionRules
{
    public const int WorldCollisionLayer = 6;
    public const int ObstaclesLayer = 11;
    public const int DefaultVisionObstacleMask = (1 << WorldCollisionLayer) | (1 << ObstaclesLayer);

    /// <summary>
    /// Returns true when <paramref name="obstacleLayer"/> includes the solid world collision layer.
    /// </summary>
    public static bool IncludesWorldCollision(LayerMask obstacleLayer)
    {
        return (obstacleLayer.value & (1 << WorldCollisionLayer)) != 0;
    }

    /// <summary>
    /// Returns true when the segment from <paramref name="from"/> to <paramref name="to"/> is not
    /// obstructed by a solid obstacle collider or an active smoke zone.
    /// </summary>
    public static bool HasLineOfSight(Vector2 from, Vector2 to, LayerMask obstacleLayer)
    {
        return !Physics2D.Linecast(from, to, obstacleLayer)
            && !SmokeZoneRegistry.IsLineOccluded(from, to);
    }

    /// <summary>
    /// Evaluates active target pursuit and attack flags given distance, LOS, aggro, stun and grace state.
    /// Attacking strictly requires line of sight regardless of distance or aggro state.
    /// </summary>
    public static EnemyTargetEngagementDecision EvaluateTargetEngagement(
        float distance,
        float disengageRange,
        float attackRangeThreshold,
        bool hasLineOfSight,
        bool aggroActive,
        bool isStunned,
        int currentPursuitLostTicks,
        int pursuitLostGraceTicks)
    {
        if (!aggroActive && distance > disengageRange)
        {
            return new EnemyTargetEngagementDecision(
                dropTarget: true,
                isOnPursuit: false,
                isAttacking: false,
                nextPursuitLostTickCount: 0,
                shouldClearAggro: false);
        }

        if (hasLineOfSight)
        {
            bool shouldClearAggro = distance <= disengageRange;
            bool inAttackRange = distance <= attackRangeThreshold;
            bool isAttacking = inAttackRange && !isStunned;

            return new EnemyTargetEngagementDecision(
                dropTarget: false,
                isOnPursuit: true,
                isAttacking: isAttacking,
                nextPursuitLostTickCount: 0,
                shouldClearAggro: shouldClearAggro);
        }

        if (aggroActive)
        {
            return new EnemyTargetEngagementDecision(
                dropTarget: false,
                isOnPursuit: true,
                isAttacking: false,
                nextPursuitLostTickCount: currentPursuitLostTicks,
                shouldClearAggro: false);
        }

        int nextLostTicks = currentPursuitLostTicks + 1;
        if (nextLostTicks >= pursuitLostGraceTicks)
        {
            return new EnemyTargetEngagementDecision(
                dropTarget: true,
                isOnPursuit: false,
                isAttacking: false,
                nextPursuitLostTickCount: 0,
                shouldClearAggro: false);
        }

        return new EnemyTargetEngagementDecision(
            dropTarget: false,
            isOnPursuit: true,
            isAttacking: false,
            nextPursuitLostTickCount: nextLostTicks,
            shouldClearAggro: false);
    }
}

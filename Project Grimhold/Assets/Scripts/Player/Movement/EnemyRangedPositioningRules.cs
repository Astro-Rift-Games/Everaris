using System;
using UnityEngine;

/// <summary>
/// Tactical movement intent resolved for a Ranged enemy (TASK-319).
/// </summary>
public enum EnemyRangedMovementIntent
{
    HoldAndAttack = 0,
    Approach = 1,
    Retreat = 2,
    RepositionForLos = 3
}

/// <summary>
/// Pure deterministic positioning rules for Ranged enemies (TASK-319):
/// - Keeps a preferred combat distance band with retreat hysteresis when a player closes in.
/// - Finds a walkable firing vantage point with clear LOS inside Leash when the target takes cover.
/// - Never authorizes firing without line of sight.
/// </summary>
public static class EnemyRangedPositioningRules
{
    private static readonly float[] RetreatFanAnglesDeg = { 0f, 30f, -30f, 60f, -60f, 90f, -90f };

    /// <summary>
    /// Evaluates whether the Ranged enemy should hold &amp; attack, approach, retreat, or reposition for LOS.
    /// </summary>
    public static EnemyRangedMovementIntent EvaluateIntent(
        float distanceToTarget,
        bool hasLineOfSight,
        float retreatDistance,
        float preferredMinDistance,
        float attackRange,
        bool wasRetreating)
    {
        if (!hasLineOfSight)
        {
            return EnemyRangedMovementIntent.RepositionForLos;
        }

        float safeRetreat = Mathf.Max(0.1f, retreatDistance);
        float safeMin = Mathf.Max(safeRetreat, preferredMinDistance);

        if (distanceToTarget < safeRetreat || (wasRetreating && distanceToTarget < safeMin))
        {
            return EnemyRangedMovementIntent.Retreat;
        }

        if (distanceToTarget > attackRange)
        {
            return EnemyRangedMovementIntent.Approach;
        }

        return EnemyRangedMovementIntent.HoldAndAttack;
    }

    /// <summary>
    /// Finds a walkable retreat destination away from <paramref name="targetPos"/> within
    /// <paramref name="leashRadius"/> without passing through walls. Prefers spots that keep LOS.
    /// Returns <see langword="false"/> when cornered against walls/leash so the enemy holds and fights.
    /// </summary>
    public static bool TryComputeRetreatDestination(
        Vector2 enemyPos,
        Vector2 targetPos,
        Vector2 homePos,
        float leashRadius,
        float stepDistance,
        Func<Vector2, bool> isPositionWalkable,
        Func<Vector2, Vector2, bool> hasLineOfSight,
        out Vector2 retreatDestination)
    {
        retreatDestination = enemyPos;
        Vector2 away = enemyPos - targetPos;
        Vector2 baseAwayDir = away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.down;
        float safeStep = Mathf.Max(0.5f, stepDistance);

        bool foundFallback = false;
        Vector2 fallbackPos = enemyPos;

        for (int i = 0; i < RetreatFanAnglesDeg.Length; i++)
        {
            Vector2 dir = Rotate(baseAwayDir, RetreatFanAnglesDeg[i]);
            Vector2 candidate = enemyPos + dir * safeStep;

            if (EnemyLeashRules.IsBeyondLeash(candidate, homePos, leashRadius))
            {
                continue;
            }

            if (isPositionWalkable != null && !isPositionWalkable(candidate))
            {
                continue;
            }

            if (hasLineOfSight != null && !hasLineOfSight(enemyPos, candidate))
            {
                continue;
            }

            // Prefer retreat candidates that maintain firing LOS on the target.
            if (hasLineOfSight == null || hasLineOfSight(candidate, targetPos))
            {
                retreatDestination = candidate;
                return true;
            }

            if (!foundFallback)
            {
                foundFallback = true;
                fallbackPos = candidate;
            }
        }

        if (foundFallback)
        {
            retreatDestination = fallbackPos;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Searches deterministically for the closest walkable position within <paramref name="searchRadius"/>
    /// and within <paramref name="leashRadius"/> of <paramref name="homePos"/> from which the Ranged
    /// enemy has unobstructed LOS to <paramref name="targetPos"/> within combat range.
    /// </summary>
    public static bool TryFindVantagePointWithLos(
        Vector2 enemyPos,
        Vector2 targetPos,
        Vector2 homePos,
        float leashRadius,
        float preferredMinDistance,
        float attackRange,
        float searchRadius,
        Func<Vector2, bool> isPositionWalkable,
        Func<Vector2, Vector2, bool> hasLineOfSight,
        out Vector2 vantagePoint)
    {
        vantagePoint = enemyPos;

        float safeSearch = Mathf.Max(1.0f, searchRadius);
        float minCombatDist = Mathf.Max(0.5f, preferredMinDistance * 0.8f);
        float maxCombatDist = Mathf.Max(minCombatDist + 0.5f, attackRange * 0.95f);

        bool found = false;
        float bestScore = float.MaxValue;

        // 1. Sample concentric rings around the enemy to find the shortest sidestep around cover (pillars/corners).
        const int angleSamples = 16;
        float angleStep = 360f / angleSamples;
        int ringCount = Mathf.Clamp(Mathf.CeilToInt(safeSearch), 1, 6);

        for (int ring = 1; ring <= ringCount; ring++)
        {
            float radius = (safeSearch * ring) / ringCount;
            for (int a = 0; a < angleSamples; a++)
            {
                float rad = a * angleStep * Mathf.Deg2Rad;
                Vector2 candidate = enemyPos + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;
                EvaluateVantageCandidate(
                    candidate, enemyPos, targetPos, homePos, leashRadius,
                    minCombatDist, maxCombatDist, isPositionWalkable, hasLineOfSight,
                    ref found, ref bestScore, ref vantagePoint);
            }

            // Early-out once an inner ring yields a clear vantage point (shortest sidestep).
            if (found)
            {
                return true;
            }
        }

        // 2. Also sample a ring around the target at preferred combat distance if enemy rings found none.
        float idealTargetRadius = Mathf.Clamp((preferredMinDistance + attackRange) * 0.5f, minCombatDist, maxCombatDist);
        for (int a = 0; a < angleSamples; a++)
        {
            float rad = a * angleStep * Mathf.Deg2Rad;
            Vector2 candidate = targetPos + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * idealTargetRadius;
            EvaluateVantageCandidate(
                candidate, enemyPos, targetPos, homePos, leashRadius,
                minCombatDist, maxCombatDist, isPositionWalkable, hasLineOfSight,
                ref found, ref bestScore, ref vantagePoint);
        }

        return found;
    }

    private static void EvaluateVantageCandidate(
        Vector2 candidate,
        Vector2 enemyPos,
        Vector2 targetPos,
        Vector2 homePos,
        float leashRadius,
        float minCombatDist,
        float maxCombatDist,
        Func<Vector2, bool> isPositionWalkable,
        Func<Vector2, Vector2, bool> hasLineOfSight,
        ref bool found,
        ref float bestScore,
        ref Vector2 bestPoint)
    {
        if (EnemyLeashRules.IsBeyondLeash(candidate, homePos, leashRadius))
        {
            return;
        }

        float distToTarget = Vector2.Distance(candidate, targetPos);
        if (distToTarget < minCombatDist || distToTarget > maxCombatDist)
        {
            return;
        }

        if (isPositionWalkable != null && !isPositionWalkable(candidate))
        {
            return;
        }

        if (hasLineOfSight != null && !hasLineOfSight(candidate, targetPos))
        {
            return;
        }

        float travelCost = Vector2.SqrMagnitude(candidate - enemyPos);
        if (travelCost < bestScore)
        {
            bestScore = travelCost;
            bestPoint = candidate;
            found = true;
        }
    }

    private static Vector2 Rotate(Vector2 vector, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(vector.x * cos - vector.y * sin, vector.x * sin + vector.y * cos);
    }
}

using UnityEngine;

/// <summary>
/// Pure deterministic locomotion smoothing rules for enemies (TASK-317):
/// configurable acceleration, braking/deceleration, and angular turn-rate limiting.
/// </summary>
public static class EnemyLocomotionRules
{
    private const float ValidDirectionSqrThreshold = 0.0001f;

    /// <summary>
    /// Advances <paramref name="currentVelocity"/> toward <paramref name="targetVelocity"/> using
    /// <paramref name="acceleration"/> when speeding up and <paramref name="deceleration"/> when
    /// slowing down or braking to zero.
    /// </summary>
    public static Vector2 StepVelocity(
        Vector2 currentVelocity,
        Vector2 targetVelocity,
        float acceleration,
        float deceleration,
        float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return currentVelocity;
        }

        bool isBraking = targetVelocity.sqrMagnitude <= ValidDirectionSqrThreshold
            || targetVelocity.sqrMagnitude < currentVelocity.sqrMagnitude;

        float rate = isBraking ? Mathf.Max(0.01f, deceleration) : Mathf.Max(0.01f, acceleration);
        return Vector2.MoveTowards(currentVelocity, targetVelocity, rate * deltaTime);
    }

    /// <summary>
    /// Smoothly rotates <paramref name="currentDirection"/> toward <paramref name="targetDirection"/>
    /// along the shortest arc without exceeding <paramref name="maxTurnDegreesPerSecond"/> * <paramref name="deltaTime"/>.
    /// </summary>
    public static Vector2 RotateDirectionTowards(
        Vector2 currentDirection,
        Vector2 targetDirection,
        float maxTurnDegreesPerSecond,
        float deltaTime)
    {
        if (targetDirection.sqrMagnitude < ValidDirectionSqrThreshold)
        {
            return currentDirection.sqrMagnitude >= ValidDirectionSqrThreshold
                ? currentDirection.normalized
                : Vector2.down;
        }

        Vector2 safeTarget = targetDirection.normalized;
        if (currentDirection.sqrMagnitude < ValidDirectionSqrThreshold || maxTurnDegreesPerSecond <= 0f || deltaTime <= 0f)
        {
            return safeTarget;
        }

        Vector2 safeCurrent = currentDirection.normalized;
        float signedAngle = Vector2.SignedAngle(safeCurrent, safeTarget);
        float maxStep = maxTurnDegreesPerSecond * deltaTime;

        if (Mathf.Abs(signedAngle) <= maxStep)
        {
            return safeTarget;
        }

        float stepRad = Mathf.Sign(signedAngle) * maxStep * Mathf.Deg2Rad;
        float cos = Mathf.Cos(stepRad);
        float sin = Mathf.Sin(stepRad);

        Vector2 rotated = new Vector2(
            safeCurrent.x * cos - safeCurrent.y * sin,
            safeCurrent.x * sin + safeCurrent.y * cos);

        return rotated.normalized;
    }
}

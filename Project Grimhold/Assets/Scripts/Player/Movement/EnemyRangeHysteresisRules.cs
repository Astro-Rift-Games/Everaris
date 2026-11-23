using UnityEngine;

/// <summary>
/// Pure range-hysteresis rules preventing rapid Chase &lt;-&gt; Attack flickering at the attack-range boundary (TASK-316).
/// </summary>
public static class EnemyRangeHysteresisRules
{
    /// <summary>
    /// Computes the active attack-range threshold for this tick:
    /// - Entering attack uses <paramref name="enterAttackRange"/>.
    /// - Staying in attack expands the exit threshold by <paramref name="exitRangeMargin"/>
    ///   (and further preserves stay while <paramref name="minStayActive"/> is true within disengage range).
    /// </summary>
    public static float ResolveEffectiveAttackRange(
        bool currentlyAttacking,
        float enterAttackRange,
        float exitRangeMargin,
        bool minStayActive,
        float disengageRange)
    {
        float safeEnter = Mathf.Max(0f, enterAttackRange);
        if (!currentlyAttacking)
        {
            return safeEnter;
        }

        float safeMargin = Mathf.Max(0f, exitRangeMargin);
        float exitThreshold = safeEnter + safeMargin;
        if (!minStayActive)
        {
            return exitThreshold;
        }

        // During the brief minimum-stay window, allow small boundary drift up to disengage range.
        float minStayThreshold = safeEnter + safeMargin * 1.5f;
        return disengageRange > 0f ? Mathf.Min(disengageRange, minStayThreshold) : minStayThreshold;
    }
}

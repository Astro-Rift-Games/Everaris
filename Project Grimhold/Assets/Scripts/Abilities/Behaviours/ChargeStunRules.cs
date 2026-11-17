/// <summary>
/// Pure decision rules for the Charge (Embestida) Stun. Stun only follows an impact that actually dealt damage:
/// a push-only hit (zero base damage with knockback), damage mitigated to zero, or any resolver rejection
/// (friendly fire, immune or unavailable target, invalid request) never stuns.
/// </summary>
internal static class ChargeStunRules
{
    /// <summary>True when the resolver committed a positive amount of damage on the target.</summary>
    public static bool IsEffectiveDamage(in DamageResult result) => result.IsApplied && result.AppliedDamage > 0f;

    /// <summary>
    /// True when a configured Stun must be applied to the target after <paramref name="impact"/>: the duration is
    /// enabled and finite, the impact dealt effective damage, and the target survived it.
    /// </summary>
    public static bool ShouldApplyStun(float stunSeconds, in DamageResult impact, bool targetIsAlive) =>
        stunSeconds > 0f && !float.IsInfinity(stunSeconds) && targetIsAlive && IsEffectiveDamage(impact);
}

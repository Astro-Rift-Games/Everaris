using UnityEngine;

/// <summary>
/// The damage-plus-knockback application shared by concrete ability behaviors. It owns no state: the pipeline
/// (damage resolver, knockback receiver) stays the single owner of the actual effect.
/// </summary>
internal static class AbilityImpact
{
    /// <summary>
    /// Damage and knockback both require resolver policy checks, including zero-damage pushes.
    /// </summary>
    public static bool UsesDamagePipeline(float damage) => damage > 0f;

    /// <summary>
    /// Damages and pushes one target along <paramref name="direction"/>. Returns false only when damage is
    /// required but the caster has no <see cref="IDamageResolver"/>; the error is logged on <paramref name="owner"/>.
    /// </summary>
    public static bool TryApply(IDamageResolver resolver, EntityId casterId, EntityId targetId, IDamageable target,
        float damage, DamageType damageType, float knockbackForce, Vector2 direction, Vector2 hitPoint,
        int tick, Object owner) =>
        TryApply(resolver, casterId, targetId, target, damage, damageType, knockbackForce, direction, hitPoint, tick,
            owner, out _);

    /// <summary>
    /// Same application as the overload above, also reporting the resolver's <see cref="DamageResult"/>.
    /// <paramref name="result"/> is <c>default</c> (nothing applied) when the resolver was not invoked: no damage
    /// and no knockback, or a missing resolver.
    /// </summary>
    public static bool TryApply(IDamageResolver resolver, EntityId casterId, EntityId targetId, IDamageable target,
        float damage, DamageType damageType, float knockbackForce, Vector2 direction, Vector2 hitPoint,
        int tick, Object owner, out DamageResult result)
    {
        result = default;
        if (UsesDamagePipeline(damage) || knockbackForce > 0f)
        {
            if (resolver == null)
            {
                Debug.LogError($"{owner.GetType().Name} requires an {nameof(IDamageResolver)} on the caster to apply damage.", owner);
                return false;
            }
            result = resolver.Resolve(new DamageRequest(casterId, targetId, damage, damageType, direction, hitPoint,
                tick, knockbackForce));
        }
        return true;
    }
}

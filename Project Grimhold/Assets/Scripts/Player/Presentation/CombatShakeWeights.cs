using UnityEngine;

/// <summary>Pure confirmed-impact weighting; never modifies gameplay or shared configuration.</summary>
public readonly struct CombatShakeWeights
{
    public float Intensity { get; }
    public float Duration { get; }
    public Vector2 Kick { get; }

    private CombatShakeWeights(float intensity, float duration, Vector2 kick)
    {
        Intensity = intensity;
        Duration = duration;
        Kick = kick;
    }

    public static CombatShakeWeights Resolve(CombatPresentationEvent feedback, WeaponDefinition weapon,
        CameraShakeConfig config, bool isCharacter)
    {
        if (config == null || !isCharacter || feedback.Kind != CombatFeedbackKind.ConfirmedImpact ||
            feedback.AppliedDamage <= 0f)
            return default;

        WeaponDealShake custom = weapon != null && feedback.HasWeaponTag ? weapon.Presentation.DealShake : default;
        float intensity = custom.Enabled ? custom.Intensity : config.DealDamageIntensity;
        float duration = custom.Enabled ? custom.Duration : config.DealDamageDuration;
        float multiplier = feedback.IsFatal ? config.KillMultiplier : 1f;
        Vector2 direction = feedback.HitDirection;
        if (float.IsNaN(direction.x) || float.IsNaN(direction.y) ||
            float.IsInfinity(direction.x) || float.IsInfinity(direction.y))
            direction = Vector2.zero;
        return new CombatShakeWeights(intensity * multiplier, duration,
            direction.normalized * config.DirectionalKickStrength * multiplier);
    }
}

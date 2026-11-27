using System;

/// <summary>Balance parameters of the encounter cycle. Doc 10 §9.2 leaves the values to Balance.</summary>
public readonly struct EncounterLifecycleSettings
{
    public EncounterLifecycleSettings(float cooldownSeconds, int maxReactivations)
    {
        if (float.IsNaN(cooldownSeconds) || cooldownSeconds < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cooldownSeconds), cooldownSeconds, "Cooldown must be zero or positive.");
        }

        if (maxReactivations < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxReactivations), maxReactivations, "MaxReactivations must be zero or positive.");
        }

        CooldownSeconds = cooldownSeconds;
        MaxReactivations = maxReactivations;
    }

    public float CooldownSeconds { get; }
    public int MaxReactivations { get; }
}

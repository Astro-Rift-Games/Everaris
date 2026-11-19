using System;

/// <summary>
/// Canonical audio keys used to look clips up in an <see cref="EntityAudioConfig"/>. Lookups are
/// case-insensitive, so always use these constants instead of string literals.
/// </summary>
public static class AudioKeys
{
    // Shared / locomotion / health
    public const string Step = "Step";
    public const string TakeDamage = "TakeDamage";
    public const string Death = "Death";

    // Weapons and enemy attacks
    public const string Swing = "Swing";
    public const string Shoot = "Shoot";
    public const string Cast = "Cast";
    public const string Charge = "Charge";
    public const string Reload = "Reload";
    public const string Attack = "Attack";
    public const string Hit = "Hit";
    public const string Block = "Block";

    // Specter state cues (optional, SpecterAudioConfig only)
    public const string Channel = "Channel";
    public const string Detonate = "Detonate";
    public const string Reappear = "Reappear";

    // Music tracks
    public const string Town = "Town";
    public const string Raid = "Raid";

    // Death, Charge, Cast and ranged Block have no content yet: optional and silent while absent.
    private static readonly AudioKeySpec PlayerSpec = new AudioKeySpec(
        required: new[] { TakeDamage, Step },
        requiredAnyOf: null,
        optional: new[] { Death });

    private static readonly AudioKeySpec EnemySpec = new AudioKeySpec(
        required: new[] { TakeDamage, Step },
        requiredAnyOf: new[] { Attack, Shoot },
        optional: new[] { Death });

    // Same requirements as any enemy; the state cues are optional (silent until clips are added to the asset).
    private static readonly AudioKeySpec SpecterSpec = new AudioKeySpec(
        required: new[] { TakeDamage, Step },
        requiredAnyOf: new[] { Attack, Shoot },
        optional: new[] { Death, Channel, Detonate, Reappear });

    private static readonly AudioKeySpec WeaponSpec = new AudioKeySpec(
        required: null,
        requiredAnyOf: new[] { Swing, Shoot, Cast },
        optional: new[] { Charge, Reload, Attack, Hit, Block });

    private static readonly AudioKeySpec MusicSpec = new AudioKeySpec(
        required: new[] { Town, Raid },
        requiredAnyOf: null,
        optional: null);

    /// <summary>Key declaration for a config type, or null when the type has no declaration.</summary>
    public static AudioKeySpec SpecFor(Type configType)
    {
        if (configType == null) return null;
        if (typeof(PlayerAudioConfig).IsAssignableFrom(configType)) return PlayerSpec;
        if (typeof(SpecterAudioConfig).IsAssignableFrom(configType)) return SpecterSpec;
        if (typeof(EnemyAudioConfig).IsAssignableFrom(configType)) return EnemySpec;
        if (typeof(WeaponAudioConfig).IsAssignableFrom(configType)) return WeaponSpec;
        if (typeof(MusicAudioConfig).IsAssignableFrom(configType)) return MusicSpec;
        return null;
    }
}

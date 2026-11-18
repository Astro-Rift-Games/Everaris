using System;
using UnityEngine;

/// <summary>
/// Player-local camera preferences stored in PlayerPrefs as overrides over <see cref="GameplayCameraConfig"/>.
/// An unset preference resolves to the config value, so designer edits keep applying live.
/// The orthographic size (zoom) is deliberately not a player preference: it stays designer-owned in the config.
/// Stored values are clamped to the config's designer-owned ranges on write and on read.
/// Overrides are cached in memory after the first read so per-frame callers never touch PlayerPrefs.
/// </summary>
public static class GameplayCameraPreferences
{
    public enum Setting
    {
        MaxOffsetDistance,
        SmoothTime,
        FollowSpeed,
    }

    private const string KeyPrefix = "Grimhold.Presentation.Camera.";

    private static readonly int SettingCount = Enum.GetValues(typeof(Setting)).Length;
    private static float?[] _overrides;

    /// <summary>Effective value: the player's override clamped to the config range, or the config value when unset.</summary>
    public static float Get(Setting setting, GameplayCameraConfig config)
    {
        float? stored = Overrides()[(int)setting];
        return stored.HasValue ? Clamp(setting, stored.Value, config) : DefaultOf(setting, config);
    }

    /// <summary>Stores the player's override, clamped to the config range. Non-finite values are ignored.</summary>
    public static void Set(Setting setting, float value, GameplayCameraConfig config)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return;
        }

        float clamped = Clamp(setting, value, config);
        Overrides()[(int)setting] = clamped;
        PlayerPrefs.SetFloat(KeyOf(setting), clamped);
        PlayerPrefs.Save();
    }

    /// <summary>Clears every override so all settings resolve to the config values again.</summary>
    public static void ResetAll()
    {
        float?[] overrides = Overrides();
        for (int i = 0; i < overrides.Length; i++)
        {
            overrides[i] = null;
            PlayerPrefs.DeleteKey(KeyOf((Setting)i));
        }

        PlayerPrefs.Save();
    }

    /// <summary>Designer-owned (min, max) range the player may pick for the setting.</summary>
    public static Vector2 RangeOf(Setting setting, GameplayCameraConfig config)
    {
        Vector2 range = setting switch
        {
            Setting.MaxOffsetDistance => config.MaxOffsetDistanceRange,
            Setting.SmoothTime => config.SmoothTimeRange,
            Setting.FollowSpeed => config.FollowSpeedRange,
            _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null),
        };

        return new Vector2(range.x, Mathf.Max(range.x, range.y));
    }

    /// <summary>Config value used while the player has no override.</summary>
    public static float DefaultOf(Setting setting, GameplayCameraConfig config)
    {
        return setting switch
        {
            Setting.MaxOffsetDistance => config.MaxOffsetDistance,
            Setting.SmoothTime => config.SmoothTime,
            Setting.FollowSpeed => config.FollowSpeed,
            _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null),
        };
    }

    internal static string KeyOf(Setting setting)
    {
        return KeyPrefix + setting;
    }

    internal static void InvalidateCacheForTests()
    {
        _overrides = null;
    }

    private static float Clamp(Setting setting, float value, GameplayCameraConfig config)
    {
        Vector2 range = RangeOf(setting, config);
        return Mathf.Clamp(value, range.x, range.y);
    }

    private static float?[] Overrides()
    {
        if (_overrides != null)
        {
            return _overrides;
        }

        float?[] loaded = new float?[SettingCount];
        for (int i = 0; i < loaded.Length; i++)
        {
            string key = KeyOf((Setting)i);
            if (PlayerPrefs.HasKey(key))
            {
                float value = PlayerPrefs.GetFloat(key);
                loaded[i] = float.IsNaN(value) || float.IsInfinity(value) ? null : value;
            }
        }

        _overrides = loaded;
        return _overrides;
    }
}

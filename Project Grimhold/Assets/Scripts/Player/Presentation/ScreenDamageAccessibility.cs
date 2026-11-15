using UnityEngine;

/// <summary>Local scalar preference with in-memory caching to prevent per-frame PlayerPrefs queries.</summary>
public static class ScreenDamageAccessibility
{
    private const string Key = "Grimhold.Presentation.ScreenDamageIntensity";
    private static float? _cachedIntensity;

    public static float Intensity
    {
        get
        {
            if (!_cachedIntensity.HasValue)
            {
                _cachedIntensity = Mathf.Clamp01(PlayerPrefs.GetFloat(Key, 1f));
            }
            return _cachedIntensity.Value;
        }
    }

    public static void SetIntensity(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return;
        float clamped = Mathf.Clamp01(value);
        _cachedIntensity = clamped;
        PlayerPrefs.SetFloat(Key, clamped);
        PlayerPrefs.Save();
    }

    internal static void InvalidateCacheForTests()
    {
        _cachedIntensity = null;
    }
}

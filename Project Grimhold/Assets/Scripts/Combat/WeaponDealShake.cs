using UnityEngine;

/// <summary>Optional presentation-only override. Disabled by default for existing weapons.</summary>
[System.Serializable]
public struct WeaponDealShake
{
    [SerializeField, Tooltip("Override shared deal-shake intensity and duration for confirmed weapon hits.")]
    private bool _enabled;
    [SerializeField, Min(0f)] private float _intensity;
    [SerializeField, Min(0f)] private float _duration;

    public bool Enabled => _enabled;
    public float Intensity => SafeNonnegative(_intensity);
    public float Duration => SafeNonnegative(_duration);

    private static float SafeNonnegative(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
}

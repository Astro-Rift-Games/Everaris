using UnityEngine;

/// <summary>Optional casting-tip particles. Static art only; never changes a weapon's release delay.</summary>
[CreateAssetMenu(fileName = "WindupChargeVfx", menuName = "Grimhold/Combat/Windup Charge VFX")]
public sealed class WindupChargeVfx : ScriptableObject
{
    [SerializeField] private ParticleSystem _prefab;
    [SerializeField, Min(0f)] private float _minimumScale = .25f;
    [SerializeField, Min(0f)] private float _maximumScale = 1f;
    [SerializeField, Min(1f), Tooltip("Scale intensity once the AimStance is fully drawn. 1 disables the boost.")]
    private float _fullDrawMultiplier = 1.5f;

    public ParticleSystem Prefab => _prefab;
    public float MinimumScale => _minimumScale;
    public float MaximumScale => _maximumScale;
    public float FullDrawMultiplier => _fullDrawMultiplier;

    public bool TryValidate(out string error)
    {
        if (_prefab == null || !Finite(_minimumScale) || !Finite(_maximumScale) ||
            !Finite(_fullDrawMultiplier) || _minimumScale < 0f || _maximumScale < _minimumScale || _fullDrawMultiplier < 1f)
        {
            error = "windup charge requires particles, finite ordered nonnegative scales and a full-draw multiplier of at least 1.";
            return false;
        }
        error = null;
        return true;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

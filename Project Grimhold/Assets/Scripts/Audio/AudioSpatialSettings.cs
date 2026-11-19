using UnityEngine;

/// <summary>
/// Central distance policy for positional SFX. 3D distance is measured to the planar listener
/// (<see cref="PlanarAudioListener"/>), so it only depends on gameplay X/Y.
/// </summary>
[System.Serializable]
public sealed class AudioSpatialSettings
{
    public const float DefaultMinDistance = 2f;
    public const float DefaultMaxDistance = 20f;
    private const float MinimumRange = 0.01f;

    [SerializeField, Tooltip("Attenuation curve between the minimum and maximum distance.")]
    private AudioRolloffMode _rolloffMode = AudioRolloffMode.Linear;

    [SerializeField, Min(MinimumRange), Tooltip("World units within which a 3D sound plays at full volume.")]
    private float _minDistance = DefaultMinDistance;

    [SerializeField, Min(MinimumRange), Tooltip("World units beyond which a 3D sound is inaudible (Linear rolloff).")]
    private float _maxDistance = DefaultMaxDistance;

    public AudioSpatialSettings() { }

    public AudioSpatialSettings(AudioRolloffMode rolloffMode, float minDistance, float maxDistance)
    {
        _rolloffMode = rolloffMode;
        _minDistance = minDistance;
        _maxDistance = maxDistance;
    }

    public AudioRolloffMode RolloffMode => _rolloffMode;
    public float MinDistance => Mathf.Max(MinimumRange, _minDistance);
    public float MaxDistance => Mathf.Max(MinDistance + MinimumRange, _maxDistance);

    /// <summary>Writes the rolloff mode and distance range to <paramref name="source"/>; a null source is ignored.</summary>
    public void Apply(AudioSource source)
    {
        if (source == null) return;

        source.rolloffMode = _rolloffMode;
        // Unity clamps one bound against the other, so write max, min, then max again.
        source.maxDistance = MaxDistance;
        source.minDistance = MinDistance;
        source.maxDistance = MaxDistance;
    }
}

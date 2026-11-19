using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Gestor central de audio. Persiste entre escenas y maneja un pool de AudioSources para SFX,
/// además de un AudioSource dedicado para música.
/// </summary>
public sealed class AudioManager : MonoBehaviour, IAudioVolumeControl
{
    public static AudioManager Instance { get; private set; }

    private AudioVolumeSettingsStore _volumeSettings;
    private bool _hasStarted;

    public float MasterVolume => VolumeSettings.MasterVolume;
    public float MusicVolume => VolumeSettings.MusicVolume;
    public float SfxVolume => VolumeSettings.SfxVolume;

    private AudioVolumeSettingsStore VolumeSettings =>
        _volumeSettings ?? (_volumeSettings = new AudioVolumeSettingsStore());

    [Header("Mixer Configuration")]
    [SerializeField] private AudioMixer _mixer;
    [SerializeField] private AudioMixerGroup _sfxGroup;
    [SerializeField] private AudioMixerGroup _musicGroup;

    [Header("Pool Configuration")]
    [SerializeField, Min(1)] private int _sfxPoolSize = 16;

    [Header("Spatial Configuration")]
    [Tooltip("Rolloff and distance range of every 3D SFX source (pooled, created or owner-controlled).")]
    [SerializeField] private AudioSpatialSettings _spatialSettings = new AudioSpatialSettings();
    
    private AudioSource[] _sfxSources;
    private bool[] _sfxPlaying;       // Scratch buffer: avoids allocating on every selection.
    private long[] _sfxStartOrder;    // Monotonic start stamp per pooled source.
    private long _sfxStartCounter;

    private AudioSource _musicSource;

    private void Awake()
    {
        // Singleton pattern con persistencia
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        transform.SetParent(null); // Debe estar en el root para DontDestroyOnLoad
        DontDestroyOnLoad(gameObject);

        InitializeSfxPool();
        InitializeMusicSource();
    }

    private void Start()
    {
        if (Instance != this) return;

        _hasStarted = true;
        SetVolumeParam("MasterVolume", MasterVolume);
        SetVolumeParam("MusicVolume", MusicVolume);
        SetVolumeParam("SFXVolume", SfxVolume);
    }

    private void OnDestroy()
    {
        // Domain reload may be disabled, so a destroyed manager must not stay published.
        if (Instance == this) Instance = null;
    }

    private void InitializeSfxPool()
    {
        _sfxSources = new AudioSource[_sfxPoolSize];
        _sfxPlaying = new bool[_sfxPoolSize];
        _sfxStartOrder = new long[_sfxPoolSize];
        GameObject sfxParent = new GameObject("SFX_Pool");
        sfxParent.transform.SetParent(transform);

        for (int i = 0; i < _sfxPoolSize; i++)
        {
            _sfxSources[i] = CreateSfxSource(sfxParent.transform, $"SFX_Source_{i}");
        }
    }

    /// <summary>Central rolloff/min/max distance applied to every SFX source.</summary>
    public AudioSpatialSettings SpatialSettings => _spatialSettings ?? (_spatialSettings = new AudioSpatialSettings());

    /// <summary>Applies the central spatial settings to an owner-controlled SFX source.</summary>
    public void ApplySpatialSettings(AudioSource source)
    {
        SpatialSettings.Apply(source);
    }

    /// <summary>Creates an owner-controlled SFX source with the same routing as pooled one-shots.</summary>
    public AudioSource CreateSfxSource(Transform owner, string sourceName)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(owner, false);
        AudioSource source = go.AddComponent<AudioSource>();
        source.outputAudioMixerGroup = _sfxGroup;
        source.playOnAwake = false;
        ApplySpatialSettings(source);
        return source;
    }

    private void InitializeMusicSource()
    {
        GameObject go = new GameObject("Music_Source");
        go.transform.SetParent(transform);
        
        _musicSource = go.AddComponent<AudioSource>();
        _musicSource.outputAudioMixerGroup = _musicGroup;
        _musicSource.playOnAwake = false;
        _musicSource.loop = true;
    }

    /// <summary>
    /// Reproduce un SFX utilizando un AudioSource del pool.
    /// Soporta posicionamiento espacial (3D) si el config lo indica; <paramref name="spatial"/> lo fuerza a 2D o 3D.
    /// </summary>
    public void PlaySfx(in CustomClip config, Vector3 worldPosition = default, SfxSpatialMode spatial = SfxSpatialMode.Default)
    {
        if (!config.IsValid) return;

        AudioSource source = GetNextSfxSource();
        
        source.transform.position = worldPosition;
        ApplyConfigToSource(config, source);
        ApplySpatialMode(source, spatial);

        source.Play();
    }

    /// <summary>
    /// Reproduce un clip SFX individual utilizando un AudioSource del pool.
    /// Sin <paramref name="spatial"/> el clip es 2D; usar <see cref="SfxSpatialMode.World3D"/> para eventos del mundo.
    /// </summary>
    public void PlaySfx(AudioClip clip, Vector3 worldPosition = default, float volume = 1f, float pitch = 1f,
        SfxSpatialMode spatial = SfxSpatialMode.Default)
    {
        if (clip == null) return;

        AudioSource source = GetNextSfxSource();
        source.transform.position = worldPosition;
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume);
        source.pitch = pitch;
        source.spatialBlend = spatial == SfxSpatialMode.World3D ? 1f : 0f;
        source.loop = false;
        source.Play();
    }

    /// <summary>
    /// Reproduce un SFX inyectando la configuración en un AudioSource externo.
    /// Útil para objetos que controlan su propio AudioSource (ej: un motor loopeando).
    /// </summary>
    public void PlaySfxAttached(in CustomClip config, AudioSource source)
    {
        if (!config.IsValid || source == null) return;

        ApplyConfigToSource(config, source);
        source.Play();
    }

    private AudioSource GetNextSfxSource()
    {
        // Una voz libre si existe; con el pool lleno, roba la que empezó a sonar antes.
        for (int i = 0; i < _sfxPoolSize; i++)
            _sfxPlaying[i] = _sfxSources[i].isPlaying;

        int index = SfxVoiceSelector.Select(_sfxPlaying, _sfxStartOrder);
        _sfxStartOrder[index] = ++_sfxStartCounter;
        return _sfxSources[index];
    }

    private static void ApplySpatialMode(AudioSource source, SfxSpatialMode spatial)
    {
        if (spatial == SfxSpatialMode.Flat2D) source.spatialBlend = 0f;
        else if (spatial == SfxSpatialMode.World3D) source.spatialBlend = 1f;
    }

    private void ApplyConfigToSource(in CustomClip config, AudioSource source)
    {
        source.clip = config.GetRandomClip();
        source.volume = config.Volume;
        source.pitch = config.Pitch;
        source.spatialBlend = config.SpatialBlend;
        source.loop = config.Loop;
    }

    /// <summary>
    /// Reproduce música de fondo en el canal de música.
    /// (El crossfade se puede implementar aquí a futuro).
    /// </summary>
    public void PlayMusic(in CustomClip config, float crossfadeDuration = 0f)
    {
        if (!config.IsValid) return;

        ApplyConfigToSource(config, _musicSource);
        
        if (!_musicSource.isPlaying)
        {
            _musicSource.Play();
        }
    }

    /// <summary>Track assigned to the music channel; null when none was requested or it was stopped.</summary>
    public AudioClip CurrentMusicClip => _musicSource != null ? _musicSource.clip : null;

    public void StopMusic(float fadeDuration = 0f)
    {
        _musicSource.Stop();
        _musicSource.clip = null;
    }

    // =========================================================================
    // Control de Volumen Global
    // Mapea valores normalizados (0-1) a decibeles (-80dB a 0dB aprox)
    // =========================================================================

    public void SetMasterVolume(float normalizedVolume)
    {
        VolumeSettings.SetMasterVolume(normalizedVolume);
        SetVolumeParam("MasterVolume", MasterVolume);
    }

    public void SetSfxVolume(float normalizedVolume)
    {
        VolumeSettings.SetSfxVolume(normalizedVolume);
        SetVolumeParam("SFXVolume", SfxVolume);
    }

    public void SetMusicVolume(float normalizedVolume)
    {
        VolumeSettings.SetMusicVolume(normalizedVolume);
        SetVolumeParam("MusicVolume", MusicVolume);
    }

    private void SetVolumeParam(string paramName, float normalizedVolume)
    {
        if (!_hasStarted || _mixer == null) return;

        // Evitar log10 de 0
        float clampedVol = Mathf.Clamp(normalizedVolume, 0.0001f, 1f);
        float db = Mathf.Log10(clampedVol) * 20f;
        _mixer.SetFloat(paramName, db);
    }
}

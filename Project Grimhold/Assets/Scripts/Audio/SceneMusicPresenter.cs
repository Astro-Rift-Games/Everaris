using UnityEngine;

/// <summary>
/// Componente de escena que solicita al AudioManager la reproducción en loop de una pista
/// musical específica al cargar o iniciar la escena (ej: "Town" en Lobby-Town o "Raid" en Gameplay).
/// </summary>
[DisallowMultipleComponent]
public class SceneMusicPresenter : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField]
    private MusicAudioConfig _musicConfig;

    [SerializeField]
    [Tooltip("Clave de la pista musical en el MusicAudioConfig (ej: 'Town', 'Raid', ver AudioKeys).")]
    private string _trackKey = AudioKeys.Town;

    [SerializeField, Min(0f)]
    [Tooltip("Duración de transición o crossfade si AudioManager lo soporta.")]
    private float _crossfadeDuration = 0.5f;

    private AudioClip _startedTrack;

    private void Start()
    {
        PlaySceneMusic();
    }

    // The AudioManager outlives scenes: stop the track this scene started when the scene unloads, so it does not
    // carry over the loading screen into a scene without music. Music started later by someone else is kept.
    private void OnDestroy()
    {
        AudioManager manager = AudioManager.Instance;
        if (_startedTrack == null || manager == null) return;

        if (manager.CurrentMusicClip == _startedTrack)
        {
            manager.StopMusic();
        }
    }

    public void PlaySceneMusic()
    {
        if (AudioManager.Instance == null || _musicConfig == null)
        {
            return;
        }

        if (_musicConfig.TryGetClip(_trackKey, out CustomClip clip))
        {
            AudioManager.Instance.PlayMusic(clip, _crossfadeDuration);
            _startedTrack = AudioManager.Instance.CurrentMusicClip;
        }
        else
        {
            Debug.LogWarning($"[{nameof(SceneMusicPresenter)}] No se encontró el track con clave '{_trackKey}' en el MusicAudioConfig asignado.", this);
        }
    }
}

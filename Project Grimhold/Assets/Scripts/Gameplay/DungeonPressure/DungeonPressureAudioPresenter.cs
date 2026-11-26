using UnityEngine;
using Fusion;

/// <summary>
/// Detects changes in the DungeonPressurePhase and plays the corresponding SFX and music transitions via the AudioManager.
/// Uses Render() to ensure all clients (not just the host) hear the audio cues when the networked phase changes.
/// </summary>
public sealed class DungeonPressureAudioPresenter : NetworkBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private DungeonPressureController _pressureController;
    [SerializeField] private CollapseHazardDirector _hazardDirector;

    [Header("Audio Clips")]
    [Tooltip("Sound played when entering the Reinforcements phase.")]
    [SerializeField] private CustomClip _reinforcementsSfx;
    
    [Tooltip("Sound played when entering the Critical Pressure phase.")]
    [SerializeField] private CustomClip _criticalPressureSfx;
    
    [Tooltip("Sound played when the dungeon Collapses.")]
    [SerializeField] private CustomClip _collapseSfx;

    [Tooltip("Global 2D warning SFX played before a Collapse rupture wave activates rifts (TASK-336).")]
    [SerializeField] private CustomClip _ruptureWaveWarningSfx;

    [Header("Music Transition")]
    [Tooltip("Optional music track played during Collapse phase with crossfade.")]
    [SerializeField] private CustomClip _collapseMusic;

    [Tooltip("Crossfade duration in seconds when transitioning to Collapse music.")]
    [SerializeField] private float _collapseMusicCrossfade = 2.5f;

    private DungeonPressurePhase _lastPhase = DungeonPressurePhase.Normal;
    private int _lastWaveSequence = -1;

    public CustomClip CollapseMusic => _collapseMusic;
    public float CollapseMusicCrossfade => _collapseMusicCrossfade;

    public override void Spawned()
    {
        if (_pressureController == null)
        {
            _pressureController = GetComponent<DungeonPressureController>();
        }
        if (_hazardDirector == null)
        {
            _hazardDirector = GetComponent<CollapseHazardDirector>();
        }

        if (_hazardDirector != null && _hazardDirector.TryGetWaveWarningState(out int initialWaveSeq, out _, out _))
        {
            _lastWaveSequence = initialWaveSeq;
        }
        
        if (_pressureController != null && _pressureController.TryGetPhase(out var initialPhase))
        {
            _lastPhase = initialPhase;
            if (_lastPhase == DungeonPressurePhase.Collapse && _collapseMusic.IsValid && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayMusic(_collapseMusic, _collapseMusicCrossfade);
            }
        }
    }

    public override void Render()
    {
        if (_pressureController == null || !_pressureController.TryGetPhase(out var newPhase)) return;

        // Detect phase changes on the client side for audio/visuals
        if (newPhase != _lastPhase)
        {
            // Only play sound if the pressure is escalating (ignore resets or backward rollbacks)
            if (newPhase > _lastPhase)
            {
                PlayPhaseChangeSfx(newPhase);
            }
            
            _lastPhase = newPhase;
        }

        EvaluateGlobalWaveWarningAudio();
    }

    private void EvaluateGlobalWaveWarningAudio()
    {
        if (_hazardDirector == null || !_hazardDirector.TryGetWaveWarningState(out int waveSeq, out bool active, out int warningEndTick))
        {
            return;
        }

        if (_lastWaveSequence < 0)
        {
            _lastWaveSequence = waveSeq;
            return;
        }

        if (waveSeq <= _lastWaveSequence)
        {
            return;
        }

        _lastWaveSequence = waveSeq;
        int currentTick = Runner != null ? Runner.Tick.Raw : 0;
        if (!active || (warningEndTick > 0 && currentTick >= warningEndTick) || AudioManager.Instance == null)
        {
            return;
        }

        CustomClip clip = _ruptureWaveWarningSfx.IsValid ? _ruptureWaveWarningSfx : _criticalPressureSfx;
        if (clip.IsValid)
        {
            Vector3 playPos = Camera.main != null ? Camera.main.transform.position : transform.position;
            AudioManager.Instance.PlaySfx(clip, playPos, SfxSpatialMode.Flat2D);
        }
    }

    private void PlayPhaseChangeSfx(DungeonPressurePhase phase)
    {
        if (AudioManager.Instance == null) return;

        CustomClip clipToPlay = default;

        switch (phase)
        {
            case DungeonPressurePhase.Reinforcements:
                clipToPlay = _reinforcementsSfx;
                break;
            case DungeonPressurePhase.CriticalPressure:
                clipToPlay = _criticalPressureSfx;
                break;
            case DungeonPressurePhase.Collapse:
                clipToPlay = _collapseSfx;
                if (_collapseMusic.IsValid)
                {
                    AudioManager.Instance.PlayMusic(_collapseMusic, _collapseMusicCrossfade);
                }
                break;
        }

        if (clipToPlay.IsValid)
        {
            // Global warning: always 2D so the local player hears it clearly wherever the camera is.
            Vector3 playPos = Camera.main != null ? Camera.main.transform.position : transform.position;
            AudioManager.Instance.PlaySfx(clipToPlay, playPos, SfxSpatialMode.Flat2D);
        }
    }
}

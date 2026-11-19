using ProjectGrimhold.Gameplay.Enemies.Specter;
using UnityEngine;

/// <summary>
/// Audio de los estados del Espectro (canalización, detonación, reaparición). Es presentación pura: observa el estado
/// replicado de <see cref="SpecterAIController"/> en cada frame (igual que <see cref="SpecterVisualPresenter"/> y que
/// <see cref="EnemyAudioPresenter"/> con la salud), así que suena en todos los peers, una vez por transición, y nunca
/// durante resimulación. La primera muestra válida solo fija la línea base (spawn, late join, host migration).
/// Las claves <see cref="AudioKeys.Channel"/>, <see cref="AudioKeys.Detonate"/> y <see cref="AudioKeys.Reappear"/> son
/// opcionales: sin entrada en el <see cref="SpecterAudioConfig"/> el cue queda en silencio.
/// Los cues son one-shots 3D por el pool normal de SFX; Channel y Detonate suenan sobre el círculo de telegraph
/// (<see cref="SpecterAIController.AttackTargetPosition"/>), Reappear sobre el Espectro.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpecterAIController))]
public sealed class SpecterAudioPresenter : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField]
    private SpecterAIController _controller;

    [Header("Configuration")]
    [SerializeField]
    private SpecterAudioConfig _audioConfig;

    private readonly SpecterAudioCueObserver _observer = new();

    private void Awake()
    {
        CacheDependencies();
    }

    private void OnEnable()
    {
        CacheDependencies();
        _observer.Reset();
        Observe();
    }

    private void OnDisable()
    {
        _observer.Reset();
    }

    private void LateUpdate()
    {
        if (_controller == null) return;

        // La observación continúa aunque no exista AudioManager o clave, para no reproducir cues "atrasados".
        SpecterAudioCue cue = Observe();
        if (cue != SpecterAudioCue.None)
        {
            Play(cue);
        }
    }

    private void CacheDependencies()
    {
        if (_controller == null)
        {
            _controller = GetComponent<SpecterAIController>();
        }
    }

    private SpecterAudioCue Observe()
    {
        // Leer una propiedad [Networked] de un objeto no spawneado/inválido lanza; se valida antes.
        bool isReadable = _controller != null && _controller.Object != null && _controller.Object.IsValid;
        return _observer.Observe(isReadable, isReadable ? _controller.CurrentState : default);
    }

    private void Play(SpecterAudioCue cue)
    {
        if (_audioConfig == null || AudioManager.Instance == null) return;

        string key;
        Vector3 position;
        switch (cue)
        {
            case SpecterAudioCue.Channel:
                key = AudioKeys.Channel;
                position = TelegraphPosition();
                break;
            case SpecterAudioCue.Detonate:
                key = AudioKeys.Detonate;
                position = TelegraphPosition();
                break;
            case SpecterAudioCue.Reappear:
                key = AudioKeys.Reappear;
                position = transform.position;
                break;
            default:
                return;
        }

        if (_audioConfig.TryGetClip(key, out CustomClip clip))
        {
            AudioManager.Instance.PlaySfx(clip, position, SfxSpatialMode.World3D);
        }
    }

    private Vector3 TelegraphPosition()
    {
        Vector2 center = _controller.AttackTargetPosition;
        return new Vector3(center.x, center.y, 0f);
    }
}

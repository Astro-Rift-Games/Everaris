using UnityEngine;

/// <summary>
/// Observa el estado del personaje jugador (PlayerCharacter) para reproducir efectos de sonido
/// de daño (TakeDamage), muerte (Death) y locomoción (Step) sin acoplarse a la simulación ni a la red.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAudioPresenter : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField]
    private CharacterBase _characterBase;

    [SerializeField]
    private MonoBehaviour _movementStateSource;

    [Header("Configuration")]
    [SerializeField]
    private PlayerAudioConfig _audioConfig;

    [Header("Movement Configuration")]
    [SerializeField]
    [Tooltip("Habilita la reproducción de pasos basada en temporizador en LateUpdate. Desmarcar si se usan AnimationEvents para evitar sonidos duplicados.")]
    private bool _enableTimerFootsteps = false;

    [SerializeField, Min(0.1f)]
    [Tooltip("Intervalo en segundos entre cada sonido de paso mientras el jugador se desplaza (solo si _enableTimerFootsteps está activo).")]
    private float _stepInterval = 0.35f;

    private IMovementState _movementState;
    private readonly HealthAudioObserver _healthObserver = new();
    private float _stepTimer;

    private void Awake()
    {
        CacheDependencies();
    }

    private void OnEnable()
    {
        CacheDependencies();
        InitializeHealthTracking();
        _stepTimer = 0f;
    }

    private void OnDisable()
    {
        _healthObserver.Reset();
    }

    /// <summary>
    /// Reproduce cualquier sonido configurado en el PlayerAudioConfig mediante su clave (ej: AudioKeys.Step, AudioKeys.TakeDamage).
    /// Útil para llamadas directas desde listeners de eventos de animación (AnimationEvents).
    /// </summary>
    public void PlayAudio(string soundKey)
    {
        if (AudioManager.Instance == null || _audioConfig == null || string.IsNullOrEmpty(soundKey))
        {
            return;
        }

        if (_audioConfig.TryGetClip(soundKey, out CustomClip clip))
        {
            AudioManager.Instance.PlaySfx(clip, transform.position);
        }
    }

    private void CacheDependencies()
    {
        if (_characterBase == null)
        {
            _characterBase = GetComponentInParent<CharacterBase>();
        }

        if (_movementStateSource != null)
        {
            _movementState = _movementStateSource as IMovementState;
        }

        if (_movementState == null)
        {
            _movementState = GetComponentInParent<IMovementState>();
        }
    }

    private void LateUpdate()
    {
        if (_characterBase == null || _audioConfig == null) return;

        // La observación continúa aunque no exista AudioManager (PlayAudio lo omite) para no reproducir daño "atrasado".
        HealthAudioCue cue = ObserveHealth();
        if (cue != HealthAudioCue.None)
        {
            PlayAudio(cue == HealthAudioCue.Death ? AudioKeys.Death : AudioKeys.TakeDamage);
        }

        if (!IsHealthReadable() || _healthObserver.IsDead) return;

        // Verificación de locomoción por temporizador (opcional si se usan AnimationEvents)
        string stepKey = PollTimerFootstep(_movementState != null && _movementState.IsMoving, Time.deltaTime);
        if (stepKey != null)
        {
            PlayAudio(stepKey);
        }
    }

    /// <summary>
    /// Avanza el temporizador de pasos opcional (<c>_enableTimerFootsteps</c>) y devuelve la clave a reproducir,
    /// o null si no toca sonar. Se reinicia cuando el jugador no se desplaza.
    /// </summary>
    public string PollTimerFootstep(bool isMoving, float deltaTime)
    {
        if (!_enableTimerFootsteps || !isMoving)
        {
            _stepTimer = 0f;
            return null;
        }

        _stepTimer -= deltaTime;
        if (_stepTimer > 0f) return null;

        _stepTimer = _stepInterval;
        return AudioKeys.Step;
    }

    private void InitializeHealthTracking()
    {
        _healthObserver.Reset();
        ObserveHealth();
    }

    private bool IsHealthReadable()
    {
        // CharacterBase.IsAlive devuelve true con un Object inválido, por eso no se lee salud/vida sin validar antes.
        return _characterBase != null && _characterBase.Object != null && _characterBase.Object.IsValid;
    }

    private HealthAudioCue ObserveHealth()
    {
        bool isValid = IsHealthReadable();
        return isValid
            ? _healthObserver.Observe(true, _characterBase.IsAlive, _characterBase.Health)
            : _healthObserver.Observe(false, false, 0f);
    }
}

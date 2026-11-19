using UnityEngine;

/// <summary>
/// Observa el estado de salud, combate y locomoción de un enemigo para reproducir audios de
/// ataque (Attack/Shoot), daño (TakeDamage), muerte (Death) y pasos (Movement),
/// sin interferir en la simulación.
/// </summary>
[DisallowMultipleComponent]
public class EnemyAudioPresenter : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField]
    private CharacterBase _characterBase;

    [SerializeField]
    private MonoBehaviour _combatControllerSource;

    [Header("Configuration")]
    [SerializeField]
    private EnemyAudioConfig _audioConfig;

    [Header("Movement Configuration")]
    [SerializeField]
    [Tooltip("Habilita la reproducción de pasos basada en temporizador en LateUpdate. Desmarcar si se usan AnimationEvents para evitar sonidos duplicados.")]
    private bool _enableTimerFootsteps = false;

    [SerializeField, Min(0.1f)]
    [Tooltip("Intervalo en segundos entre cada sonido de paso mientras el enemigo se desplaza (solo si _enableTimerFootsteps está activo).")]
    private float _stepInterval = 0.35f;

    private ICombatController _combatController;
    private IMovementState _movementState;
    private readonly HealthAudioObserver _healthObserver = new();
    private bool _isSubscribed;
    private float _stepTimer;

    private void Awake()
    {
        CacheDependencies();
    }

    private void OnEnable()
    {
        CacheDependencies();
        Subscribe();
        InitializeHealthTracking();
        _stepTimer = 0f;
    }

    private void OnDisable()
    {
        Unsubscribe();
        _healthObserver.Reset();
    }

    /// <summary>
    /// Reproduce cualquier sonido configurado en el EnemyAudioConfig mediante su clave (ej: AudioKeys.Step, AudioKeys.Attack, AudioKeys.TakeDamage).
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

        if (_combatControllerSource != null)
        {
            _combatController = _combatControllerSource as ICombatController;
        }

        if (_combatController == null)
        {
            _combatController = GetComponentInParent<ICombatController>();
        }

        if (_movementState == null)
        {
            _movementState = GetComponentInParent<IMovementState>();
        }
    }

    private void Subscribe()
    {
        if (_isSubscribed) return;

        if (_combatController != null)
        {
            _combatController.AttackPerformed += OnAttackPerformed;
            _isSubscribed = true;
        }
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed) return;

        if (_combatController != null)
        {
            _combatController.AttackPerformed -= OnAttackPerformed;
            _isSubscribed = false;
        }
    }

    private void OnAttackPerformed(AttackPerformedEvent attackEvent)
    {
        if (_audioConfig == null || AudioManager.Instance == null) return;

        if (attackEvent.AttackType == AttackType.Melee)
        {
            if (_audioConfig.TryGetClip(AudioKeys.Attack, out var attackClip))
            {
                AudioManager.Instance.PlaySfx(attackClip, transform.position);
            }
        }
        else if (attackEvent.AttackType == AttackType.Ranged)
        {
            if (_audioConfig.TryGetClip(AudioKeys.Shoot, out var shootClip))
            {
                AudioManager.Instance.PlaySfx(shootClip, transform.position);
            }
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
        if (_enableTimerFootsteps && _movementState != null && _movementState.IsMoving)
        {
            _stepTimer -= Time.deltaTime;
            if (_stepTimer <= 0f)
            {
                _stepTimer = _stepInterval;
                PlayAudio(AudioKeys.Step);
            }
        }
        else
        {
            _stepTimer = 0f;
        }
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

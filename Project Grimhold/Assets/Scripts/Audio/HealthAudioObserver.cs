/// <summary>Cue de audio derivado de la observación de salud de un personaje.</summary>
public enum HealthAudioCue
{
    None = 0,
    TakeDamage = 1,
    Death = 2,
}

/// <summary>
/// Observador puro (sin Unity) que convierte muestras de salud (válida / viva / salud) en el cue de audio a reproducir.
/// Reglas: la primera muestra válida solo fija la línea base; una pérdida de salud emite <see cref="HealthAudioCue.TakeDamage"/>;
/// la muerte emite <see cref="HealthAudioCue.Death"/> una sola vez por muerte; revivir re-arma la muerte y no emite daño
/// aunque la salud sea menor que antes de morir; las muestras inválidas no emiten ni tocan el estado.
/// El llamador debe seguir alimentando muestras aunque no pueda reproducir audio, para no generar cues de "puesta al día".
/// </summary>
public sealed class HealthAudioObserver
{
    private const float HealthEpsilon = 0.001f;

    private float _lastObservedHealth;
    private bool _hasBaseline;
    private bool _isDead;

    /// <summary>True si la última muestra válida correspondía a un personaje muerto.</summary>
    public bool IsDead => _isDead;

    /// <summary>Olvida línea base y estado de muerte; la próxima muestra válida será solo línea base.</summary>
    public void Reset()
    {
        _lastObservedHealth = 0f;
        _hasBaseline = false;
        _isDead = false;
    }

    /// <param name="isValid">El objeto observado es legible (spawneado y válido). Si es false, isAlive/health se ignoran.</param>
    public HealthAudioCue Observe(bool isValid, bool isAlive, float health)
    {
        if (!isValid)
        {
            return HealthAudioCue.None;
        }

        if (!_hasBaseline)
        {
            _hasBaseline = true;
            _isDead = !isAlive;
            _lastObservedHealth = health;
            return HealthAudioCue.None;
        }

        // La línea base se mantiene también mientras está muerto, para que revivir con menos salud no cuente como daño.
        float previousHealth = _lastObservedHealth;
        _lastObservedHealth = health;

        if (!isAlive)
        {
            if (_isDead)
            {
                return HealthAudioCue.None;
            }

            _isDead = true;
            return HealthAudioCue.Death;
        }

        if (_isDead)
        {
            _isDead = false;
            return HealthAudioCue.None;
        }

        return health < previousHealth - HealthEpsilon ? HealthAudioCue.TakeDamage : HealthAudioCue.None;
    }
}

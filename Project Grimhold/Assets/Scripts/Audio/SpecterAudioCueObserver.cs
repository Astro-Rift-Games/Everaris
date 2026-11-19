using ProjectGrimhold.Gameplay.Enemies.Specter;

/// <summary>
/// Observador puro (sin Unity) que convierte muestras del estado replicado del Espectro en el cue de audio a reproducir.
/// Reglas: el cue se emite una sola vez al ENTRAR en Canalizacion / Detonacion / Reaparicion; la primera muestra válida
/// (spawn, late join, restauración tras host migration) solo fija la línea base y no emite; las muestras inválidas no
/// emiten ni tocan la línea base. El llamador debe seguir alimentando muestras aunque no pueda reproducir audio, para
/// no generar cues de "puesta al día".
/// </summary>
public sealed class SpecterAudioCueObserver
{
    private SpecterStateType _lastState;
    private bool _hasBaseline;

    /// <summary>Olvida la línea base; la próxima muestra válida será solo línea base.</summary>
    public void Reset()
    {
        _lastState = default;
        _hasBaseline = false;
    }

    /// <param name="isValid">El objeto observado es legible (spawneado y válido). Si es false, <paramref name="state"/> se ignora.</param>
    public SpecterAudioCue Observe(bool isValid, SpecterStateType state)
    {
        if (!isValid)
        {
            return SpecterAudioCue.None;
        }

        if (!_hasBaseline)
        {
            _hasBaseline = true;
            _lastState = state;
            return SpecterAudioCue.None;
        }

        if (state == _lastState)
        {
            return SpecterAudioCue.None;
        }

        _lastState = state;
        return state switch
        {
            SpecterStateType.Canalizacion => SpecterAudioCue.Channel,
            SpecterStateType.Detonacion => SpecterAudioCue.Detonate,
            SpecterStateType.Reaparicion => SpecterAudioCue.Reappear,
            _ => SpecterAudioCue.None,
        };
    }
}

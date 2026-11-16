/// <summary>
/// Local render-only edge keyed by stance start tick. A completion at most 100 ms late may play,
/// including first observation; older completions are consumed silently (late join/resume).
/// Clock rewind cannot replay a consumed or ended draw. A newer start tick re-arms it.
/// </summary>
public sealed class AimStanceDrawCue
{
    public const float MaximumLatenessSeconds = 0.1f;
    private int _startTick = AimStanceDraw.NoStart;
    private bool _consumed;

    public bool Observe(bool active, int startTick, float elapsedSeconds, float drawSeconds)
    {
        if (!active || startTick < 0)
        {
            _consumed = true;
            return false;
        }
        if (startTick < _startTick) return false;
        if (startTick != _startTick)
        {
            _startTick = startTick;
            _consumed = false;
        }
        if (_consumed || float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds) ||
            float.IsNaN(drawSeconds) || float.IsInfinity(drawSeconds) || drawSeconds < 0f ||
            elapsedSeconds < drawSeconds)
            return false;

        _consumed = true;
        return elapsedSeconds - drawSeconds <= MaximumLatenessSeconds;
    }
}

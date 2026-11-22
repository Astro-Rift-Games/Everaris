/// <summary>Pure normalized fade level of a light that follows a particle emitter.</summary>
public static class ParticleLightLevel
{
    /// <summary>
    /// Moves the 0..1 level toward 1 while lit and toward 0 otherwise, linearly over the given
    /// fade durations. A zero duration snaps; an invalid delta leaves the level unchanged.
    /// </summary>
    public static float Step(float level, bool lit, float deltaSeconds, float fadeInSeconds, float fadeOutSeconds)
    {
        if (!(deltaSeconds >= 0f)) return level;
        float duration = lit ? fadeInSeconds : fadeOutSeconds;
        float target = lit ? 1f : 0f;
        if (!(duration > 0f)) return target;
        float step = deltaSeconds / duration;
        float next = lit ? level + step : level - step;
        return next < 0f ? 0f : next > 1f ? 1f : next;
    }
}

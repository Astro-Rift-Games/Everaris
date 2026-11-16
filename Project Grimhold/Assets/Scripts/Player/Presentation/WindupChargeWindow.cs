/// <summary>Pure acceptance-to-release window on the existing confirmed presentation clock.</summary>
public static class WindupChargeWindow
{
    public static bool TryResolve(float seconds, float releaseSeconds, out float progress)
    {
        progress = 0f;
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) ||
            float.IsNaN(releaseSeconds) || float.IsInfinity(releaseSeconds) ||
            releaseSeconds <= 0f || seconds < 0f || seconds >= releaseSeconds) return false;
        progress = seconds / releaseSeconds;
        return true;
    }
}

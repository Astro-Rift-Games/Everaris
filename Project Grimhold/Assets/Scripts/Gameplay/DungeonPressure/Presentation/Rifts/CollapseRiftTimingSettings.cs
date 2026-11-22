using UnityEngine;

public readonly struct CollapseRiftTimingSettings
{
    public readonly int SpawnFrameCount;
    public readonly float SpawnFps;
    public readonly int TelegraphFrameCount;
    public readonly float TelegraphFps;
    public readonly float TelegraphDurationSeconds;
    public readonly int EruptionFrameCount;
    public readonly float EruptionFps;
    public readonly int CooldownFrameCount;
    public readonly float CooldownFps;
    public readonly float PhaseOffsetSeconds;

    public CollapseRiftTimingSettings(
        int spawnFrameCount,
        float spawnFps,
        int telegraphFrameCount,
        float telegraphFps,
        float telegraphDurationSeconds,
        int eruptionFrameCount,
        float eruptionFps,
        int cooldownFrameCount,
        float cooldownFps,
        float phaseOffsetSeconds)
    {
        SpawnFrameCount = Mathf.Max(1, spawnFrameCount);
        SpawnFps = Mathf.Max(0.1f, spawnFps);
        TelegraphFrameCount = Mathf.Max(1, telegraphFrameCount);
        TelegraphFps = Mathf.Max(0.1f, telegraphFps);
        TelegraphDurationSeconds = Mathf.Max(0.05f, telegraphDurationSeconds);
        EruptionFrameCount = Mathf.Max(1, eruptionFrameCount);
        EruptionFps = Mathf.Max(0.1f, eruptionFps);
        CooldownFrameCount = Mathf.Max(1, cooldownFrameCount);
        CooldownFps = Mathf.Max(0.1f, cooldownFps);
        PhaseOffsetSeconds = Mathf.Max(0f, phaseOffsetSeconds);
    }
}

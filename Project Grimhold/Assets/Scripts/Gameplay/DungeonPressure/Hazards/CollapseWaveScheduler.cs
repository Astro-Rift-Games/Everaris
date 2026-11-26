using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure deterministic cadence and rift selection rules for global Collapse rupture waves (Doc 20 §4, §6).
/// </summary>
public static class CollapseWaveScheduler
{
    public static bool AreGlobalWavesEnabled(DungeonPressurePhase phase)
    {
        return phase == DungeonPressurePhase.CriticalPressure || phase == DungeonPressurePhase.Collapse;
    }

    public static int ComputeNextWaveIntervalTicks(
        DungeonPressurePhase phase,
        Vector2 criticalIntervalRangeSeconds,
        Vector2 collapseIntervalRangeSeconds,
        int seed,
        int waveSequence,
        float deltaTime)
    {
        if (!AreGlobalWavesEnabled(phase) || deltaTime <= 0f)
        {
            return 0;
        }

        Vector2 range = phase == DungeonPressurePhase.Collapse
            ? collapseIntervalRangeSeconds
            : criticalIntervalRangeSeconds;

        float minSec = Mathf.Max(0.1f, range.x);
        float maxSec = Mathf.Max(minSec, range.y);

        // Deterministic jitter in [0, 1] derived from (seed, waveSequence, phase) so Host Migration keeps cadence reproducible.
        float jitter = ComputeDeterministicUnitFloat(seed, waveSequence, (int)phase);
        float seconds = Mathf.Lerp(minSec, maxSec, jitter);
        return Mathf.Max(1, Mathf.CeilToInt(seconds / deltaTime));
    }

    /// <summary>
    /// Selects all eligible activatable Dormant rifts for a global wave while respecting
    /// concurrency and spatial overlap caps (Doc 20 §4, §6). Occupied and harmless rifts are untouched.
    /// </summary>
    public static void SelectEligibleRiftIndicesForWave(
        IReadOnlyList<CollapseRiftNetState> states,
        IReadOnlyList<Vector2> positions,
        int maxConcurrentActiveRifts,
        float minSimultaneousRiftSpacing,
        List<int> selectedIndices)
    {
        if (selectedIndices == null)
        {
            return;
        }

        selectedIndices.Clear();
        if (states == null || positions == null)
        {
            return;
        }

        int count = Mathf.Min(states.Count, positions.Count);
        if (count <= 0)
        {
            return;
        }

        int alreadyActiveCount = 0;
        for (int i = 0; i < count; i++)
        {
            CollapseRiftAuthoritativeStage stage = states[i].Stage;
            if (stage == CollapseRiftAuthoritativeStage.Telegraph ||
                stage == CollapseRiftAuthoritativeStage.Erupting)
            {
                alreadyActiveCount++;
            }
        }

        int remainingBudget = maxConcurrentActiveRifts > 0
            ? Mathf.Max(0, maxConcurrentActiveRifts - alreadyActiveCount)
            : int.MaxValue;

        if (remainingBudget <= 0)
        {
            return;
        }

        float minSpacingSq = minSimultaneousRiftSpacing > 0f
            ? minSimultaneousRiftSpacing * minSimultaneousRiftSpacing
            : 0f;

        for (int i = 0; i < count && selectedIndices.Count < remainingBudget; i++)
        {
            CollapseRiftNetState state = states[i];
            if (!state.IsActivatable || state.Stage != CollapseRiftAuthoritativeStage.Dormant)
            {
                continue;
            }

            if (minSpacingSq > 0f && IsTooCloseToActiveOrSelected(positions[i], states, positions, selectedIndices, minSpacingSq))
            {
                continue;
            }

            selectedIndices.Add(i);
        }
    }

    private static bool IsTooCloseToActiveOrSelected(
        Vector2 candidatePos,
        IReadOnlyList<CollapseRiftNetState> states,
        IReadOnlyList<Vector2> positions,
        List<int> selectedIndices,
        float minSpacingSq)
    {
        for (int i = 0; i < selectedIndices.Count; i++)
        {
            if ((candidatePos - positions[selectedIndices[i]]).sqrMagnitude < minSpacingSq)
            {
                return true;
            }
        }

        int count = Mathf.Min(states.Count, positions.Count);
        for (int i = 0; i < count; i++)
        {
            CollapseRiftAuthoritativeStage stage = states[i].Stage;
            if ((stage == CollapseRiftAuthoritativeStage.Telegraph || stage == CollapseRiftAuthoritativeStage.Erupting) &&
                (candidatePos - positions[i]).sqrMagnitude < minSpacingSq)
            {
                return true;
            }
        }

        return false;
    }

    private static float ComputeDeterministicUnitFloat(int seed, int sequence, int salt)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)seed) * 16777619u;
            h = (h ^ (uint)sequence) * 16777619u;
            h = (h ^ (uint)salt) * 16777619u;
            h ^= h >> 16;
            h *= 0x7feb352du;
            h ^= h >> 15;
            return (h & 0x00FFFFFFu) / (float)0x01000000u;
        }
    }
}

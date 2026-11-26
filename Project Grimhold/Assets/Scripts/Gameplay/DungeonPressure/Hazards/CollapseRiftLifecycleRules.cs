using UnityEngine;

/// <summary>
/// Pure authoritative lifecycle rules for floor collapse rifts (Doc 20 §3, §5, §11).
/// Deterministic, tick-driven, and free of Unity scene/Fusion dependencies for EditMode verification.
/// </summary>
public static class CollapseRiftLifecycleRules
{
    public static void Initialize(ref CollapseRiftNetState state, CollapseRiftBatch batch)
    {
        state = default;
        state.IsSecondaryBatch = batch == CollapseRiftBatch.Stage3Secondary;
        state.Stage = CollapseRiftAuthoritativeStage.Hidden;
    }

    public static void ApplyPhaseTransition(
        ref CollapseRiftNetState state,
        DungeonPressurePhase phase,
        int currentTick,
        int spawnDurationTicks,
        bool immediate,
        bool isClosed)
    {
        if (isClosed || state.Stage == CollapseRiftAuthoritativeStage.Disabled)
        {
            return;
        }

        CollapseRiftBatch batch = state.Batch;
        bool shouldBeVisible = CollapseRiftPhaseRules.IsFloorBatchVisible(batch, phase);
        if (!shouldBeVisible)
        {
            state.Stage = CollapseRiftAuthoritativeStage.Hidden;
            state.IsActivatable = false;
            state.EffectApplied = false;
            state.StateStartTick = currentTick;
            state.PhaseEndTick = currentTick;
            return;
        }

        state.IsActivatable = CollapseRiftPhaseRules.IsFloorBatchActivatable(batch, phase);

        if (state.Stage == CollapseRiftAuthoritativeStage.Hidden)
        {
            if (immediate || spawnDurationTicks <= 0)
            {
                state.Stage = CollapseRiftAuthoritativeStage.Dormant;
                state.StateStartTick = currentTick;
                state.PhaseEndTick = currentTick;
            }
            else
            {
                state.Stage = CollapseRiftAuthoritativeStage.Spawning;
                state.StateStartTick = currentTick;
                state.PhaseEndTick = currentTick + spawnDurationTicks;
            }
            state.EffectApplied = false;
            return;
        }

        // Doc 20 §12: ongoing eruptions/telegraphs/cooldowns finish naturally across phase transitions.
        if (immediate && !state.IsOccupied)
        {
            state.Stage = CollapseRiftAuthoritativeStage.Dormant;
            state.StateStartTick = currentTick;
            state.PhaseEndTick = currentTick;
            state.EffectApplied = false;
        }
    }

    public static bool TryActivate(
        ref CollapseRiftNetState state,
        int currentTick,
        int telegraphDurationTicks,
        bool isClosed)
    {
        if (isClosed ||
            !state.IsActivatable ||
            state.Stage != CollapseRiftAuthoritativeStage.Dormant)
        {
            return false;
        }

        int duration = Mathf.Max(1, telegraphDurationTicks);
        state.Stage = CollapseRiftAuthoritativeStage.Telegraph;
        state.EffectApplied = false;
        state.StateStartTick = currentTick;
        state.PhaseEndTick = currentTick + duration;
        return true;
    }

    public static void Step(
        ref CollapseRiftNetState state,
        int currentTick,
        int eruptionDurationTicks,
        int cooldownDurationTicks,
        bool isClosed,
        out bool enteredErupting)
    {
        enteredErupting = false;

        switch (state.Stage)
        {
            case CollapseRiftAuthoritativeStage.Hidden:
            case CollapseRiftAuthoritativeStage.Dormant:
            case CollapseRiftAuthoritativeStage.Disabled:
                return;

            case CollapseRiftAuthoritativeStage.Spawning:
                if (currentTick >= state.PhaseEndTick)
                {
                    state.Stage = isClosed
                        ? CollapseRiftAuthoritativeStage.Disabled
                        : CollapseRiftAuthoritativeStage.Dormant;
                    state.StateStartTick = currentTick;
                    state.PhaseEndTick = currentTick;
                }
                return;

            case CollapseRiftAuthoritativeStage.Telegraph:
                if (isClosed)
                {
                    state.Stage = CollapseRiftAuthoritativeStage.Disabled;
                    state.IsActivatable = false;
                    state.EffectApplied = false;
                    state.StateStartTick = currentTick;
                    state.PhaseEndTick = currentTick;
                    return;
                }

                if (currentTick >= state.PhaseEndTick)
                {
                    int duration = Mathf.Max(1, eruptionDurationTicks);
                    state.Stage = CollapseRiftAuthoritativeStage.Erupting;
                    state.EffectApplied = false;
                    state.StateStartTick = currentTick;
                    state.PhaseEndTick = currentTick + duration;
                    enteredErupting = true;
                }
                return;

            case CollapseRiftAuthoritativeStage.Erupting:
                if (currentTick >= state.PhaseEndTick)
                {
                    if (isClosed)
                    {
                        // Doc 20 §11: eruptions already running at closure complete once, then stay Disabled (never Cooldown).
                        state.Stage = CollapseRiftAuthoritativeStage.Disabled;
                        state.IsActivatable = false;
                        state.StateStartTick = currentTick;
                        state.PhaseEndTick = currentTick;
                        return;
                    }

                    int duration = Mathf.Max(1, cooldownDurationTicks);
                    state.Stage = CollapseRiftAuthoritativeStage.Cooldown;
                    state.StateStartTick = currentTick;
                    state.PhaseEndTick = currentTick + duration;
                }
                return;

            case CollapseRiftAuthoritativeStage.Cooldown:
                if (isClosed)
                {
                    state.Stage = CollapseRiftAuthoritativeStage.Disabled;
                    state.IsActivatable = false;
                    state.StateStartTick = currentTick;
                    state.PhaseEndTick = currentTick;
                    return;
                }

                if (currentTick >= state.PhaseEndTick)
                {
                    state.Stage = CollapseRiftAuthoritativeStage.Dormant;
                    state.EffectApplied = false;
                    state.StateStartTick = currentTick;
                    state.PhaseEndTick = currentTick;
                }
                return;
        }
    }

    public static void ApplyAbsoluteClosure(ref CollapseRiftNetState state, int currentTick)
    {
        state.IsActivatable = false;

        // Doc 20 §11 approved exception: eruptions already running at closure complete once,
        // then transition straight to Disabled without entering Cooldown.
        if (state.Stage == CollapseRiftAuthoritativeStage.Erupting)
        {
            return;
        }

        // All other states (Hidden, Spawning, Dormant, Telegraph, Cooldown, Disabled) stop immediately:
        // telegraphs are cancelled and never erupt.
        state.Stage = CollapseRiftAuthoritativeStage.Disabled;
        state.EffectApplied = false;
        state.StateStartTick = currentTick;
        state.PhaseEndTick = currentTick;
    }

    public static CollapseRiftVisualState ToVisualState(CollapseRiftAuthoritativeStage stage)
    {
        return stage switch
        {
            CollapseRiftAuthoritativeStage.Spawning => CollapseRiftVisualState.Spawning,
            CollapseRiftAuthoritativeStage.Dormant => CollapseRiftVisualState.Dormant,
            CollapseRiftAuthoritativeStage.Telegraph => CollapseRiftVisualState.Telegraph,
            CollapseRiftAuthoritativeStage.Erupting => CollapseRiftVisualState.Erupting,
            CollapseRiftAuthoritativeStage.Cooldown => CollapseRiftVisualState.Cooldown,
            _ => CollapseRiftVisualState.Hidden
        };
    }
}

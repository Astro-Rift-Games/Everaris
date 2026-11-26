using UnityEngine;

/// <summary>
/// Deterministic visual state machine for a single floor collapse rift.
/// Maps dungeon pressure phases and activation triggers to the 5 visual stages
/// (Stage0-Spawn, Stage1 Dormant, Stage2 Telegraph loop, Stage3 Eruption, Stage4 Cooldown).
/// </summary>
public sealed class CollapseRiftStateMachine
{
    private readonly CollapseRiftBatch _batch;
    private readonly CollapseRiftTimingSettings _timing;

    private CollapseRiftVisualState _state = CollapseRiftVisualState.Hidden;
    private bool _isActivatable;
    private float _stateElapsedSeconds;
    private int _currentFrameIndex;

    public CollapseRiftBatch Batch => _batch;
    public CollapseRiftVisualState State => _state;
    public bool IsActivatable => _isActivatable;
    public bool IsVisible => _state != CollapseRiftVisualState.Hidden;
    public int CurrentFrameIndex => _currentFrameIndex;
    public float StateElapsedSeconds => _stateElapsedSeconds;

    public CollapseRiftStateMachine(CollapseRiftBatch batch, CollapseRiftTimingSettings timing)
    {
        _batch = batch;
        _timing = timing;
    }

    public void ApplyDungeonPhase(DungeonPressurePhase phase, bool immediate)
    {
        bool shouldBeVisible = CollapseRiftPhaseRules.IsFloorBatchVisible(_batch, phase);
        if (!shouldBeVisible)
        {
            _state = CollapseRiftVisualState.Hidden;
            _isActivatable = false;
            _stateElapsedSeconds = 0f;
            _currentFrameIndex = 0;
            return;
        }

        _isActivatable = CollapseRiftPhaseRules.IsFloorBatchActivatable(_batch, phase);

        if (_state == CollapseRiftVisualState.Hidden)
        {
            if (immediate)
            {
                EnterState(CollapseRiftVisualState.Dormant);
            }
            else
            {
                EnterState(CollapseRiftVisualState.Spawning);
            }
            return;
        }

        if (immediate)
        {
            // Late-join, reconnect, or Editor preview snap: reflect active phase without replaying historical cycles.
            EnterState(CollapseRiftVisualState.Dormant);
        }
    }

    public bool TryTriggerActivation()
    {
        if (!_isActivatable || _state != CollapseRiftVisualState.Dormant)
        {
            return false;
        }

        EnterState(CollapseRiftVisualState.Telegraph);
        return true;
    }

    /// <summary>
    /// Authoritative visual sync hook for CollapseHazardDirector (TASK-335).
    /// Mirrors the replicated stage, elapsed time, and activatability directly without local transitions.
    /// </summary>
    public void SyncFromAuthority(CollapseRiftVisualState authoritativeState, float stateElapsedSeconds, bool isActivatable)
    {
        _state = authoritativeState;
        _isActivatable = isActivatable;
        _stateElapsedSeconds = Mathf.Max(0f, stateElapsedSeconds);

        switch (_state)
        {
            case CollapseRiftVisualState.Spawning:
                _currentFrameIndex = Mathf.Clamp(Mathf.FloorToInt(_stateElapsedSeconds * _timing.SpawnFps), 0, _timing.SpawnFrameCount - 1);
                break;

            case CollapseRiftVisualState.Telegraph:
            {
                float offsetTime = _stateElapsedSeconds + _timing.PhaseOffsetSeconds;
                int rawFrame = Mathf.FloorToInt(offsetTime * _timing.TelegraphFps);
                _currentFrameIndex = ((rawFrame % _timing.TelegraphFrameCount) + _timing.TelegraphFrameCount) % _timing.TelegraphFrameCount;
                break;
            }

            case CollapseRiftVisualState.Erupting:
                _currentFrameIndex = Mathf.Clamp(Mathf.FloorToInt(_stateElapsedSeconds * _timing.EruptionFps), 0, _timing.EruptionFrameCount - 1);
                break;

            case CollapseRiftVisualState.Cooldown:
                _currentFrameIndex = Mathf.Clamp(Mathf.FloorToInt(_stateElapsedSeconds * _timing.CooldownFps), 0, _timing.CooldownFrameCount - 1);
                break;

            default:
                _currentFrameIndex = 0;
                break;
        }
    }

    /// <summary>
    /// Authoritative visual sync hook reserved for Editor preview and tests.
    /// </summary>
    public void ForceVisualState(CollapseRiftVisualState state)
    {
        EnterState(state);
    }

    public void Tick(float deltaTime)
    {
        if (deltaTime <= 0f || _state == CollapseRiftVisualState.Hidden || _state == CollapseRiftVisualState.Dormant)
        {
            return;
        }

        _stateElapsedSeconds += deltaTime;

        switch (_state)
        {
            case CollapseRiftVisualState.Spawning:
                AdvanceOneShot(_timing.SpawnFrameCount, _timing.SpawnFps, CollapseRiftVisualState.Dormant);
                break;

            case CollapseRiftVisualState.Telegraph:
                if (_stateElapsedSeconds >= _timing.TelegraphDurationSeconds)
                {
                    EnterState(CollapseRiftVisualState.Erupting);
                }
                else
                {
                    float offsetTime = _stateElapsedSeconds + _timing.PhaseOffsetSeconds;
                    int rawFrame = Mathf.FloorToInt(offsetTime * _timing.TelegraphFps);
                    _currentFrameIndex = ((rawFrame % _timing.TelegraphFrameCount) + _timing.TelegraphFrameCount) % _timing.TelegraphFrameCount;
                }
                break;

            case CollapseRiftVisualState.Erupting:
                AdvanceOneShot(_timing.EruptionFrameCount, _timing.EruptionFps, CollapseRiftVisualState.Cooldown);
                break;

            case CollapseRiftVisualState.Cooldown:
                AdvanceOneShot(_timing.CooldownFrameCount, _timing.CooldownFps, CollapseRiftVisualState.Dormant);
                break;
        }
    }

    private void AdvanceOneShot(int frameCount, float fps, CollapseRiftVisualState nextState)
    {
        float duration = frameCount / fps;
        if (_stateElapsedSeconds >= duration)
        {
            EnterState(nextState);
            return;
        }

        int frame = Mathf.FloorToInt(_stateElapsedSeconds * fps);
        _currentFrameIndex = Mathf.Clamp(frame, 0, frameCount - 1);
    }

    private void EnterState(CollapseRiftVisualState newState)
    {
        _state = newState;
        _stateElapsedSeconds = 0f;

        if (newState == CollapseRiftVisualState.Telegraph)
        {
            int rawFrame = Mathf.FloorToInt(_timing.PhaseOffsetSeconds * _timing.TelegraphFps);
            _currentFrameIndex = ((rawFrame % _timing.TelegraphFrameCount) + _timing.TelegraphFrameCount) % _timing.TelegraphFrameCount;
        }
        else
        {
            _currentFrameIndex = 0;
        }
    }
}

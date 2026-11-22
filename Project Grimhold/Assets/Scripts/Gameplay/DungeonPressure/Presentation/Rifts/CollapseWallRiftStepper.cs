using UnityEngine;

/// <summary>
/// Deterministic frame stepper for wall collapse rifts across dungeon pressure stages:
/// - Stage 1 (Normal): Hidden
/// - Stage 2 (Reinforcements): Stage0-Spawn (on live transition) -> Stage1
/// - Stage 3 (CriticalPressure): Loop Stage1 + Stage2
/// - Stage 4 (Collapse): Loop Stage2 + Stage3 + Stage4
/// Frame interval is configurable from the Inspector.
/// </summary>
public sealed class CollapseWallRiftStepper
{
    private const float MinFrameIntervalSeconds = 0.02f;

    private readonly int _spawnFrameCount;
    private readonly int _stage1FrameCount;
    private readonly int _stage2FrameCount;
    private readonly int _stage3FrameCount;
    private readonly int _stage4FrameCount;
    private readonly float _phaseOffsetSeconds;

    private float _frameIntervalSeconds;
    private DungeonPressurePhase _phase = DungeonPressurePhase.Normal;
    private bool _isSpawning;
    private float _elapsedSeconds;

    private CollapseRiftVisualState _currentStage = CollapseRiftVisualState.Hidden;
    private int _currentStageFrameIndex;

    public bool IsVisible => _currentStage != CollapseRiftVisualState.Hidden;
    public CollapseRiftVisualState CurrentStage => _currentStage;
    public int CurrentStageFrameIndex => _currentStageFrameIndex;
    public float FrameIntervalSeconds => _frameIntervalSeconds;

    public CollapseWallRiftStepper(
        int spawnFrameCount,
        int stage1FrameCount,
        int stage2FrameCount,
        int stage3FrameCount,
        int stage4FrameCount,
        float frameIntervalSeconds,
        float phaseOffsetSeconds = 0f)
    {
        _spawnFrameCount = Mathf.Max(1, spawnFrameCount);
        _stage1FrameCount = Mathf.Max(1, stage1FrameCount);
        _stage2FrameCount = Mathf.Max(1, stage2FrameCount);
        _stage3FrameCount = Mathf.Max(1, stage3FrameCount);
        _stage4FrameCount = Mathf.Max(1, stage4FrameCount);
        _frameIntervalSeconds = Mathf.Max(MinFrameIntervalSeconds, frameIntervalSeconds);
        _phaseOffsetSeconds = Mathf.Max(0f, phaseOffsetSeconds);
    }

    public void SetFrameIntervalSeconds(float seconds)
    {
        _frameIntervalSeconds = Mathf.Max(MinFrameIntervalSeconds, seconds);
        EvaluateCurrentFrame();
    }

    public void ApplyDungeonPhase(DungeonPressurePhase phase, bool immediate)
    {
        bool wasHidden = _currentStage == CollapseRiftVisualState.Hidden;
        _phase = phase;

        if (!CollapseRiftPhaseRules.IsWallRiftVisible(phase))
        {
            _isSpawning = false;
            _elapsedSeconds = 0f;
            _currentStage = CollapseRiftVisualState.Hidden;
            _currentStageFrameIndex = 0;
            return;
        }

        if (wasHidden && !immediate)
        {
            _isSpawning = true;
            _elapsedSeconds = 0f;
        }
        else
        {
            _isSpawning = false;
            _elapsedSeconds = _phaseOffsetSeconds;
        }

        EvaluateCurrentFrame();
    }

    public void Tick(float deltaTime)
    {
        if (deltaTime <= 0f || _currentStage == CollapseRiftVisualState.Hidden)
        {
            return;
        }

        _elapsedSeconds += deltaTime;

        if (_isSpawning)
        {
            float spawnDuration = _spawnFrameCount * _frameIntervalSeconds;
            if (_elapsedSeconds >= spawnDuration)
            {
                _isSpawning = false;
                _elapsedSeconds = _phaseOffsetSeconds;
            }
        }

        EvaluateCurrentFrame();
    }

    private void EvaluateCurrentFrame()
    {
        if (!CollapseRiftPhaseRules.IsWallRiftVisible(_phase))
        {
            _currentStage = CollapseRiftVisualState.Hidden;
            _currentStageFrameIndex = 0;
            return;
        }

        if (_isSpawning)
        {
            int spawnFrame = Mathf.FloorToInt(_elapsedSeconds / _frameIntervalSeconds);
            _currentStage = CollapseRiftVisualState.Spawning;
            _currentStageFrameIndex = Mathf.Clamp(spawnFrame, 0, _spawnFrameCount - 1);
            return;
        }

        int step = Mathf.FloorToInt(_elapsedSeconds / _frameIntervalSeconds);

        switch (_phase)
        {
            case DungeonPressurePhase.Reinforcements:
                _currentStage = CollapseRiftVisualState.Dormant;
                _currentStageFrameIndex = PositiveMod(step, _stage1FrameCount);
                break;

            case DungeonPressurePhase.CriticalPressure:
            {
                int total = _stage1FrameCount + _stage2FrameCount;
                int index = PositiveMod(step, total);
                if (index < _stage1FrameCount)
                {
                    _currentStage = CollapseRiftVisualState.Dormant;
                    _currentStageFrameIndex = index;
                }
                else
                {
                    _currentStage = CollapseRiftVisualState.Telegraph;
                    _currentStageFrameIndex = index - _stage1FrameCount;
                }
                break;
            }

            case DungeonPressurePhase.Collapse:
            {
                int total = _stage2FrameCount + _stage3FrameCount + _stage4FrameCount;
                int index = PositiveMod(step, total);
                if (index < _stage2FrameCount)
                {
                    _currentStage = CollapseRiftVisualState.Telegraph;
                    _currentStageFrameIndex = index;
                }
                else if (index < _stage2FrameCount + _stage3FrameCount)
                {
                    _currentStage = CollapseRiftVisualState.Erupting;
                    _currentStageFrameIndex = index - _stage2FrameCount;
                }
                else
                {
                    _currentStage = CollapseRiftVisualState.Cooldown;
                    _currentStageFrameIndex = index - _stage2FrameCount - _stage3FrameCount;
                }
                break;
            }

            default:
                _currentStage = CollapseRiftVisualState.Hidden;
                _currentStageFrameIndex = 0;
                break;
        }
    }

    private static int PositiveMod(int value, int modulus)
    {
        if (modulus <= 1)
        {
            return 0;
        }

        int r = value % modulus;
        return r < 0 ? r + modulus : r;
    }
}

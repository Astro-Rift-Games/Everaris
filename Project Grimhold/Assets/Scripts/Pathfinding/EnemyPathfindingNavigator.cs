using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Manages the lifecycle of a single enemy's active path on State Authority:
/// shared-solver queries, partial-path fallback for unreachable targets, tick-based repath cadence,
/// deterministic initial staggering, arrival detection, no-progress stuck detection, and
/// immediate invalidation when <see cref="PathfindingGrid.TopologyVersion"/> changes (e.g. doors).
///
/// Resimulation safety: all timers, waypoint advances, and progress checks are keyed to
/// Fusion's <c>Runner.Tick</c> rather than frame time.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyPathfindingNavigator : MonoBehaviour
{
    [SerializeField]
    private PathfindingGridConfig _config;

    [Header("Navigation Cadence")]
    [Tooltip("How often to recalculate the path, in seconds.")]
    [SerializeField, Min(0.05f)]
    private float _repathIntervalSeconds = 0.5f;

    [Tooltip("Distance from the current waypoint at which the agent advances to the next.")]
    [SerializeField, Min(0.05f)]
    private float _waypointReachRadius = 0.3f;

    [Tooltip("How far the target must move before a repath is triggered immediately.")]
    [SerializeField, Min(0.1f)]
    private float _targetMovedThreshold = 1.5f;

    [Header("Stuck Detection & Logging")]
    [Tooltip("Seconds without meaningful progress before reporting IsStuck so callers can discard the destination.")]
    [SerializeField, Min(0.25f)]
    private float _stuckTimeoutSeconds = 2.0f;

    [Tooltip("Minimum displacement in world units required to reset the stuck progress timer.")]
    [SerializeField, Min(0.05f)]
    private float _stuckMinProgressDistance = 0.25f;

    [Tooltip("Minimum seconds between iteration-limit warning logs for this navigator.")]
    [SerializeField, Min(0.5f)]
    private float _warningCooldownSeconds = 5.0f;

    [Header("Path Smoothing")]
    [Tooltip("If true, applies CircleCast path smoothing to remove grid zig-zag.")]
    [SerializeField]
    private bool _enablePathSmoothing = false;

    private AStarPathSolver _solver;
    private PathfindingGrid _grid;

    private readonly List<Vector2> _waypointBuffer = new List<Vector2>(32);
    private int _waypointCount;
    private int _waypointIndex;

    private bool _hasValidPath;
    private bool _hasAttemptedPath;
    private bool _forceRepathOnNextQuery;
    private bool _hasReachedDestination;
    private bool _isStuck;
    private AStarPathStatus _lastPathStatus;

    private int _lastRepathTick = int.MinValue;
    private int _lastWaypointAdvanceTick = int.MinValue;
    private int _lastWarningTick = int.MinValue;
    private int _observedTopologyVersion = -1;

    private int _staggerOffsetTicks;
    private int _initialActivationTick = int.MinValue;

    private Vector2 _lastKnownTargetPosition;
    private Vector2 _progressAnchorPosition;
    private int _progressAnchorTick = int.MinValue;
    private int _lastProgressEvalTick = int.MinValue;

    private NetworkRunner _runner;

    /// <summary>True when a valid complete or partial path is cached with remaining waypoints.</summary>
    public bool HasValidPath => _hasValidPath && _waypointCount > 0;

    /// <summary>True when the agent reached the final waypoint of a complete path.</summary>
    public bool HasReachedDestination => _hasReachedDestination;

    /// <summary>True when the active path is a partial fallback toward the closest reachable node.</summary>
    public bool IsPathPartial => _lastPathStatus == AStarPathStatus.Partial;

    /// <summary>True when no meaningful progress has been made for the configured stuck timeout.</summary>
    public bool IsStuck => _isStuck;

    /// <summary>Active scene pathfinding grid discovered during initialization.</summary>
    public PathfindingGrid Grid => _grid;

    /// <summary>
    /// Initializes the navigator after the network object spawns.
    /// Infers authority from the attached <see cref="NetworkObject"/> when available.
    /// </summary>
    public void Initialize(NetworkRunner runner)
    {
        NetworkObject netObj = GetComponent<NetworkObject>();
        bool hasAuthority = netObj != null && netObj.IsValid
            ? netObj.HasStateAuthority
            : runner == null || runner.IsServer;
        uint? seed = netObj != null && netObj.IsValid ? netObj.Id.Raw : (uint?)null;
        Initialize(runner, hasAuthority, seed);
    }

    /// <summary>
    /// Explicit authority and stagger-seed initialization for runtime and deterministic EditMode tests.
    /// </summary>
    public void Initialize(NetworkRunner runner, bool hasStateAuthority, uint? staggerSeedOverride = null)
    {
        _runner = runner;
        _grid = UnityEngine.Object.FindFirstObjectByType<PathfindingGrid>();

        if (_config == null)
        {
            Debug.LogError(
                $"{nameof(EnemyPathfindingNavigator)} on '{name}' has no " +
                $"{nameof(PathfindingGridConfig)} assigned.",
                this);
            return;
        }

        if (_grid == null)
        {
            if (hasStateAuthority && runner != null)
            {
                Debug.LogWarning(
                    $"{nameof(EnemyPathfindingNavigator)} on '{name}': no " +
                    $"{nameof(PathfindingGrid)} found in the scene. Pathfinding will not be available.",
                    this);
            }
            return;
        }

        ResetState();

        if (hasStateAuthority)
        {
            uint seed = staggerSeedOverride ?? ResolveNetworkObjectSeed();
            int repathTicks = RepathIntervalTicks;
            _staggerOffsetTicks = repathTicks > 1 ? (int)(seed % (uint)repathTicks) : 0;
        }
    }

    /// <summary>
    /// Returns the normalized direction toward the next waypoint for <paramref name="targetPos"/>.
    /// Respects repath cadence even when unreachable, falls back to the closest reachable point,
    /// invalidates immediately on map topology changes, and tracks arrival and stuck status.
    /// </summary>
    public Vector2 GetDirectionToTarget(Vector2 currentPos, Vector2 targetPos, int currentTick)
    {
        if (_grid == null || !_grid.IsBuilt)
        {
            return Vector2.zero;
        }

        if (_solver == null)
        {
            _solver = _grid.SharedSolver ?? new AStarPathSolver(_grid.Width, _grid.Height, _config);
        }

        bool topologyInvalidated = CheckTopologyInvalidation(currentPos);

        if (!_hasAttemptedPath)
        {
            if (!CanExecuteInitialRequest(currentTick, topologyInvalidated))
            {
                return Vector2.zero;
            }

            RequestPath(currentPos, targetPos, currentTick);
        }
        else if (_forceRepathOnNextQuery ||
                 topologyInvalidated ||
                 ShouldRepath(currentTick) ||
                 TargetHasMoved(targetPos))
        {
            RequestPath(currentPos, targetPos, currentTick);
        }

        if (_hasReachedDestination)
        {
            return Vector2.zero;
        }

        if (!_hasValidPath || _waypointCount == 0)
        {
            UpdateProgressTracking(currentPos, currentTick);
            return Vector2.zero;
        }

        // Advance waypoints (including the final waypoint!) at most once per simulation tick.
        if (currentTick != _lastWaypointAdvanceTick)
        {
            float reachSqr = _waypointReachRadius * _waypointReachRadius;

            // If the agent is already within reach of the final waypoint (e.g., pushed past intermediate
            // nodes by knockback), mark arrival directly instead of walking backwards to earlier nodes.
            if (_lastPathStatus == AStarPathStatus.Complete &&
                Vector2.SqrMagnitude(_waypointBuffer[_waypointCount - 1] - currentPos) < reachSqr)
            {
                _waypointIndex = _waypointCount;
                _lastWaypointAdvanceTick = currentTick;
            }
            else
            {
                bool advanced = false;

                while (_waypointIndex < _waypointCount &&
                       Vector2.SqrMagnitude(_waypointBuffer[_waypointIndex] - currentPos) < reachSqr)
                {
                    _waypointIndex++;
                    advanced = true;
                }

                if (advanced)
                {
                    _lastWaypointAdvanceTick = currentTick;
                }
            }
        }

        if (_waypointIndex >= _waypointCount)
        {
            _hasValidPath = false;
            _hasReachedDestination = _lastPathStatus == AStarPathStatus.Complete;
            if (_hasReachedDestination)
            {
                _isStuck = false;
            }
            else
            {
                // Reached the end of a partial fallback path: evaluate stuck progress so callers can recover.
                UpdateProgressTracking(currentPos, currentTick);
            }
            return Vector2.zero;
        }

        UpdateProgressTracking(currentPos, currentTick);

        Vector2 direction = _waypointBuffer[_waypointIndex] - currentPos;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return Vector2.zero;
        }

        return direction.normalized;
    }

    /// <summary>
    /// Immediately invalidates the current path, arrival flag, and stuck timer so the next
    /// query requests a fresh path to the new destination.
    /// </summary>
    public void InvalidatePath()
    {
        _hasValidPath = false;
        _waypointCount = 0;
        _waypointIndex = 0;
        _hasReachedDestination = false;
        _isStuck = false;
        _lastPathStatus = AStarPathStatus.NoPath;
        _forceRepathOnNextQuery = true;
        _progressAnchorTick = int.MinValue;
        _lastProgressEvalTick = int.MinValue;
    }

    /// <summary>
    /// Resets only the stuck-progress window without dropping the cached path.
    /// </summary>
    public void ResetProgressTracking()
    {
        _isStuck = false;
        _progressAnchorTick = int.MinValue;
        _lastProgressEvalTick = int.MinValue;
    }

    private uint ResolveNetworkObjectSeed()
    {
        NetworkObject networkObject = GetComponent<NetworkObject>();
        return networkObject != null && networkObject.IsValid ? networkObject.Id.Raw : 0u;
    }

    private float SimulationDeltaTime => _runner != null && _runner.DeltaTime > 0f ? _runner.DeltaTime : 0.02f;

    private int RepathIntervalTicks => Mathf.Max(1, Mathf.CeilToInt(_repathIntervalSeconds / SimulationDeltaTime));

    private int StuckTimeoutTicks => Mathf.Max(1, Mathf.CeilToInt(_stuckTimeoutSeconds / SimulationDeltaTime));

    private int WarningCooldownTicks => Mathf.Max(1, Mathf.CeilToInt(_warningCooldownSeconds / SimulationDeltaTime));

    private bool CanExecuteInitialRequest(int currentTick, bool topologyInvalidated)
    {
        if (topologyInvalidated || _staggerOffsetTicks <= 0)
        {
            return true;
        }

        if (_initialActivationTick == int.MinValue)
        {
            _initialActivationTick = currentTick;
        }

        return (currentTick - _initialActivationTick) >= _staggerOffsetTicks;
    }

    private bool CheckTopologyInvalidation(Vector2 currentPos)
    {
        int currentVersion = _grid.TopologyVersion;
        if (_observedTopologyVersion < 0)
        {
            _observedTopologyVersion = currentVersion;
            return false;
        }

        if (currentVersion == _observedTopologyVersion)
        {
            return false;
        }

        // If we had no complete path (NoPath or Partial), a newly opened door might unlock a path.
        if (!_hasValidPath || _lastPathStatus != AStarPathStatus.Complete || _waypointCount == 0)
        {
            return true;
        }

        int startIdx = Mathf.Clamp(_waypointIndex, 0, _waypointCount - 1);
        if (_grid.HasTopologyChangedNearSegment(_observedTopologyVersion, currentPos, _waypointBuffer[startIdx]))
        {
            return true;
        }

        for (int i = startIdx; i < _waypointCount - 1; i++)
        {
            if (_grid.HasTopologyChangedNearSegment(_observedTopologyVersion, _waypointBuffer[i], _waypointBuffer[i + 1]))
            {
                return true;
            }
        }

        // Topology changed elsewhere on the map; keep our unaffected cached path.
        _observedTopologyVersion = currentVersion;
        return false;
    }

    private bool ShouldRepath(int currentTick)
    {
        return _lastRepathTick == int.MinValue || (currentTick - _lastRepathTick) >= RepathIntervalTicks;
    }

    private bool TargetHasMoved(Vector2 targetPos)
    {
        return Vector2.SqrMagnitude(targetPos - _lastKnownTargetPosition) >
               _targetMovedThreshold * _targetMovedThreshold;
    }

    private void RequestPath(Vector2 currentPos, Vector2 targetPos, int currentTick)
    {
        _hasAttemptedPath = true;
        _forceRepathOnNextQuery = false;
        _lastRepathTick = currentTick;
        _lastKnownTargetPosition = targetPos;
        _observedTopologyVersion = _grid.TopologyVersion;

        AStarPathStatus status = _solver.FindPathWithStatus(
            _grid,
            currentPos,
            targetPos,
            _enablePathSmoothing,
            _config.PathSmoothingRadius,
            _waypointBuffer,
            allowPartialPath: true,
            logIterationWarning: false);

        if (_solver.LastSearchHitIterationLimit &&
            (_lastWarningTick == int.MinValue || (currentTick - _lastWarningTick) >= WarningCooldownTicks))
        {
            _lastWarningTick = currentTick;
            Debug.LogWarning(
                $"[{nameof(EnemyPathfindingNavigator)}] '{name}' path search hit {_config.MaxPathIterations} iterations " +
                $"(start={currentPos}, end={targetPos}, status={status}).",
                this);
        }

        _lastPathStatus = status;

        if (status != AStarPathStatus.NoPath && _waypointBuffer.Count > 0)
        {
            _waypointCount = _waypointBuffer.Count;
            _waypointIndex = 0;
            _hasValidPath = true;
            _hasReachedDestination = false;

            // Skip starting node if we are already standing right on it (when further waypoints exist).
            float reachSqr = _waypointReachRadius * _waypointReachRadius;
            if (_waypointCount > 1 && Vector2.SqrMagnitude(_waypointBuffer[0] - currentPos) < reachSqr)
            {
                _waypointIndex = 1;
            }
        }
        else
        {
            _hasValidPath = false;
            _waypointCount = 0;
            _waypointIndex = 0;
            _hasReachedDestination = false;
        }
    }

    private void UpdateProgressTracking(Vector2 currentPos, int currentTick)
    {
        if (_progressAnchorTick == int.MinValue)
        {
            _progressAnchorPosition = currentPos;
            _progressAnchorTick = currentTick;
            _lastProgressEvalTick = currentTick;
            _isStuck = false;
            return;
        }

        // Resimulation guard: do not evaluate backwards or duplicate ticks.
        if (currentTick <= _lastProgressEvalTick)
        {
            return;
        }

        _lastProgressEvalTick = currentTick;

        float minProgressSqr = _stuckMinProgressDistance * _stuckMinProgressDistance;
        if (Vector2.SqrMagnitude(currentPos - _progressAnchorPosition) >= minProgressSqr)
        {
            _progressAnchorPosition = currentPos;
            _progressAnchorTick = currentTick;
            _isStuck = false;
            return;
        }

        if ((currentTick - _progressAnchorTick) >= StuckTimeoutTicks)
        {
            _isStuck = true;
        }
    }

    private void ResetState()
    {
        _waypointBuffer.Clear();
        _waypointCount = 0;
        _waypointIndex = 0;
        _hasValidPath = false;
        _hasAttemptedPath = false;
        _forceRepathOnNextQuery = false;
        _hasReachedDestination = false;
        _isStuck = false;
        _lastPathStatus = AStarPathStatus.NoPath;
        _lastRepathTick = int.MinValue;
        _lastWaypointAdvanceTick = int.MinValue;
        _lastWarningTick = int.MinValue;
        _observedTopologyVersion = _grid != null ? _grid.TopologyVersion : -1;
        _staggerOffsetTicks = 0;
        _initialActivationTick = int.MinValue;
        _lastKnownTargetPosition = Vector2.zero;
        _progressAnchorPosition = Vector2.zero;
        _progressAnchorTick = int.MinValue;
        _lastProgressEvalTick = int.MinValue;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (_config != null)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, _config.AgentRadius);
        }

        if (!_hasValidPath || _waypointCount == 0) return;

        Gizmos.color = _lastPathStatus == AStarPathStatus.Partial ? Color.magenta : Color.cyan;
        for (int i = 0; i < _waypointCount - 1; i++)
        {
            Gizmos.DrawLine(
                new Vector3(_waypointBuffer[i].x, _waypointBuffer[i].y, 0f),
                new Vector3(_waypointBuffer[i + 1].x, _waypointBuffer[i + 1].y, 0f));
        }

        if (_waypointIndex < _waypointCount)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(
                new Vector3(_waypointBuffer[_waypointIndex].x,
                    _waypointBuffer[_waypointIndex].y, 0f),
                0.15f);
        }

        Vector2 last = _waypointBuffer[_waypointCount - 1];
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(new Vector3(last.x, last.y, 0f), 0.2f);
    }
#endif
}

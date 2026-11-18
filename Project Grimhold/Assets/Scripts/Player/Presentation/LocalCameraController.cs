using UnityEngine;

/// <summary>
/// Local gameplay camera (03 - Player Design §8). Follows a target with a cursor-driven
/// offset, smoothing and a follow-speed cap read live from <see cref="GameplayCameraConfig"/>,
/// overridden per player by <see cref="GameplayCameraPreferences"/>.
/// This component belongs to the presentation layer and does not participate in network simulation.
///
/// The follow position is kept separately from the rendered position: when a
/// <see cref="CameraShakeController"/> is present on the same GameObject, its
/// <see cref="CameraShakeController.ShakeOffset"/> is added on top every frame and never
/// feeds back into the follow.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class LocalCameraController : MonoBehaviour
{
    /// <summary>
    /// Singleton reference to the active local camera controller.
    /// </summary>
    public static LocalCameraController Instance { get; private set; }

    [Header("Configuration")]
    [SerializeField]
    private GameplayCameraConfig _config;

    [Tooltip("Scene pointer source for the cursor offset. Optional; without it the camera follows without cursor offset.")]
    [SerializeField]
    private PlayerInputReader _cursorInput;

    [Tooltip("Per-scene world bounds the view must stay inside. Optional; null means no clamp.")]
    [SerializeField]
    private CameraMapBounds _mapBounds;

    [Header("Tracking Configuration")]
    [SerializeField]
    private float _zDepth = -10f;

    private Transform _target;
    private ICameraCursorSource _cursorSource;
    private bool _followCursor;
    private Vector2 _followPosition;
    private Vector2 _followVelocity;
    private Camera _camera;
    private CameraShakeController _shakeController;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _shakeController = GetComponent<CameraShakeController>();
        _followPosition = transform.position;

        if (_cursorInput != null)
        {
            _cursorSource = _cursorInput;
        }

        if (_config == null)
        {
            Debug.LogError($"{nameof(LocalCameraController)} on {gameObject.name} has no {nameof(GameplayCameraConfig)} assigned; the camera will not follow.", this);
        }
    }

    private void OnEnable()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"Another active instance of {nameof(LocalCameraController)} was detected on {Instance.gameObject.name}. " +
                             $"Replacing reference with the instance on {gameObject.name}.", this);
        }

        Instance = this;
        _followVelocity = Vector2.zero;

        // If the local player binder is already spawned, attach immediately.
        LocalPlayerCameraBinder localPlayer = LocalPlayerCameraBinder.LocalPlayerInstance;
        if (localPlayer != null)
        {
            SetTarget(localPlayer.transform, followCursor: true);
        }
    }

    private void OnDisable()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        _followVelocity = Vector2.zero;
    }

    /// <summary>
    /// Replaces the pointer that drives the cursor offset (explicit initialization and test seam).
    /// </summary>
    public void SetCursorSource(ICameraCursorSource source)
    {
        _cursorSource = source;
    }

    /// <summary>
    /// Sets the tracking target for the camera.
    /// </summary>
    /// <param name="target">The transform to follow.</param>
    /// <param name="followCursor">
    /// True applies the cursor offset while following this target (the local player);
    /// false follows the target without cursor offset (for example while spectating a teammate).
    /// </param>
    public void SetTarget(Transform target, bool followCursor = false)
    {
        if (target == null)
        {
            Debug.LogWarning($"{nameof(SetTarget)} received a null target reference.", this);
            return;
        }

        _followCursor = followCursor;

        if (_target == target)
        {
            return;
        }

        _target = target;
        _followVelocity = Vector2.zero;

        if (_config == null || _config.SnapOnTargetAssigned)
        {
            SnapToTarget();
        }
    }

    /// <summary>
    /// Clears the tracking target if it matches the specified target.
    /// </summary>
    /// <param name="target">The target to clear.</param>
    public void ClearTarget(Transform target)
    {
        if (_target != target)
        {
            return;
        }

        _target = null;
        _followCursor = false;
        _followVelocity = Vector2.zero;
    }

    private void LateUpdate()
    {
        UpdateCamera(Time.deltaTime);
    }

    /// <summary>
    /// Advances follow, cursor offset and shake for one frame.
    /// </summary>
    public void UpdateCamera(float deltaTime)
    {
        if (_config == null)
        {
            return;
        }

        ApplyOrthographicSize();

        if (_target == null)
        {
            return;
        }

        Vector2 desired = ClampToMap(ResolveTargetCenter() + ResolveCursorOffset());
        // Clamp again after following: the ortho size can change live and shrink the allowed area.
        _followPosition = ClampToMap(Follow(_followPosition, desired, deltaTime));
        ApplyTransform();
    }

    private Vector2 ResolveTargetCenter()
    {
        Vector3 targetPosition = _target.position;
        Vector2 offset = _config != null ? _config.Offset : Vector2.zero;
        return new Vector2(targetPosition.x + offset.x, targetPosition.y + offset.y);
    }

    private Vector2 ResolveCursorOffset()
    {
        if (!_followCursor || _cursorSource == null || !_cursorSource.TryGetCursorViewport(_camera, out Vector2 viewport))
        {
            return Vector2.zero;
        }

        return GameplayCameraMath.CursorOffset(
            viewport,
            ViewHalfExtents(),
            Effective(GameplayCameraPreferences.Setting.MaxOffsetDistance),
            _config.SafeMargin,
            _config.CursorDeadZone);
    }

    private Vector2 Follow(Vector2 current, Vector2 desired, float deltaTime)
    {
        float smoothTime = Effective(GameplayCameraPreferences.Setting.SmoothTime);
        float followSpeed = Effective(GameplayCameraPreferences.Setting.FollowSpeed);

        if (smoothTime <= 0f)
        {
            _followVelocity = Vector2.zero;
            return Vector2.MoveTowards(current, desired, followSpeed * deltaTime);
        }

        Vector2 next = Vector2.SmoothDamp(current, desired, ref _followVelocity, smoothTime, followSpeed, deltaTime);
        return Vector2.MoveTowards(current, next, followSpeed * deltaTime);
    }

    private void ApplyOrthographicSize()
    {
        // Zoom is designer-owned: there is no player override for the orthographic size.
        _camera.orthographicSize = _config.OrthographicSize;
    }

    // Player preference override over the config value; cached in memory, no per-frame PlayerPrefs reads.
    private float Effective(GameplayCameraPreferences.Setting setting)
    {
        return GameplayCameraPreferences.Get(setting, _config);
    }

    private Vector2 ClampToMap(Vector2 center)
    {
        return _mapBounds == null
            ? center
            : GameplayCameraMath.ClampToBounds(center, ViewHalfExtents(), _mapBounds.WorldBounds);
    }

    private Vector2 ViewHalfExtents()
    {
        return GameplayCameraMath.OrthographicHalfExtents(_camera.orthographicSize, _camera.aspect);
    }

    private void ApplyTransform()
    {
        Vector3 shakeOffset = _shakeController != null ? _shakeController.ShakeOffset : Vector3.zero;
        transform.position = new Vector3(
            _followPosition.x + shakeOffset.x,
            _followPosition.y + shakeOffset.y,
            _zDepth);
    }

    private void SnapToTarget()
    {
        if (_target == null)
        {
            return;
        }

        if (_config != null)
        {
            ApplyOrthographicSize();
        }

        _followPosition = ClampToMap(ResolveTargetCenter());
        _followVelocity = Vector2.zero;
        ApplyTransform();
    }
}

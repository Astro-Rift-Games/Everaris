using UnityEngine;

/// <summary>
/// Gameplay camera tuning owned by 03 - Player Design §8 and §11.
/// Read every frame by <see cref="LocalCameraController"/>, so edits apply live in Play Mode.
/// The player may override only offset distance, smoothing and follow speed (within the ranges below) through
/// <see cref="GameplayCameraPreferences"/>; every other value, including the orthographic size, is designer-only.
/// Local presentation configuration; never replicated and never mutated by gameplay code.
/// </summary>
[CreateAssetMenu(fileName = "GameplayCameraConfig", menuName = "Grimhold/Presentation/GameplayCameraConfig")]
public sealed class GameplayCameraConfig : ScriptableObject
{
    [Header("Cursor Offset")]
    [Tooltip("Maximum distance in world units the camera moves from the followed player toward the cursor.")]
    [SerializeField, Min(0f)]
    private float _maxOffsetDistance = 3f;

    [Tooltip("Fraction (0-0.9) of the screen center radius where the cursor causes no offset. The offset ramps from the dead zone edge (zero) to the screen edge (maximum distance).")]
    [SerializeField, Range(0f, 0.9f)]
    private float _cursorDeadZone = 0.1f;

    [Tooltip("Fraction (0-1) of the half screen kept between the followed player and the screen edge, so the cursor offset never pushes the player out of view.")]
    [SerializeField, Range(0f, 1f)]
    private float _safeMargin = 0.25f;

    [Tooltip("Fixed framing offset in world units added to the followed target's position, before the cursor offset.")]
    [SerializeField]
    private Vector2 _offset = Vector2.zero;

    [Header("Follow")]
    [Tooltip("Maximum camera speed in world units per second.")]
    [SerializeField, Min(0.01f)]
    private float _followSpeed = 50f;

    [Tooltip("Approximate time in seconds to reach the desired position. Zero follows without easing.")]
    [SerializeField, Min(0f)]
    private float _smoothTime = 0.1f;

    [Tooltip("When enabled, assigning a new target positions the camera on it instantly (still respecting map bounds) instead of easing toward it.")]
    [SerializeField]
    private bool _snapOnTargetAssigned = true;

    [Header("View")]
    [Tooltip("Orthographic size (half the visible height in world units).")]
    [SerializeField, Min(0.01f)]
    private float _orthographicSize = 10f;

    [Header("Player Preference Ranges (Settings sliders)")]
    [Tooltip("Min (x) and max (y) maximum offset distance the player can pick in Settings.")]
    [SerializeField]
    private Vector2 _maxOffsetDistanceRange = new(0f, 6f);

    [Tooltip("Min (x) and max (y) smoothing time in seconds the player can pick in Settings.")]
    [SerializeField]
    private Vector2 _smoothTimeRange = new(0f, 0.5f);

    [Tooltip("Min (x) and max (y) follow speed the player can pick in Settings.")]
    [SerializeField]
    private Vector2 _followSpeedRange = new(5f, 100f);

    public float MaxOffsetDistance => _maxOffsetDistance;
    public float CursorDeadZone => _cursorDeadZone;
    public float SafeMargin => _safeMargin;
    public Vector2 Offset => _offset;
    public float FollowSpeed => _followSpeed;
    public float SmoothTime => _smoothTime;
    public bool SnapOnTargetAssigned => _snapOnTargetAssigned;
    public float OrthographicSize => _orthographicSize;
    public Vector2 MaxOffsetDistanceRange => _maxOffsetDistanceRange;
    public Vector2 SmoothTimeRange => _smoothTimeRange;
    public Vector2 FollowSpeedRange => _followSpeedRange;

#if UNITY_EDITOR
    private void OnValidate()
    {
        _maxOffsetDistance = Mathf.Max(0f, _maxOffsetDistance);
        _cursorDeadZone = Mathf.Clamp(_cursorDeadZone, 0f, 0.9f);
        _safeMargin = Mathf.Clamp01(_safeMargin);
        _followSpeed = Mathf.Max(0.01f, _followSpeed);
        _smoothTime = Mathf.Max(0f, _smoothTime);
        _orthographicSize = Mathf.Max(0.01f, _orthographicSize);
        _maxOffsetDistanceRange = SanitizeRange(_maxOffsetDistanceRange, 0f);
        _smoothTimeRange = SanitizeRange(_smoothTimeRange, 0f);
        _followSpeedRange = SanitizeRange(_followSpeedRange, 0.01f);
    }

    private static Vector2 SanitizeRange(Vector2 range, float floor)
    {
        float min = Mathf.Max(floor, range.x);
        return new Vector2(min, Mathf.Max(min, range.y));
    }
#endif
}

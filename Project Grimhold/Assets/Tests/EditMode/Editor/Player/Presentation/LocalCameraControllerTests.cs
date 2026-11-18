using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class LocalCameraControllerTests
{
    private const float Tolerance = 0.0001f;
    private const float Aspect = 16f / 9f;

    private GameObject _cameraObject;
    private Camera _camera;
    private CameraShakeController _shake;
    private CameraShakeConfig _shakeConfig;
    private LocalCameraController _controller;
    private GameplayCameraConfig _config;
    private GameObject _localPlayer;
    private GameObject _teammate;
    private FakeCursorSource _cursor;
    private GameObject _boundsObject;
    private GameplayCameraPreferencesTestScope _preferences;

    [SetUp]
    public void SetUp()
    {
        _preferences = new GameplayCameraPreferencesTestScope();
        _cameraObject = new GameObject("GameplayCameraUnderTest");
        _camera = _cameraObject.AddComponent<Camera>();
        _camera.orthographic = true;
        _camera.aspect = Aspect;
        _shake = _cameraObject.AddComponent<CameraShakeController>();
        _shakeConfig = ScriptableObject.CreateInstance<CameraShakeConfig>();
        _shake.Configure(_shakeConfig);
        _controller = _cameraObject.AddComponent<LocalCameraController>();

        _config = ScriptableObject.CreateInstance<GameplayCameraConfig>();
        EditConfig(maxOffsetDistance: 4f, followSpeed: 1000f, smoothTime: 0f, orthographicSize: 9f, safeMargin: 0.2f);

        var controllerObject = new SerializedObject(_controller);
        controllerObject.FindProperty("_config").objectReferenceValue = _config;
        controllerObject.ApplyModifiedPropertiesWithoutUndo();
        InvokeLifecycle("Awake");

        _localPlayer = new GameObject("LocalPlayer");
        _localPlayer.transform.position = new Vector3(10f, 5f, 0f);
        _teammate = new GameObject("Teammate");
        _teammate.transform.position = new Vector3(-20f, 30f, 0f);
        _cursor = new FakeCursorSource();
        _controller.SetCursorSource(_cursor);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_cameraObject);
        if (_boundsObject != null)
        {
            Object.DestroyImmediate(_boundsObject);
        }
        Object.DestroyImmediate(_localPlayer);
        Object.DestroyImmediate(_teammate);
        Object.DestroyImmediate(_config);
        Object.DestroyImmediate(_shakeConfig);
        _preferences.Dispose();
    }

    [Test]
    public void SetTarget_SnapsToTargetAtConfiguredDepth()
    {
        _controller.SetTarget(_localPlayer.transform, followCursor: true);

        AssertCameraAt(new Vector2(10f, 5f));
        Assert.That(_cameraObject.transform.position.z, Is.EqualTo(-10f).Within(Tolerance));
    }

    [Test]
    public void UpdateCamera_AppliesConfiguredOrthographicSize()
    {
        _controller.UpdateCamera(0.016f);

        Assert.That(_camera.orthographicSize, Is.EqualTo(9f).Within(Tolerance));
    }

    [Test]
    public void UpdateCamera_CursorTowardRightEdge_OffsetsTowardCursorUpToMaxDistance()
    {
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _cursor.Viewport = new Vector2(1f, 0.5f);

        _controller.UpdateCamera(1f);

        AssertCameraAt(new Vector2(14f, 5f));
    }

    [Test]
    public void UpdateCamera_CursorHalfwayBetweenDeadZoneAndEdge_OffsetsHalfOfMaxDistance()
    {
        EditConfigField("_cursorDeadZone", 0.2f);
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _cursor.Viewport = new Vector2(0.8f, 0.5f);

        _controller.UpdateCamera(1f);

        AssertCameraAt(new Vector2(12f, 5f));
    }

    [Test]
    public void UpdateCamera_CursorInsideConfiguredDeadZone_DoesNotOffset()
    {
        EditConfigField("_cursorDeadZone", 0.3f);
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _cursor.Viewport = new Vector2(0.6f, 0.55f);

        _controller.UpdateCamera(1f);

        AssertCameraAt(new Vector2(10f, 5f));
    }

    [Test]
    public void UpdateCamera_CursorUnavailable_OffsetReturnsToZero()
    {
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _cursor.Viewport = new Vector2(1f, 0.5f);
        _controller.UpdateCamera(1f);

        _cursor.Available = false;
        _controller.UpdateCamera(1f);

        AssertCameraAt(new Vector2(10f, 5f));
    }

    [Test]
    public void UpdateCamera_ConfigEditedAtRuntime_AppliesOnNextUpdate()
    {
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _cursor.Viewport = new Vector2(1f, 0.5f);
        _controller.UpdateCamera(1f);

        EditConfig(maxOffsetDistance: 2f, followSpeed: 1000f, smoothTime: 0f, orthographicSize: 12f, safeMargin: 0.2f);
        _controller.UpdateCamera(1f);

        Assert.That(_camera.orthographicSize, Is.EqualTo(12f).Within(Tolerance));
        AssertCameraAt(new Vector2(12f, 5f));
    }

    [Test]
    public void UpdateCamera_PlayerPreferenceChangedAtRuntime_AppliesOnNextUpdate()
    {
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _cursor.Viewport = new Vector2(1f, 0.5f);
        _controller.UpdateCamera(1f);
        AssertCameraAt(new Vector2(14f, 5f));

        GameplayCameraPreferences.Set(GameplayCameraPreferences.Setting.MaxOffsetDistance, 2f, _config);
        _controller.UpdateCamera(1f);

        AssertCameraAt(new Vector2(12f, 5f));
    }

    [Test]
    public void UpdateCamera_OrthographicSize_IgnoresPlayerPreferencesAndFollowsTheAssetLive()
    {
        // A stale or hand-edited key must never act as a zoom override.
        const string staleZoomKey = "Grimhold.Presentation.Camera.OrthographicSize";
        PlayerPrefs.SetFloat(staleZoomKey, 12f);
        try
        {
            GameplayCameraPreferences.Set(GameplayCameraPreferences.Setting.MaxOffsetDistance, 1f, _config);
            GameplayCameraPreferences.Set(GameplayCameraPreferences.Setting.SmoothTime, 0.5f, _config);
            GameplayCameraPreferences.Set(GameplayCameraPreferences.Setting.FollowSpeed, 5f, _config);

            _controller.UpdateCamera(0.016f);
            Assert.That(_camera.orthographicSize, Is.EqualTo(9f).Within(Tolerance));

            EditConfig(maxOffsetDistance: 4f, followSpeed: 1000f, smoothTime: 0f, orthographicSize: 11f, safeMargin: 0.2f);
            _controller.UpdateCamera(0.016f);
            Assert.That(_camera.orthographicSize, Is.EqualTo(11f).Within(Tolerance));
        }
        finally
        {
            PlayerPrefs.DeleteKey(staleZoomKey);
        }
    }

    [Test]
    public void UpdateCamera_NoPreferenceSet_AssetEditsApplyLiveToOffsetSmoothingAndFollowSpeed()
    {
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _cursor.Viewport = new Vector2(1f, 0.5f);
        _controller.UpdateCamera(1f);
        AssertCameraAt(new Vector2(14f, 5f));

        EditConfig(maxOffsetDistance: 1.5f, followSpeed: 1000f, smoothTime: 0f, orthographicSize: 9f, safeMargin: 0.2f);
        _controller.UpdateCamera(1f);
        AssertCameraAt(new Vector2(11.5f, 5f));

        EditConfig(maxOffsetDistance: 1.5f, followSpeed: 2f, smoothTime: 0.3f, orthographicSize: 9f, safeMargin: 0.2f);
        _localPlayer.transform.position = new Vector3(110f, 5f, 0f);
        float before = _cameraObject.transform.position.x;
        _controller.UpdateCamera(0.1f);

        float moved = _cameraObject.transform.position.x - before;
        Assert.That(moved, Is.GreaterThan(0f));
        Assert.That(moved, Is.LessThanOrEqualTo(2f * 0.1f + Tolerance));
    }

    [Test]
    public void UpdateCamera_PlayerFollowAndSmoothingPreferences_OverrideConfig()
    {
        GameplayCameraPreferences.Set(GameplayCameraPreferences.Setting.FollowSpeed, 5f, _config);
        GameplayCameraPreferences.Set(GameplayCameraPreferences.Setting.SmoothTime, 0.5f, _config);
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _localPlayer.transform.position = new Vector3(110f, 5f, 0f);

        _controller.UpdateCamera(0.1f);

        float moved = _cameraObject.transform.position.x - 10f;
        Assert.That(moved, Is.GreaterThan(0f));
        Assert.That(moved, Is.LessThanOrEqualTo(5f * 0.1f + Tolerance));
    }

    [Test]
    public void UpdateCamera_PreferencesReset_ReturnsToConfigValues()
    {
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _cursor.Viewport = new Vector2(1f, 0.5f);
        GameplayCameraPreferences.Set(GameplayCameraPreferences.Setting.MaxOffsetDistance, 1f, _config);
        _controller.UpdateCamera(1f);
        AssertCameraAt(new Vector2(11f, 5f));

        GameplayCameraPreferences.ResetAll();
        _controller.UpdateCamera(1f);

        AssertCameraAt(new Vector2(14f, 5f));
    }

    [Test]
    public void UpdateCamera_FollowSpeed_CapsMovementPerFrame()
    {
        EditConfig(maxOffsetDistance: 4f, followSpeed: 2f, smoothTime: 0.3f, orthographicSize: 9f, safeMargin: 0.2f);
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _localPlayer.transform.position = new Vector3(110f, 5f, 0f);

        _controller.UpdateCamera(0.1f);

        float moved = _cameraObject.transform.position.x - 10f;
        Assert.That(moved, Is.GreaterThan(0f));
        Assert.That(moved, Is.LessThanOrEqualTo(2f * 0.1f + Tolerance));
    }

    [Test]
    public void UpdateCamera_SmoothTime_EasesTowardTarget()
    {
        EditConfig(maxOffsetDistance: 4f, followSpeed: 1000f, smoothTime: 0.5f, orthographicSize: 9f, safeMargin: 0.2f);
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _localPlayer.transform.position = new Vector3(20f, 5f, 0f);

        _controller.UpdateCamera(0.016f);

        float x = _cameraObject.transform.position.x;
        Assert.That(x, Is.GreaterThan(10f));
        Assert.That(x, Is.LessThan(20f));
    }

    [Test]
    public void UpdateCamera_Shake_IsAppliedOnTopWithoutDriftingTheFollowPosition()
    {
        EditConfig(maxOffsetDistance: 4f, followSpeed: 1000f, smoothTime: 0.2f, orthographicSize: 9f, safeMargin: 0.2f);
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _shake.RequestDirectionalImpulse(new Vector2(1f, 0.5f), 1f);
        _shake.UpdateShake(0.01f);
        Vector3 shakeOffset = _shake.ShakeOffset;
        Assume.That(shakeOffset.sqrMagnitude, Is.GreaterThan(0f));

        _controller.UpdateCamera(0.016f);
        AssertCameraAt(new Vector2(10f + shakeOffset.x, 5f + shakeOffset.y));

        _shake.UpdateShake(10f);
        Assume.That(_shake.ShakeOffset, Is.EqualTo(Vector3.zero));
        _controller.UpdateCamera(0.016f);

        AssertCameraAt(new Vector2(10f, 5f));
    }

    [Test]
    public void SetTarget_SpectatedTeammateWithoutFollowCursor_SnapsAndIgnoresCursor()
    {
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _cursor.Viewport = new Vector2(1f, 1f);
        _controller.UpdateCamera(1f);

        _controller.ClearTarget(_localPlayer.transform);
        _controller.SetTarget(_teammate.transform);
        AssertCameraAt(new Vector2(-20f, 30f));

        _controller.UpdateCamera(1f);
        AssertCameraAt(new Vector2(-20f, 30f));
    }

    [Test]
    public void UpdateCamera_CursorNearMapEdge_ViewStaysInsideBounds()
    {
        UseMapBounds(new Rect(-50f, -40f, 100f, 80f));
        _localPlayer.transform.position = new Vector3(33f, 5f, 0f);
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _cursor.Viewport = new Vector2(1f, 0.5f);

        _controller.UpdateCamera(1f);

        // Half view width at ortho 9 and aspect 16:9 is 16; the map ends at x=50.
        AssertCameraAt(new Vector2(34f, 5f));
    }

    [Test]
    public void SetTarget_SnapNearMapEdge_IsClamped()
    {
        UseMapBounds(new Rect(-50f, -40f, 100f, 80f));
        _localPlayer.transform.position = new Vector3(48f, -39f, 0f);

        _controller.SetTarget(_localPlayer.transform, followCursor: true);

        AssertCameraAt(new Vector2(34f, -31f));
    }

    [Test]
    public void SetTarget_SpectatedTeammateNearMapEdge_IsClamped()
    {
        UseMapBounds(new Rect(-50f, -40f, 100f, 80f));
        _teammate.transform.position = new Vector3(-49f, 38f, 0f);

        _controller.SetTarget(_teammate.transform);
        AssertCameraAt(new Vector2(-34f, 31f));

        _controller.UpdateCamera(1f);
        AssertCameraAt(new Vector2(-34f, 31f));
    }

    [Test]
    public void UpdateCamera_MapSmallerThanViewOnAxis_CentersOnThatAxis()
    {
        UseMapBounds(new Rect(0f, -40f, 12f, 80f));
        _localPlayer.transform.position = new Vector3(10f, 5f, 0f);

        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _controller.UpdateCamera(1f);

        AssertCameraAt(new Vector2(6f, 5f));
    }

    [Test]
    public void UpdateCamera_OrthographicSizeGrowsAtRuntime_ReClampsToBounds()
    {
        UseMapBounds(new Rect(-50f, -40f, 100f, 80f));
        _localPlayer.transform.position = new Vector3(34f, 5f, 0f);
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        AssertCameraAt(new Vector2(34f, 5f));

        EditConfig(maxOffsetDistance: 4f, followSpeed: 1000f, smoothTime: 0f, orthographicSize: 12f, safeMargin: 0.2f);
        _controller.UpdateCamera(1f);

        AssertCameraAt(new Vector2(50f - 12f * Aspect, 5f));
    }

    [Test]
    public void SetTarget_FollowCursorFalseOnSameTarget_StopsCursorOffset()
    {
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        _cursor.Viewport = new Vector2(1f, 0.5f);
        _controller.UpdateCamera(1f);

        _controller.SetTarget(_localPlayer.transform);
        _controller.UpdateCamera(1f);

        AssertCameraAt(new Vector2(10f, 5f));
    }

    [Test]
    public void SetTarget_ConfigFixedOffset_ShiftsTheFollowedCenter()
    {
        EditConfigVector("_offset", new Vector2(2f, -3f));

        _controller.SetTarget(_localPlayer.transform, followCursor: true);

        AssertCameraAt(new Vector2(12f, 2f));
    }

    [Test]
    public void UpdateCamera_ConfigFixedOffsetEditedAtRuntime_AppliesOnNextUpdate()
    {
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        AssertCameraAt(new Vector2(10f, 5f));

        EditConfigVector("_offset", new Vector2(-1.5f, 4f));
        _controller.UpdateCamera(1f);

        AssertCameraAt(new Vector2(8.5f, 9f));
    }

    [Test]
    public void SetTarget_ConfigSnapDisabled_DoesNotSnapAndEasesTowardTarget()
    {
        EditConfig(maxOffsetDistance: 4f, followSpeed: 1000f, smoothTime: 0.5f, orthographicSize: 9f, safeMargin: 0.2f);
        EditConfigBool("_snapOnTargetAssigned", false);

        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        AssertCameraAt(new Vector2(0f, 0f));

        _controller.UpdateCamera(0.016f);

        float x = _cameraObject.transform.position.x;
        Assert.That(x, Is.GreaterThan(0f));
        Assert.That(x, Is.LessThan(10f));
    }

    [Test]
    public void SetTarget_ConfigSnapEnabled_SnapsImmediately()
    {
        EditConfig(maxOffsetDistance: 4f, followSpeed: 1000f, smoothTime: 0.5f, orthographicSize: 9f, safeMargin: 0.2f);
        EditConfigBool("_snapOnTargetAssigned", true);

        _controller.SetTarget(_localPlayer.transform, followCursor: true);

        AssertCameraAt(new Vector2(10f, 5f));
    }

    [Test]
    public void SetTarget_ConfigSnapEditedAtRuntime_AppliesOnNextTargetAssignment()
    {
        EditConfigBool("_snapOnTargetAssigned", false);
        _controller.SetTarget(_localPlayer.transform, followCursor: true);
        AssertCameraAt(new Vector2(0f, 0f));

        EditConfigBool("_snapOnTargetAssigned", true);
        _controller.ClearTarget(_localPlayer.transform);
        _controller.SetTarget(_teammate.transform);

        AssertCameraAt(new Vector2(-20f, 30f));
    }

    private void EditConfigVector(string field, Vector2 value)
    {
        var configObject = new SerializedObject(_config);
        configObject.FindProperty(field).vector2Value = value;
        configObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private void EditConfigBool(string field, bool value)
    {
        var configObject = new SerializedObject(_config);
        configObject.FindProperty(field).boolValue = value;
        configObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private void UseMapBounds(Rect worldBounds)
    {
        _boundsObject = new GameObject("MapBoundsUnderTest");
        CameraMapBounds bounds = _boundsObject.AddComponent<CameraMapBounds>();
        var boundsObject = new SerializedObject(bounds);
        boundsObject.FindProperty("_worldBounds").rectValue = worldBounds;
        boundsObject.ApplyModifiedPropertiesWithoutUndo();

        var controllerObject = new SerializedObject(_controller);
        controllerObject.FindProperty("_mapBounds").objectReferenceValue = bounds;
        controllerObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private void EditConfig(float maxOffsetDistance, float followSpeed, float smoothTime, float orthographicSize, float safeMargin)
    {
        var configObject = new SerializedObject(_config);
        configObject.FindProperty("_maxOffsetDistance").floatValue = maxOffsetDistance;
        configObject.FindProperty("_followSpeed").floatValue = followSpeed;
        configObject.FindProperty("_smoothTime").floatValue = smoothTime;
        configObject.FindProperty("_orthographicSize").floatValue = orthographicSize;
        configObject.FindProperty("_safeMargin").floatValue = safeMargin;
        configObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private void EditConfigField(string field, float value)
    {
        var configObject = new SerializedObject(_config);
        configObject.FindProperty(field).floatValue = value;
        configObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private void AssertCameraAt(Vector2 expected)
    {
        Vector3 position = _cameraObject.transform.position;
        Assert.That(position.x, Is.EqualTo(expected.x).Within(Tolerance), "camera x");
        Assert.That(position.y, Is.EqualTo(expected.y).Within(Tolerance), "camera y");
    }

    private void InvokeLifecycle(string methodName)
    {
        MethodInfo method = typeof(LocalCameraController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(_controller, null);
    }

    private sealed class FakeCursorSource : ICameraCursorSource
    {
        public bool Available = true;
        public Vector2 Viewport = new(0.5f, 0.5f);

        public bool TryGetCursorViewport(Camera camera, out Vector2 viewport)
        {
            viewport = Viewport;
            return Available;
        }
    }
}

using UnityEngine;

/// <summary>
/// Per-scene world-space rectangle the gameplay camera must not show beyond.
/// Consumed by <see cref="LocalCameraController"/>; presentation only.
/// </summary>
[DisallowMultipleComponent]
public sealed class CameraMapBounds : MonoBehaviour
{
    [SerializeField]
    private Rect _worldBounds = new(-10f, -10f, 20f, 20f);

    /// <summary>World-space rectangle the camera view must stay inside.</summary>
    public Rect WorldBounds => _worldBounds;

    private void OnValidate()
    {
        _worldBounds.width = Mathf.Max(0f, _worldBounds.width);
        _worldBounds.height = Mathf.Max(0f, _worldBounds.height);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 center = new(_worldBounds.center.x, _worldBounds.center.y, 0f);
        Gizmos.DrawWireCube(center, new Vector3(_worldBounds.width, _worldBounds.height, 0f));
    }
}

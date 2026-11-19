using UnityEngine;

/// <summary>
/// Keeps the active <see cref="AudioListener"/> on the gameplay plane (world z = 0) while it follows this
/// camera's X/Y. A 2D game camera sits at z = -10, so a listener on the camera would measure every 3D sound
/// (gameplay objects live at z = 0) as 10 units away. Put this on the gameplay camera instead of an
/// AudioListener; any AudioListener left on the same object is disabled so only one stays active.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)] // After camera followers have moved the camera this frame.
public sealed class PlanarAudioListener : MonoBehaviour
{
    private const string ListenerObjectName = "Planar AudioListener";

    [SerializeField, Tooltip("World z of the gameplay plane the listener is kept on.")]
    private float _gameplayPlaneZ;

    private AudioListener _listener;

    /// <summary>The listener kept on the gameplay plane; null before the component first enables.</summary>
    public AudioListener Listener => _listener;

    private void OnEnable()
    {
        foreach (AudioListener own in GetComponents<AudioListener>())
        {
            own.enabled = false;
        }

        if (_listener == null)
        {
            var child = new GameObject(ListenerObjectName);
            child.transform.SetParent(transform, false);
            _listener = child.AddComponent<AudioListener>();
        }

        Align();
        _listener.enabled = true;
    }

    private void OnDisable()
    {
        if (_listener != null) _listener.enabled = false;
    }

    private void LateUpdate()
    {
        Align();
    }

    private void Align()
    {
        Vector3 position = transform.position;
        _listener.transform.SetPositionAndRotation(
            new Vector3(position.x, position.y, _gameplayPlaneZ), transform.rotation);
    }
}

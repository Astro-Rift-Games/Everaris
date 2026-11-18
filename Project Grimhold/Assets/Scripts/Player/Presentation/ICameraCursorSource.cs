using UnityEngine;

/// <summary>
/// Local pointer position used by the gameplay camera's cursor look-ahead.
/// Presentation boundary only; never feeds network simulation.
/// </summary>
public interface ICameraCursorSource
{
    /// <summary>
    /// Reads the pointer in <paramref name="camera"/> viewport space (0..1 inside the screen).
    /// Returns false when the cursor must not drive the camera, for example while a UI
    /// suppresses gameplay input.
    /// </summary>
    bool TryGetCursorViewport(Camera camera, out Vector2 viewport);
}

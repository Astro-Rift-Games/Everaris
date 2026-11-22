using UnityEngine;

/// <summary>
/// Stores local-only contextual prompt text for an interactable network object.
/// It has no gameplay authority and is never synchronized or registered.
/// </summary>
[DisallowMultipleComponent]
public sealed class InteractionPromptMetadata : MonoBehaviour
{
    [SerializeField]
    private string _promptText = "Interactuar";

    [SerializeField, Tooltip("Optional point the prompt hangs from. Empty uses the top of the visible sprites.")]
    private Transform _promptAnchor;

    [SerializeField, Tooltip("World-unit nudge applied to the prompt's anchor. The gizmo shows the result.")]
    private Vector2 _promptOffset;

    public string PromptText => string.IsNullOrWhiteSpace(_promptText)
        ? "Interactuar"
        : _promptText;

    /// <summary>Explicit prompt anchor, or null when the prompt should follow the sprite bounds.</summary>
    public Transform PromptAnchor => _promptAnchor;

    /// <summary>World-unit nudge added to the resolved anchor.</summary>
    public Vector2 PromptOffset => _promptOffset;

    /// <summary>
    /// World point the prompt hangs from for this object, as the HUD and the scene gizmo both see it.
    /// Measures sprites, so call it when the target changes, never per frame.
    /// </summary>
    public bool TryResolvePromptWorldAnchor(out Vector3 worldAnchor)
    {
        if (!InteractionPromptPlacement.TryResolveWorldAnchor(
                _promptAnchor,
                GetComponentsInChildren<SpriteRenderer>(),
                transform,
                out worldAnchor))
        {
            return false;
        }

        worldAnchor += (Vector3)_promptOffset;
        return true;
    }

#if UNITY_EDITOR
    private static readonly Color GizmoColor = new(1f, 0.82f, 0.25f, 1f);
    private const float GizmoHalfWidth = 0.6f;
    private const float GizmoStemHeight = 0.25f;

    /// <summary>
    /// Marks where the prompt's bottom edge will rest, so the anchor and offset can be tuned in the Scene view
    /// without entering Play mode. The on-screen gap above this line comes from the shared prompt style.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!TryResolvePromptWorldAnchor(out Vector3 anchor))
        {
            return;
        }

        Gizmos.color = GizmoColor;
        Gizmos.DrawLine(anchor + (Vector3.left * GizmoHalfWidth), anchor + (Vector3.right * GizmoHalfWidth));
        Gizmos.DrawLine(anchor, anchor + (Vector3.up * GizmoStemHeight));
        UnityEditor.Handles.color = GizmoColor;
        UnityEditor.Handles.Label(anchor + (Vector3.up * GizmoStemHeight), InteractionPromptText.Format(PromptText));
    }
#endif
}

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

    public string PromptText => string.IsNullOrWhiteSpace(_promptText)
        ? "Interactuar"
        : _promptText;

    /// <summary>Explicit prompt anchor, or null when the prompt should follow the sprite bounds.</summary>
    public Transform PromptAnchor => _promptAnchor;
}

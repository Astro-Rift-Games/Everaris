using UnityEngine;

/// <summary>
/// Shared tuning for the interaction prompt that hangs above interactables. One asset drives every HUD, so the
/// prompt is tuned in a single place for Town and Raid. Values are canvas units, so they scale with the HUD.
/// </summary>
[CreateAssetMenu(fileName = "InteractionPromptStyle", menuName = "Grimhold/UI/Interaction Prompt Style")]
public sealed class InteractionPromptStyle : ScriptableObject
{
    public const float DefaultGap = 8f;
    public const float DefaultScreenMargin = 12f;

    [SerializeField, Min(0f), Tooltip("Canvas units between the interactable's top and the prompt's bottom edge.")]
    private float _gap = DefaultGap;

    [SerializeField, Min(0f), Tooltip("Canvas units the prompt keeps from every screen edge.")]
    private float _screenMargin = DefaultScreenMargin;

    public float Gap => _gap;
    public float ScreenMargin => _screenMargin;
}

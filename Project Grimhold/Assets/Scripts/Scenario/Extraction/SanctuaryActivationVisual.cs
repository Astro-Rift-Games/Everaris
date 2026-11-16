using UnityEngine;

/// <summary>Presentation-only opacity of the On sprite over the untinted Off sprite.</summary>
public static class SanctuaryActivationVisual
{
    public static float ResolveAlpha(ExtractionRitualState state, float progress, float pulse)
    {
        switch (state)
        {
            case ExtractionRitualState.Completed:
                return 1f;
            case ExtractionRitualState.InProgress:
                return Mathf.Clamp01(progress) * Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(pulse));
            default:
                return 0f;
        }
    }
}

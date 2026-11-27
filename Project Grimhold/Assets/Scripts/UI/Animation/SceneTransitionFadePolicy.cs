/// <summary>Which local session states keep the screen covered while scenes and runners change.</summary>
public static class SceneTransitionFadePolicy
{
    public static bool ShouldCover(SessionConnectionState state)
    {
        switch (state)
        {
            case SessionConnectionState.ConnectingTown:
            case SessionConnectionState.ConnectingRaid:
            case SessionConnectionState.ReturningTown:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Player-facing Spanish status shown while the transition overlay covers the screen.</summary>
    public static string GetStatusText(SessionConnectionState state) => state switch
    {
        SessionConnectionState.ConnectingTown => "Conectando a Town",
        SessionConnectionState.ConnectingRaid => "Conectando a la incursión",
        SessionConnectionState.ReturningTown => "Volviendo a Town",
        _ => "Cargando"
    };

    /// <summary>Appends a 1..3 dot animation suffix driven by unscaled presentation time.</summary>
    public static string FormatAnimatedStatus(SessionConnectionState state, float unscaledTimeSeconds)
    {
        int dots = 1 + ((int)(UnityEngine.Mathf.Max(0f, unscaledTimeSeconds) * 2f) % 3);
        string suffix = dots == 1 ? "." : dots == 2 ? ".." : "...";
        return GetStatusText(state) + suffix;
    }
}

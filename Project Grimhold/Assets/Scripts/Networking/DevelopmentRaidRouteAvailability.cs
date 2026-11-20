/// <summary>
/// Single build gate for the development-only direct raid route (MainMenu join and
/// <see cref="DirectRaidDevelopmentStarter"/>). Available only in the Editor or Development builds;
/// Release builds compile it to a constant <c>false</c>, mirroring
/// <see cref="DevelopmentProfileIdentity.IsAvailableInThisBuild"/>.
/// </summary>
public static class DevelopmentRaidRouteAvailability
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static bool? _availabilityOverride;
#endif

    /// <summary>True when the development direct raid route may start in this build.</summary>
    public static bool IsAvailable
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return _availabilityOverride ?? true;
#else
            return false;
#endif
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// Test seam: simulates a Release build (<c>false</c>) or restores the compiled default (<c>null</c>).
    /// Not compiled into Release builds.
    /// </summary>
    internal static void SetAvailabilityOverrideForTests(bool? available) => _availabilityOverride = available;
#endif
}

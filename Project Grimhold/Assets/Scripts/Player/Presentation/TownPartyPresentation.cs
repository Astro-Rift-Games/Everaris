public readonly struct TownPartyPresentation
{
    public const string InExpeditionSuffix = " (en expedición)";

    public string LocalDisplayName { get; }
    public string CompanionDisplayName { get; }
    public bool HasCompanion { get; }
    public bool CompanionInExpedition { get; }
    public bool CanLeave { get; }

    public TownPartyPresentation(string localDisplayName, string companionDisplayName, bool hasCompanion)
        : this(localDisplayName, companionDisplayName, hasCompanion, companionInExpedition: false, canLeave: hasCompanion)
    {
    }

    public TownPartyPresentation(
        string localDisplayName,
        string companionDisplayName,
        bool hasCompanion,
        bool companionInExpedition,
        bool canLeave)
    {
        LocalDisplayName = localDisplayName ?? string.Empty;
        CompanionDisplayName = companionDisplayName ?? string.Empty;
        HasCompanion = hasCompanion;
        CompanionInExpedition = companionInExpedition;
        CanLeave = canLeave;
    }

    public static bool TryCreate(
        in TownPartySnapshot party,
        ProfileId localProfile,
        string localDisplayName,
        string companionDisplayName,
        out TownPartyPresentation presentation)
    {
        return TryCreate(party, localProfile, localDisplayName, companionDisplayName, companionInExpedition: false, out presentation);
    }

    public static bool TryCreate(
        in TownPartySnapshot party,
        ProfileId localProfile,
        string localDisplayName,
        string companionDisplayName,
        bool companionInExpedition,
        out TownPartyPresentation presentation)
    {
        presentation = default;
        if (!TownPartyRules.IsValid(party) || !party.Contains(localProfile)) return false;

        bool hasCompanion = party.Members.Count == TownPartyRules.MaxMembers;
        string companionText = string.Empty;
        if (hasCompanion)
        {
            string baseName = ResolveName(companionDisplayName, GetCompanion(party, localProfile));
            companionText = companionInExpedition ? baseName + InExpeditionSuffix : baseName;
        }

        presentation = new TownPartyPresentation(
            ResolveName(localDisplayName, localProfile),
            companionText,
            hasCompanion,
            hasCompanion && companionInExpedition,
            hasCompanion && !companionInExpedition);
        return true;
    }

    /// <summary>
    /// Creates a Town Party HUD presentation while the local player returned to Town and their Duo
    /// companion is still in the active Dungeon expedition (FIX-46).
    /// </summary>
    public static bool TryCreateAwaitingExpedition(
        TownPartyContinuationContext pendingContinuation,
        ProfileId localProfile,
        string localDisplayName,
        string companionDisplayName,
        out TownPartyPresentation presentation)
    {
        presentation = default;
        if (pendingContinuation == null || !localProfile.IsValid ||
            pendingContinuation.Members.Count != TownPartyRules.MaxMembers ||
            !pendingContinuation.Contains(localProfile))
        {
            return false;
        }

        ProfileId companion = pendingContinuation.Members[0] == localProfile
            ? pendingContinuation.Members[1]
            : pendingContinuation.Members[0];
        string baseCompanionName = ResolveName(companionDisplayName, companion);
        presentation = new TownPartyPresentation(
            ResolveName(localDisplayName, localProfile),
            baseCompanionName + InExpeditionSuffix,
            hasCompanion: true,
            companionInExpedition: true,
            canLeave: false);
        return true;
    }

    private static ProfileId GetCompanion(in TownPartySnapshot party, ProfileId localProfile) =>
        party.Members[0] == localProfile ? party.Members[1] : party.Members[0];

    private static string ResolveName(string displayName, ProfileId fallback) =>
        string.IsNullOrWhiteSpace(displayName) ? fallback.Value : displayName;
}

using System.Collections.Generic;
using Fusion;

/// <summary>
/// Outcome of resolving a coordinated Raid launch notice recipient strictly from verified identities (FIX-93).
/// </summary>
public enum TownLaunchRecipientStatus
{
    /// <summary>Recipient was resolved strictly from an identity verified by this authority peer.</summary>
    Verified,

    /// <summary>
    /// No verified binding exists yet on this authority, but an active player still claims this profile
    /// (for example mid-migration while ticket re-registration/verification is in flight). The caller
    /// must NOT send any launch notice to an unverified peer and may wait boundedly until verified or timeout.
    /// </summary>
    WaitingForVerification,

    /// <summary>No active player in Town holds or claims this profile (member disconnected).</summary>
    Disconnected
}

/// <summary>
/// Pure recipient resolver for Town coordinated launch release &amp; cancellation notices (FIX-93).
/// Guarantees that launch notices are NEVER directed to a peer whose ProfileId is only self-declared
/// (even when TownPartyDirectory is owned by a different State Authority during migration).
/// </summary>
public static class TownLaunchRecipientResolver
{
    public delegate bool TryResolveVerifiedPlayerDelegate(ProfileId profile, out PlayerRef player);
    public delegate bool TryResolveDeclaredProfileDelegate(PlayerRef player, out ProfileId profile);
    public delegate bool TryResolveVerifiedProfileDelegate(PlayerRef player, out ProfileId profile);

    public static TownLaunchRecipientStatus Resolve(
        ProfileId targetProfile,
        IEnumerable<PlayerRef> activePlayers,
        TryResolveVerifiedPlayerDelegate tryResolveVerifiedPlayer,
        TryResolveDeclaredProfileDelegate tryResolveDeclaredProfile,
        TryResolveVerifiedProfileDelegate tryResolveVerifiedProfile,
        out PlayerRef verifiedPlayer)
    {
        verifiedPlayer = PlayerRef.None;
        if (!targetProfile.IsValid || tryResolveVerifiedPlayer == null)
        {
            return TownLaunchRecipientStatus.Disconnected;
        }

        if (tryResolveVerifiedPlayer(targetProfile, out PlayerRef resolved) && !resolved.IsNone)
        {
            verifiedPlayer = resolved;
            return TownLaunchRecipientStatus.Verified;
        }

        // Never return an unverified PlayerRef; only report whether an unverified active peer
        // still claims targetProfile so the caller can wait boundedly rather than misrouting.
        if (activePlayers != null && tryResolveDeclaredProfile != null)
        {
            foreach (PlayerRef active in activePlayers)
            {
                if (active.IsNone)
                {
                    continue;
                }

                // A peer already verified as a DIFFERENT profile cannot be waiting to verify as targetProfile.
                if (tryResolveVerifiedProfile != null &&
                    tryResolveVerifiedProfile(active, out ProfileId activeVerified) &&
                    activeVerified.IsValid && activeVerified != targetProfile)
                {
                    continue;
                }

                if (tryResolveDeclaredProfile(active, out ProfileId declared) && declared == targetProfile)
                {
                    return TownLaunchRecipientStatus.WaitingForVerification;
                }
            }
        }

        return TownLaunchRecipientStatus.Disconnected;
    }
}

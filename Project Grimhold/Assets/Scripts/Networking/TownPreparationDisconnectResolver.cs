using System;
using System.Collections.Generic;
using Fusion;

/// <summary>
/// Resolves which <see cref="ProfileId"/> left a Town Raid preparation on <c>PlayerLeft</c> (FIX-92).
/// - When the disconnecting player was already verified by this authority, uses the verified mapping
///   directly (preserving normal pre-migration behavior).
/// - When the disconnecting player left after a Town authority migration before finishing ticket
///   re-registration/verification, resolves from their observed declared ProfileId ONLY IF no other
///   currently active player is verified as or declares that same ProfileId. An unverified disconnect
///   can therefore never remove or dissolve another connected player's preparation.
/// </summary>
public sealed class TownPreparationDisconnectResolver
{
    private readonly Dictionary<PlayerRef, ProfileId> _verifiedByPlayer = new();
    private readonly Dictionary<PlayerRef, ProfileId> _declaredByPlayer = new();

    public void RecordVerified(PlayerRef player, ProfileId profile)
    {
        if (!player.IsNone && profile.IsValid)
        {
            _verifiedByPlayer[player] = profile;
        }
    }

    public void RecordDeclared(PlayerRef player, ProfileId profile)
    {
        if (!player.IsNone && profile.IsValid)
        {
            _declaredByPlayer[player] = profile;
        }
    }

    public bool TryGetVerifiedProfile(PlayerRef player, out ProfileId profile) =>
        _verifiedByPlayer.TryGetValue(player, out profile);

    public bool TryFindVerifiedPlayer(ProfileId profile, out PlayerRef player)
    {
        player = PlayerRef.None;
        if (!profile.IsValid)
        {
            return false;
        }

        foreach (KeyValuePair<PlayerRef, ProfileId> pair in _verifiedByPlayer)
        {
            if (pair.Value == profile)
            {
                player = pair.Key;
                return true;
            }
        }

        return false;
    }

    public void RemoveVerified(PlayerRef player) => _verifiedByPlayer.Remove(player);

    public void ClearVerified() => _verifiedByPlayer.Clear();

    public void ClearAll()
    {
        _verifiedByPlayer.Clear();
        _declaredByPlayer.Clear();
    }

    /// <summary>
    /// Refreshes observed declared profiles for currently active players so we retain a player's
    /// declared ProfileId even if their PlayerObject is already despawned when <c>PlayerLeft</c> fires.
    /// </summary>
    public void ObserveActiveDeclaredProfiles(
        IEnumerable<PlayerRef> activePlayers,
        Func<PlayerRef, ProfileId> tryReadDeclared)
    {
        if (activePlayers == null || tryReadDeclared == null)
        {
            return;
        }

        foreach (PlayerRef active in activePlayers)
        {
            if (active.IsNone) continue;
            ProfileId declared = tryReadDeclared(active);
            if (declared.IsValid)
            {
                _declaredByPlayer[active] = declared;
            }
        }
    }

    /// <summary>
    /// Resolves the <see cref="ProfileId"/> of <paramref name="disconnectedPlayer"/> for preparation
    /// cleanup, returning false if the player is unknown or if cleaning up an unverified declared
    /// profile could affect another active connected player.
    /// </summary>
    public bool TryResolveDisconnectedProfile(
        PlayerRef disconnectedPlayer,
        IEnumerable<PlayerRef> remainingActivePlayers,
        Func<PlayerRef, ProfileId> tryResolveLiveVerified,
        Func<PlayerRef, ProfileId> tryReadLiveDeclared,
        out ProfileId disconnectedProfile)
    {
        disconnectedProfile = default;
        if (disconnectedPlayer.IsNone)
        {
            return false;
        }

        _declaredByPlayer.TryGetValue(disconnectedPlayer, out ProfileId cachedDeclared);
        _declaredByPlayer.Remove(disconnectedPlayer);

        // Path 1: Verified identity on current authority (standard behavior, unchanged).
        if (_verifiedByPlayer.Remove(disconnectedPlayer, out ProfileId verifiedProfile) && verifiedProfile.IsValid)
        {
            disconnectedProfile = verifiedProfile;
            return true;
        }

        // Path 2: Unverified post-migration disconnect fallback.
        ProfileId liveDeclared = tryReadLiveDeclared != null ? tryReadLiveDeclared(disconnectedPlayer) : default;
        ProfileId candidate = liveDeclared.IsValid ? liveDeclared : cachedDeclared;
        if (!candidate.IsValid)
        {
            return false;
        }

        // Security invariant (FIX-92): never clean up if ANY other active player is verified as
        // or declares the same ProfileId.
        if (remainingActivePlayers != null)
        {
            foreach (PlayerRef other in remainingActivePlayers)
            {
                if (other.IsNone || other == disconnectedPlayer)
                {
                    continue;
                }

                if (_verifiedByPlayer.TryGetValue(other, out ProfileId otherVerified) && otherVerified == candidate)
                {
                    return false;
                }

                if (tryResolveLiveVerified != null && tryResolveLiveVerified(other) == candidate)
                {
                    return false;
                }

                if (_declaredByPlayer.TryGetValue(other, out ProfileId otherCachedDeclared) && otherCachedDeclared == candidate)
                {
                    return false;
                }

                if (tryReadLiveDeclared != null && tryReadLiveDeclared(other) == candidate)
                {
                    return false;
                }
            }
        }

        disconnectedProfile = candidate;
        return true;
    }
}

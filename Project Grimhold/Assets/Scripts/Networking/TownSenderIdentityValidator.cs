using System;
using Fusion;
using UnityEngine;

/// <summary>
/// Classification of a Town sender identity check (FIX-59). Distinguishes in-flight async
/// verification from terminal rejections so Town RPCs can queue pending requests and suppress
/// per-tick console warning spam.
/// </summary>
public enum TownSenderResolutionStatus
{
    Verified,
    PendingVerification,
    RejectedNoTicket,
    RejectedInvalidOrForged,
    RejectedNoVerifier,
    RejectedConflict
}

/// <summary>
/// Authoritative validator for RPC senders in Town.
/// Replaces blind trust in SocialPlayerIdentity.ProfileId by verifying the player's authenticated TownSessionTicket.
/// Rejects absent, expired, foreign, or forged identity proofs before any mutation occurs.
/// </summary>
public static class TownSenderIdentityValidator
{
    public static bool TryResolveVerifiedSender(
        NetworkRunner runner,
        PlayerRef player,
        ITownSessionTicketVerifier verifier,
        out ProfileId profileId)
    {
        return TryResolveVerifiedSender(runner, player, verifier, out profileId, out _);
    }

    public static bool TryResolveVerifiedSender(
        NetworkRunner runner,
        PlayerRef player,
        ITownSessionTicketVerifier verifier,
        out ProfileId profileId,
        out TownSenderResolutionStatus status)
    {
        profileId = default;
        status = TownSenderResolutionStatus.RejectedNoTicket;
        if (runner == null || player.IsNone)
        {
            return false;
        }

        TownSenderIdentityContext context = runner.GetComponent<TownSenderIdentityContext>();
        if (context == null)
        {
            Debug.LogWarning($"[SECURITY] Town sender resolution rejected: runner has no {nameof(TownSenderIdentityContext)}.");
            status = TownSenderResolutionStatus.RejectedNoVerifier;
            return false;
        }

        if (!context.Registry.TryResolveWithStatus(player, verifier, out ProfileId bound, out status))
        {
            return false;
        }

        if (!TryResolveDeclaredProfile(runner, player, out ProfileId declared) || declared != bound)
        {
            status = TownSenderResolutionStatus.RejectedInvalidOrForged;
            return false;
        }

        profileId = bound;
        status = TownSenderResolutionStatus.Verified;
        return true;
    }

    /// <summary>Replicated, unauthenticated claim. For presentation only; never authorize with it.</summary>
    public static bool TryResolveDeclaredProfile(NetworkRunner runner, PlayerRef player, out ProfileId profileId)
    {
        profileId = default;
        NetworkObject playerObject = runner != null && !player.IsNone ? runner.GetPlayerObject(player) : null;
        if (playerObject == null || !playerObject.TryGetBehaviour(out SocialPlayerIdentity identity) ||
            identity.Object == null || identity.Object.InputAuthority != player ||
            string.IsNullOrWhiteSpace(identity.ProfileId.ToString()))
        {
            return false;
        }

        profileId = new ProfileId(identity.ProfileId.ToString());
        return true;
    }

    public static bool TryValidateSenderIdentity(
        string declaredProfile,
        string rawTicket,
        ITownSessionTicketVerifier verifier,
        string playerIdentifierForLogging,
        out ProfileId profileId)
    {
        return TryValidateSenderIdentity(
            declaredProfile,
            rawTicket,
            verifier,
            playerIdentifierForLogging,
            out profileId,
            out _,
            warningGate: null);
    }

    public static bool TryValidateSenderIdentity(
        string declaredProfile,
        string rawTicket,
        ITownSessionTicketVerifier verifier,
        string playerIdentifierForLogging,
        out ProfileId profileId,
        out TownSenderResolutionStatus status,
        TownSecurityWarningGate warningGate = null)
    {
        profileId = default;
        status = TownSenderResolutionStatus.RejectedNoTicket;
        if (string.IsNullOrWhiteSpace(declaredProfile))
        {
            status = TownSenderResolutionStatus.RejectedInvalidOrForged;
            return false;
        }

        // 1. Rejection: Absent identity proof
        if (string.IsNullOrWhiteSpace(rawTicket))
        {
            status = TownSenderResolutionStatus.RejectedNoTicket;
            LogSecurityWarning(warningGate, playerIdentifierForLogging, "no_ticket",
                $"[SECURITY] Town sender resolution rejected: {playerIdentifierForLogging} has no SessionTicket.");
            return false;
        }

        // 2. Decode structural ticket
        if (!TownSessionTicketCodec.TryDecode(rawTicket, out TownSessionTicket ticket))
        {
            status = TownSenderResolutionStatus.RejectedInvalidOrForged;
            LogSecurityWarning(warningGate, playerIdentifierForLogging, "unparseable",
                $"[SECURITY] Town sender resolution rejected: {playerIdentifierForLogging} provided an unparseable SessionTicket.");
            return false;
        }

        // 3. Rejection: Foreign proof / Spoofing attempt (declared ProfileId does not match ticket claim)
        if (ticket.ProfileId.Value != declaredProfile)
        {
            status = TownSenderResolutionStatus.RejectedInvalidOrForged;
            LogSecurityWarning(warningGate, playerIdentifierForLogging, "foreign_profile",
                $"[SECURITY] Town sender resolution rejected: {playerIdentifierForLogging} declared ProfileId '{declaredProfile}' but ticket belongs to '{ticket.ProfileId.Value}'.");
            return false;
        }

        // 4. Rejection: Wrong session scope
        if (ticket.SessionName != "Lobby-Town")
        {
            status = TownSenderResolutionStatus.RejectedInvalidOrForged;
            LogSecurityWarning(warningGate, playerIdentifierForLogging, "wrong_session",
                $"[SECURITY] Town sender resolution rejected: {playerIdentifierForLogging} ticket session '{ticket.SessionName}' is not 'Lobby-Town'.");
            return false;
        }

        // 5. Rejection: Expired ticket
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (ticket.ExpiresAt > 0 && now > ticket.ExpiresAt)
        {
            status = TownSenderResolutionStatus.RejectedInvalidOrForged;
            LogSecurityWarning(warningGate, playerIdentifierForLogging, "expired",
                $"[SECURITY] Town sender resolution rejected: {playerIdentifierForLogging} ticket has expired.");
            return false;
        }

        // 6. Claims are not authentication: a configured verifier is mandatory.
        if (verifier != null)
        {
            var task = verifier.VerifyTownTicketAsync(rawTicket);
            if (!task.IsCompleted)
            {
                status = TownSenderResolutionStatus.PendingVerification;
                LogSecurityWarning(warningGate, playerIdentifierForLogging, "pending_verification",
                    $"[SECURITY] Town sender resolution pending verification: Verifier task for {playerIdentifierForLogging} is still in progress.");
                return false;
            }

            if (task.IsFaulted || task.IsCanceled)
            {
                status = TownSenderResolutionStatus.RejectedInvalidOrForged;
                LogSecurityWarning(warningGate, playerIdentifierForLogging, "verifier_faulted",
                    $"[SECURITY] Town sender resolution rejected: Verifier task faulted or was canceled for {playerIdentifierForLogging}.");
                return false;
            }

            var result = task.Result;
            if (!result.valid || result.profileId.Value != declaredProfile)
            {
                status = TownSenderResolutionStatus.RejectedInvalidOrForged;
                LogSecurityWarning(warningGate, playerIdentifierForLogging, $"verifier_rejected:{result.error}",
                    $"[SECURITY] Town sender resolution rejected: Verifier rejected ticket for {playerIdentifierForLogging}: {result.error}");
                return false;
            }

            warningGate?.ResetSender(playerIdentifierForLogging);
            profileId = result.profileId;
            status = TownSenderResolutionStatus.Verified;
            return true;
        }

        status = TownSenderResolutionStatus.RejectedNoVerifier;
        LogSecurityWarning(warningGate, playerIdentifierForLogging, "no_verifier",
            $"[SECURITY] Town sender resolution rejected: no ticket verifier configured for {playerIdentifierForLogging}.");
        return false;
    }

    private static void LogSecurityWarning(TownSecurityWarningGate gate, string senderKey, string reasonKey, string message)
    {
        if (gate == null || gate.ShouldLog(senderKey, reasonKey))
        {
            Debug.LogWarning(message);
        }
    }
}

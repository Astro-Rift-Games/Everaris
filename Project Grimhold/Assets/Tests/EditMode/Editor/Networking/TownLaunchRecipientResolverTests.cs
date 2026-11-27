using Fusion;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// FIX-93: Coordinated launch release and cancellation notices must resolve recipients strictly from
/// identities verified by the sending authority peer, never from unverified declared ProfileIds
/// (even when TownPartyDirectory is owned by a different State Authority during migration).
/// </summary>
[TestFixture]
public sealed class TownLaunchRecipientResolverTests
{
    private static readonly PlayerRef LegitMemberA = PlayerRef.FromIndex(1);
    private static readonly PlayerRef LegitMemberB = PlayerRef.FromIndex(2);
    private static readonly PlayerRef SpooferPeer = PlayerRef.FromIndex(3);
    private static readonly ProfileId ProfileA = new ProfileId("member_a_fix93");
    private static readonly ProfileId ProfileB = new ProfileId("member_b_fix93");

    [Test]
    public void UnverifiedSpooferDeclaringMemberA_NeverResolvesAsLaunchRecipient()
    {
        // Member A has not yet completed re-verification on this authority; SpooferPeer copies ProfileA.
        var activePlayers = new[] { LegitMemberA, SpooferPeer };

        TownLaunchRecipientStatus status = TownLaunchRecipientResolver.Resolve(
            ProfileA,
            activePlayers,
            tryResolveVerifiedPlayer: (ProfileId _, out PlayerRef p) => { p = PlayerRef.None; return false; },
            tryResolveDeclaredProfile: (PlayerRef player, out ProfileId prof) =>
            {
                // Spoofer appears first or also declares ProfileA!
                if (player == SpooferPeer || player == LegitMemberA) { prof = ProfileA; return true; }
                prof = default;
                return false;
            },
            tryResolveVerifiedProfile: (PlayerRef _, out ProfileId p) => { p = default; return false; },
            out PlayerRef recipient);

        Assert.AreEqual(TownLaunchRecipientStatus.WaitingForVerification, status);
        Assert.AreEqual(PlayerRef.None, recipient,
            "Launch release/cancel MUST NEVER target an unverified or spoofing PlayerRef.");
    }

    [Test]
    public void VerifiedMemberA_ResolvesStrictlyToLegitMemberA_IgnoringSpooferDeclaringProfileA()
    {
        var activePlayers = new[] { SpooferPeer, LegitMemberA, LegitMemberB };

        TownLaunchRecipientStatus statusA = TownLaunchRecipientResolver.Resolve(
            ProfileA,
            activePlayers,
            tryResolveVerifiedPlayer: (ProfileId prof, out PlayerRef p) =>
            {
                if (prof == ProfileA) { p = LegitMemberA; return true; }
                if (prof == ProfileB) { p = LegitMemberB; return true; }
                p = PlayerRef.None;
                return false;
            },
            tryResolveDeclaredProfile: (PlayerRef player, out ProfileId prof) =>
            {
                prof = player == LegitMemberB ? ProfileB : ProfileA;
                return true;
            },
            tryResolveVerifiedProfile: (PlayerRef player, out ProfileId prof) =>
            {
                if (player == LegitMemberA) { prof = ProfileA; return true; }
                if (player == LegitMemberB) { prof = ProfileB; return true; }
                prof = default;
                return false;
            },
            out PlayerRef recipientA);

        Assert.AreEqual(TownLaunchRecipientStatus.Verified, statusA);
        Assert.AreEqual(LegitMemberA, recipientA, "Verified Member A must receive its own launch notice, never SpooferPeer.");
    }

    [Test]
    public void DisconnectedMember_WithNoActiveClaimant_ResolvesAsDisconnected()
    {
        var activePlayers = new[] { LegitMemberB };

        TownLaunchRecipientStatus status = TownLaunchRecipientResolver.Resolve(
            ProfileA,
            activePlayers,
            tryResolveVerifiedPlayer: (ProfileId _, out PlayerRef p) => { p = PlayerRef.None; return false; },
            tryResolveDeclaredProfile: (PlayerRef player, out ProfileId prof) =>
            {
                prof = player == LegitMemberB ? ProfileB : default;
                return prof.IsValid;
            },
            tryResolveVerifiedProfile: (PlayerRef player, out ProfileId prof) =>
            {
                prof = player == LegitMemberB ? ProfileB : default;
                return prof.IsValid;
            },
            out PlayerRef recipient);

        Assert.AreEqual(TownLaunchRecipientStatus.Disconnected, status);
        Assert.AreEqual(PlayerRef.None, recipient);
    }

    [Test]
    public void VerifiedOtherPeerSpoofingTargetDeclaredProfile_DoesNotMasqueradeAsPendingMember()
    {
        // Member A already disconnected; SpooferPeer is verified as ProfileB yet declares ProfileA.
        var activePlayers = new[] { SpooferPeer };

        TownLaunchRecipientStatus status = TownLaunchRecipientResolver.Resolve(
            ProfileA,
            activePlayers,
            tryResolveVerifiedPlayer: (ProfileId _, out PlayerRef p) => { p = PlayerRef.None; return false; },
            tryResolveDeclaredProfile: (PlayerRef player, out ProfileId prof) =>
            {
                prof = player == SpooferPeer ? ProfileA : default;
                return prof.IsValid;
            },
            tryResolveVerifiedProfile: (PlayerRef player, out ProfileId prof) =>
            {
                prof = player == SpooferPeer ? ProfileB : default;
                return prof.IsValid;
            },
            out PlayerRef recipient);

        Assert.AreEqual(TownLaunchRecipientStatus.Disconnected, status,
            "A peer already verified as ProfileB cannot stall launch by declaring ProfileA.");
        Assert.AreEqual(PlayerRef.None, recipient);
    }
}

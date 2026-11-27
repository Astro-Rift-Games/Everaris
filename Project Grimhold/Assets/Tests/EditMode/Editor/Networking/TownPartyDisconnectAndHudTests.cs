using Fusion;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// FIX-46: A player disconnecting in Town leaves their Party (converting Duo to Solo and cancelling
/// pending invitations), EXCEPT when in an active expedition of that Party, in which case the Party
/// preserves them and the HUD displays "(en expedición)".
/// </summary>
[TestFixture]
public sealed class TownPartyDisconnectAndHudTests
{
    private static readonly ProfileId Ana = new ProfileId("ana_fix46");
    private static readonly ProfileId Beto = new ProfileId("beto_fix46");

    [Test]
    public void TownPartyDirectory_ImplementsIPlayerLeft()
    {
        Assert.IsTrue(typeof(IPlayerLeft).IsAssignableFrom(typeof(TownPartyDirectory)),
            "TownPartyDirectory must implement IPlayerLeft so disconnects in Town leave the Party.");
    }

    [Test]
    public void DisconnectInTownWithoutExpedition_LeavesParty_AndResolvesRemainingSoloMember()
    {
        var duo = new TownPartySnapshot(partyId: 7, hostProfileId: Beto, members: new[] { Beto, Ana }, revision: 2);

        Assert.AreEqual(
            TownPartyDisconnectPolicy.DisconnectDisposition.LeavePartyAndCancelInvitations,
            TownPartyDisconnectPolicy.Evaluate(Beto, isInLaunchingExpedition: false));

        Assert.IsTrue(TownPartyDisconnectPolicy.TryResolveRemainingSoloMember(duo, Beto, out ProfileId remaining));
        Assert.AreEqual(Ana, remaining, "Remaining player must become a valid Solo Party immediately.");
    }

    [Test]
    public void DisconnectDuringActivePartyExpedition_PreservesPartyMembership()
    {
        Assert.AreEqual(
            TownPartyDisconnectPolicy.DisconnectDisposition.PreserveInExpedition,
            TownPartyDisconnectPolicy.Evaluate(Beto, isInLaunchingExpedition: true));
    }

    [Test]
    public void PartyHud_ShowsInExpeditionIndicator_WhilePartnerRemainsInDungeon()
    {
        Assert.IsTrue(TownPartyContinuationContext.TryCreate(Ana, new[] { Ana, Beto }, out var pending));

        Assert.IsTrue(TownPartyPresentation.TryCreateAwaitingExpedition(
            pending,
            localProfile: Ana,
            localDisplayName: "Ana",
            companionDisplayName: "Beto",
            out TownPartyPresentation presentation));

        Assert.IsTrue(presentation.HasCompanion);
        Assert.IsTrue(presentation.CompanionInExpedition);
        Assert.IsFalse(presentation.CanLeave);
        Assert.AreEqual("Ana", presentation.LocalDisplayName);
        Assert.AreEqual("Beto (en expedición)", presentation.CompanionDisplayName);
    }

    [Test]
    public void PartyHud_LiveDuoWithCompanionLaunchingIntoExpedition_ShowsInExpeditionSuffix()
    {
        var duo = new TownPartySnapshot(partyId: 9, hostProfileId: Ana, members: new[] { Ana, Beto }, revision: 3);

        Assert.IsTrue(TownPartyPresentation.TryCreate(
            duo,
            localProfile: Ana,
            localDisplayName: "Ana",
            companionDisplayName: "Beto",
            companionInExpedition: true,
            out TownPartyPresentation presentation));

        Assert.IsTrue(presentation.HasCompanion);
        Assert.IsTrue(presentation.CompanionInExpedition);
        Assert.AreEqual("Beto (en expedición)", presentation.CompanionDisplayName);
    }
}

#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;

public sealed class RaidStartPolicyTests
{
    private const NetworkMatchController.MatchPhase Waiting = NetworkMatchController.MatchPhase.WaitingForPlayers;
    private const NetworkMatchController.MatchPhase Starting = NetworkMatchController.MatchPhase.Starting;
    private const NetworkMatchController.MatchPhase InProgress = NetworkMatchController.MatchPhase.InProgress;
    private const NetworkMatchController.MatchPhase Closing = NetworkMatchController.MatchPhase.Closing;
    private const NetworkMatchController.MatchPhase Finished = NetworkMatchController.MatchPhase.Finished;

    [TestCase(Waiting, RaidClosureReason.NaturalCompletion, false)]
    [TestCase(Starting, RaidClosureReason.NaturalCompletion, false)]
    [TestCase(InProgress, RaidClosureReason.NaturalCompletion, true)]
    // A technical closure aborts from Starting or Waiting, so the dungeon was never entered.
    [TestCase(Closing, RaidClosureReason.BootstrapFailure, false)]
    [TestCase(Finished, RaidClosureReason.BootstrapFailure, false)]
    // Closure from InProgress means the raid had started.
    [TestCase(Closing, RaidClosureReason.NaturalCompletion, true)]
    [TestCase(Finished, RaidClosureReason.NaturalCompletion, true)]
    public void HasEffectivelyStarted_FollowsTheInProgressBoundary(
        NetworkMatchController.MatchPhase phase, RaidClosureReason reason, bool expected)
    {
        Assert.That(RaidStartPolicy.HasEffectivelyStarted(phase, reason), Is.EqualTo(expected));
    }

    [Test]
    public void ShouldReportRaidStarted_OnlyStateAuthorityOncePerRaidWithAdmittedReservation()
    {
        Assert.That(RaidStartPolicy.ShouldReportRaidStarted(true, true, false, true), Is.True);
        Assert.That(RaidStartPolicy.ShouldReportRaidStarted(false, true, false, true), Is.False, "proxy");
        Assert.That(RaidStartPolicy.ShouldReportRaidStarted(true, false, false, true), Is.False, "not started");
        Assert.That(RaidStartPolicy.ShouldReportRaidStarted(true, true, true, true), Is.False, "already reported");
        Assert.That(RaidStartPolicy.ShouldReportRaidStarted(true, true, false, false), Is.False, "no admitted reservation");
    }

    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(false, false, false)]
    [TestCase(false, true, false)]
    public void ShouldRequestPreStartRefund_RequiresAdmittedSnapshotAndNoStart(
        bool hasSnapshot, bool started, bool expected)
    {
        Assert.That(RaidStartPolicy.ShouldRequestPreStartRefund(hasSnapshot, started), Is.EqualTo(expected));
    }
}
#endif

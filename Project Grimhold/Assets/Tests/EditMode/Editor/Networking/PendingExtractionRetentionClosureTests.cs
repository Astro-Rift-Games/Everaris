#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;

public sealed class PendingExtractionRetentionClosureTests
{
    [TestCase(RaidParticipantState.Extracted, false, true, true)]
    [TestCase(RaidParticipantState.Extracted, false, false, false)]
    [TestCase(RaidParticipantState.Extracted, true, true, false)]
    [TestCase(RaidParticipantState.Extracted, true, false, false)]
    [TestCase(RaidParticipantState.Raiding, false, true, false)]
    [TestCase(RaidParticipantState.Defeated, false, true, false)]
    [TestCase(RaidParticipantState.Aborted, false, true, false)]
    public void Barrier_OnlyPendingExtractorsWhoCanStillAcknowledge(
        RaidParticipantState state, bool complete, bool canAcknowledge, bool expected)
    {
        Assert.That(NetworkSpawnManager.ShouldAwaitExtractionCommit(state, complete, canAcknowledge), Is.EqualTo(expected));
    }

    [TestCase(true, false, false, true, true)]
    [TestCase(true, true, false, true, false)]
    [TestCase(true, true, true, true, false)]
    [TestCase(false, false, false, false, false)]
    [TestCase(false, true, true, false, false)]
    public void Barrier_DeadlineNeverDiscardsPendingCommit_AndMissingTimerIsArmed(
        bool pending, bool running, bool expired, bool wait, bool arm)
    {
        Assert.That(NetworkMatchController.ShouldHoldPersistenceBarrier(pending, running, expired, out bool armTimer), Is.EqualTo(wait));
        Assert.That(armTimer, Is.EqualTo(arm));
    }
}
#endif

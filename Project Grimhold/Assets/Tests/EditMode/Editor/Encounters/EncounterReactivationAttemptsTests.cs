using NUnit.Framework;

/// <summary>TASK-330 T5 (S3): a room that cannot reactivate for a structural reason is neither retried nor logged on every cadence tick.</summary>
public sealed class EncounterReactivationAttemptsTests
{
    private static EncounterReactivationAttempts Create() => new EncounterReactivationAttempts(4);

    [Test]
    public void NewRooms_AreNotParked()
    {
        EncounterReactivationAttempts attempts = Create();
        for (int i = 0; i < 4; i++) Assert.That(attempts.IsParked(i), Is.False);
    }

    [Test]
    public void Blocked_IsNormalAndNeverLoggedOrParked()
    {
        EncounterReactivationAttempts attempts = Create();
        for (int i = 0; i < 20; i++)
        {
            Assert.That(attempts.Record(0, EncounterReactivationOutcome.Blocked), Is.False);
        }

        Assert.That(attempts.IsParked(0), Is.False);
    }

    [Test]
    public void Reactivated_IsLoggedAndClearsEarlierSpawnFailures()
    {
        EncounterReactivationAttempts attempts = Create();
        attempts.Record(0, EncounterReactivationOutcome.SpawnFailed);
        attempts.Record(0, EncounterReactivationOutcome.SpawnFailed);

        Assert.That(attempts.Record(0, EncounterReactivationOutcome.Reactivated), Is.True);
        Assert.That(attempts.Record(0, EncounterReactivationOutcome.SpawnFailed), Is.True, "The failure counter restarted, so this is a first failure again.");
        Assert.That(attempts.IsParked(0), Is.False);
    }

    [TestCase(EncounterReactivationOutcome.NoEncounter)]
    [TestCase(EncounterReactivationOutcome.InvalidComposition)]
    [TestCase(EncounterReactivationOutcome.ActivationRejected)]
    public void StructuralOutcome_ParksTheRoomAndIsLoggedOnce(EncounterReactivationOutcome outcome)
    {
        EncounterReactivationAttempts attempts = Create();

        Assert.That(attempts.Record(1, outcome), Is.True);

        Assert.That(attempts.IsParked(1), Is.True, "The same seed would decide the same again.");
        Assert.That(attempts.IsParked(0), Is.False, "Other rooms are unaffected.");
        Assert.That(attempts.Record(1, outcome), Is.False, "Not logged twice.");
    }

    private static int WaitedPasses(EncounterReactivationAttempts attempts, int room)
    {
        int waited = 0;
        while (!attempts.ShouldAttempt(room))
        {
            waited++;
            Assert.That(waited, Is.LessThanOrEqualTo(EncounterReactivationAttempts.MaxBackoffPasses), "The wait must be bounded.");
        }

        return waited;
    }

    [Test]
    public void SpawnFailures_BackOffExponentiallyUpToTheCapAndNeverPark()
    {
        EncounterReactivationAttempts attempts = Create();
        int[] expected = { 1, 2, 4, 8, 16, 32, 32, 32 };
        for (int failure = 0; failure < expected.Length; failure++)
        {
            attempts.Record(2, EncounterReactivationOutcome.SpawnFailed);

            Assert.That(attempts.IsParked(2), Is.False, $"failure {failure + 1}: a transient failure never parks the room");
            Assert.That(WaitedPasses(attempts, 2), Is.EqualTo(expected[failure]), $"passes skipped after failure {failure + 1}");
        }
    }

    [Test]
    public void SpawnFailures_AreLoggedOncePerStreakAndAgainAfterASuccess()
    {
        EncounterReactivationAttempts attempts = Create();
        int logged = 0;
        for (int i = 0; i < 10; i++)
        {
            if (attempts.Record(2, EncounterReactivationOutcome.SpawnFailed)) logged++;
            WaitedPasses(attempts, 2);
        }

        Assert.That(logged, Is.EqualTo(1));
        attempts.Record(2, EncounterReactivationOutcome.Reactivated);
        Assert.That(attempts.ShouldAttempt(2), Is.True, "A success clears the wait.");
        Assert.That(attempts.Record(2, EncounterReactivationOutcome.SpawnFailed), Is.True, "A new streak is logged again.");
        Assert.That(WaitedPasses(attempts, 2), Is.EqualTo(1), "...and starts the backoff over.");
    }

    [Test]
    public void ShouldAttempt_IsFalseForParkedRoomsAndOutOfRangeRoomsAndTrueOtherwise()
    {
        EncounterReactivationAttempts attempts = Create();
        Assert.That(attempts.ShouldAttempt(0), Is.True);
        Assert.That(attempts.ShouldAttempt(-1), Is.False);
        Assert.That(attempts.ShouldAttempt(99), Is.False);
        attempts.Record(1, EncounterReactivationOutcome.NoEncounter);
        Assert.That(attempts.ShouldAttempt(1), Is.False);
        for (int i = 0; i < 5; i++) Assert.That(attempts.Record(0, EncounterReactivationOutcome.Blocked), Is.False);
        Assert.That(attempts.ShouldAttempt(0), Is.True, "Blocked is normal and costs no wait.");
    }

    [Test]
    public void Reset_ClearsTheBackoffToo()
    {
        EncounterReactivationAttempts attempts = Create();
        attempts.Record(0, EncounterReactivationOutcome.SpawnFailed);
        attempts.Reset();
        Assert.That(attempts.ShouldAttempt(0), Is.True);
    }

    [Test]
    public void Reset_UnparksEveryRoom()
    {
        EncounterReactivationAttempts attempts = Create();
        attempts.Record(0, EncounterReactivationOutcome.NoEncounter);
        attempts.Record(3, EncounterReactivationOutcome.InvalidComposition);

        attempts.Reset();

        Assert.That(attempts.IsParked(0), Is.False);
        Assert.That(attempts.IsParked(3), Is.False);
        Assert.That(attempts.Record(0, EncounterReactivationOutcome.NoEncounter), Is.True, "Logged again after a reset.");
    }

    [Test]
    public void OutOfRangeRoom_IsIgnoredWithoutThrowing()
    {
        EncounterReactivationAttempts attempts = Create();
        Assert.That(attempts.IsParked(-1), Is.False);
        Assert.That(attempts.IsParked(99), Is.False);
        Assert.That(attempts.Record(99, EncounterReactivationOutcome.NoEncounter), Is.False);
    }
}

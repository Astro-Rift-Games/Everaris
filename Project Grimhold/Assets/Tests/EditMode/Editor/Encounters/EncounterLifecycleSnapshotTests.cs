using NUnit.Framework;

/// <summary>TASK-330 T4 (S2, S5): the lifecycle can be captured and restored, so a Host Migration keeps its rules in the domain.</summary>
public sealed class EncounterLifecycleSnapshotTests
{
    private static EncounterLifecycle Create(float cooldownSeconds = 30f, int maxReactivations = 2, int id = 7) =>
        new EncounterLifecycle(new EncounterId(id), new EncounterLifecycleSettings(cooldownSeconds, maxReactivations));

    private static EncounterLifecycleSnapshot Snapshot(
        EncounterState state, int reactivations = 0, double cooldownStart = 0d, uint[] members = null, uint[] alive = null) =>
        new EncounterLifecycleSnapshot(state, reactivations, cooldownStart, members ?? new uint[0], alive ?? new uint[0]);

    [Test]
    public void CaptureSnapshot_NewEncounter_IsNotActivatedAndEmpty()
    {
        EncounterLifecycleSnapshot snapshot = Create().CaptureSnapshot();

        Assert.That(snapshot.State, Is.EqualTo(EncounterState.NotActivated));
        Assert.That(snapshot.ReactivationCount, Is.EqualTo(0));
        Assert.That(snapshot.MemberKeys, Is.Empty);
        Assert.That(snapshot.AliveMemberKeys, Is.Empty);
    }

    [Test]
    public void CaptureSnapshot_ActiveEncounter_ListsMembersAndSurvivorsAscending()
    {
        EncounterLifecycle encounter = Create();
        encounter.TryActivate(new uint[] { 5u, 2u, 9u });
        encounter.RegisterValidElimination(2u, 10d);

        EncounterLifecycleSnapshot snapshot = encounter.CaptureSnapshot();

        Assert.That(snapshot.State, Is.EqualTo(EncounterState.Active));
        Assert.That(snapshot.MemberKeys, Is.EqualTo(new uint[] { 2u, 5u, 9u }));
        Assert.That(snapshot.AliveMemberKeys, Is.EqualTo(new uint[] { 5u, 9u }));
    }

    [Test]
    public void CaptureSnapshot_ResolvedEncounter_KeepsTheCooldownStartAndNoSurvivors()
    {
        EncounterLifecycle encounter = Create();
        encounter.TryActivate(new uint[] { 1u });
        encounter.RegisterValidElimination(1u, 12.5d);

        EncounterLifecycleSnapshot snapshot = encounter.CaptureSnapshot();

        Assert.That(snapshot.State, Is.EqualTo(EncounterState.Resolved));
        Assert.That(snapshot.CooldownStartTime, Is.EqualTo(12.5d));
        Assert.That(snapshot.MemberKeys, Is.EqualTo(new uint[] { 1u }));
        Assert.That(snapshot.AliveMemberKeys, Is.Empty);
    }

    [Test]
    public void CaptureSnapshot_AfterReactivation_CarriesTheReactivationCount()
    {
        EncounterLifecycle encounter = Create(cooldownSeconds: 1f, maxReactivations: 2);
        encounter.TryActivate(new uint[] { 1u });
        encounter.RegisterValidElimination(1u, 0d);
        encounter.Advance(5d);
        var world = new EncounterReactivationContext(false, true, true);
        Assert.That(encounter.TryReactivate(world, new uint[] { 1u, 2u }), Is.True);

        EncounterLifecycleSnapshot snapshot = encounter.CaptureSnapshot();

        Assert.That(snapshot.ReactivationCount, Is.EqualTo(1));
        Assert.That(snapshot.State, Is.EqualTo(EncounterState.Active));
    }

    [Test]
    public void TryRestore_ActiveSnapshot_ContinuesTheCycleOnTheRestoredMembers()
    {
        EncounterLifecycle source = Create();
        source.TryActivate(new uint[] { 1u, 2u, 3u });
        source.RegisterValidElimination(1u, 4d);
        EncounterLifecycle restored = Create();

        Assert.That(restored.TryRestore(source.CaptureSnapshot()), Is.True);

        Assert.That(restored.State, Is.EqualTo(EncounterState.Active));
        Assert.That(restored.AliveMemberCount, Is.EqualTo(2));
        Assert.That(restored.IsMember(1u), Is.True, "An eliminated member still belongs to the cycle.");
        Assert.That(restored.IsAliveMember(1u), Is.False);
        Assert.That(restored.RegisterValidElimination(1u, 5d), Is.False, "A restored elimination is not repeated.");
        Assert.That(restored.RegisterValidElimination(2u, 6d), Is.True);
        Assert.That(restored.RegisterValidElimination(3u, 7d), Is.True);
        Assert.That(restored.State, Is.EqualTo(EncounterState.Resolved));
        Assert.That(restored.CooldownStartTime, Is.EqualTo(7d));
    }

    [Test]
    public void TryRestore_ResolvedSnapshot_FinishesTheCooldownFromTheRestoredStart()
    {
        EncounterLifecycle restored = Create(cooldownSeconds: 30f);

        Assert.That(restored.TryRestore(Snapshot(EncounterState.Resolved, cooldownStart: 10d, members: new uint[] { 1u })), Is.True);

        restored.Advance(39.9d);
        Assert.That(restored.State, Is.EqualTo(EncounterState.Cooldown));
        restored.Advance(40d);
        Assert.That(restored.State, Is.EqualTo(EncounterState.Reactivable));
    }

    [Test]
    public void TryRestore_ReactivationCount_IsKeptAndStillBoundsReactivation()
    {
        EncounterLifecycle restored = Create(maxReactivations: 2);

        Assert.That(restored.TryRestore(Snapshot(EncounterState.Reactivable, reactivations: 2, members: new uint[] { 1u })), Is.True);

        Assert.That(restored.ReactivationCount, Is.EqualTo(2));
        Assert.That(
            restored.EvaluateReactivation(new EncounterReactivationContext(false, true, true)),
            Is.EqualTo(EncounterReactivationBlock.MaxReactivationsReached));
    }

    [Test]
    public void TryRestore_NotActivatedSnapshot_ResetsAnActiveEncounter()
    {
        EncounterLifecycle encounter = Create();
        encounter.TryActivate(new uint[] { 1u, 2u });

        Assert.That(encounter.TryRestore(Snapshot(EncounterState.NotActivated)), Is.True);

        Assert.That(encounter.State, Is.EqualTo(EncounterState.NotActivated));
        Assert.That(encounter.AliveMemberCount, Is.EqualTo(0));
        Assert.That(encounter.IsMember(1u), Is.False);
    }

    [Test]
    public void TryRestore_DoesNotShareTheSnapshotArrays()
    {
        var members = new uint[] { 1u, 2u };
        var alive = new uint[] { 1u, 2u };
        EncounterLifecycle restored = Create();
        Assert.That(restored.TryRestore(Snapshot(EncounterState.Active, members: members, alive: alive)), Is.True);

        members[0] = 99u;
        alive[0] = 99u;

        Assert.That(restored.IsAliveMember(1u), Is.True);
        Assert.That(restored.IsMember(99u), Is.False);
    }

    private static TestCaseData[] InvalidSnapshots() => new[]
    {
        new TestCaseData(Snapshot(EncounterState.Active, members: new uint[] { 1u }, alive: new uint[0])).SetName("Active_WithoutSurvivors"),
        new TestCaseData(Snapshot(EncounterState.Active, members: new uint[] { 1u }, alive: new uint[] { 2u })).SetName("Active_SurvivorNotAMember"),
        new TestCaseData(Snapshot(EncounterState.Active, members: new uint[] { 1u, 1u }, alive: new uint[] { 1u })).SetName("DuplicateMembers"),
        new TestCaseData(Snapshot(EncounterState.Active, members: new uint[] { 0u }, alive: new uint[] { 0u })).SetName("MemberKeyZero"),
        new TestCaseData(Snapshot(EncounterState.Resolved, cooldownStart: 1d, members: new uint[] { 1u }, alive: new uint[] { 1u })).SetName("Resolved_WithSurvivors"),
        new TestCaseData(Snapshot(EncounterState.Cooldown, cooldownStart: 1d, members: new uint[0])).SetName("Cooldown_WithoutMembers"),
        new TestCaseData(Snapshot(EncounterState.NotActivated, members: new uint[] { 1u })).SetName("NotActivated_WithMembers"),
        new TestCaseData(Snapshot(EncounterState.NotActivated, reactivations: 1)).SetName("NotActivated_WithReactivations"),
        new TestCaseData(Snapshot(EncounterState.Reactivable, reactivations: -1, members: new uint[] { 1u })).SetName("NegativeReactivationCount"),
        new TestCaseData(Snapshot(EncounterState.Resolved, cooldownStart: double.NaN, members: new uint[] { 1u })).SetName("NaNCooldownStart"),
        new TestCaseData(Snapshot(EncounterState.Resolved, cooldownStart: -1d, members: new uint[] { 1u })).SetName("NegativeCooldownStart"),
        new TestCaseData(Snapshot((EncounterState)99, members: new uint[] { 1u }, alive: new uint[] { 1u })).SetName("UndefinedState"),
    };

    [TestCaseSource(nameof(InvalidSnapshots))]
    public void TryRestore_InconsistentSnapshot_IsRejectedAndLeavesTheEncounterUntouched(EncounterLifecycleSnapshot invalid)
    {
        EncounterLifecycle encounter = Create();
        encounter.TryActivate(new uint[] { 1u, 2u });
        encounter.RegisterValidElimination(1u, 3d);

        Assert.That(encounter.TryRestore(invalid), Is.False);

        Assert.That(encounter.State, Is.EqualTo(EncounterState.Active));
        Assert.That(encounter.AliveMemberCount, Is.EqualTo(1));
        Assert.That(encounter.IsAliveMember(2u), Is.True);
        Assert.That(encounter.IsMember(1u), Is.True);
        Assert.That(encounter.ReactivationCount, Is.EqualTo(0));
    }
}

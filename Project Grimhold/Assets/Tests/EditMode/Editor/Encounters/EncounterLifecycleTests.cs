using System;
using NUnit.Framework;

public sealed class EncounterLifecycleTests
{
    private static readonly EncounterReactivationContext ClearWorld = new EncounterReactivationContext(
        playersInGenerationZone: false, globalCapacityAvailable: true, dungeonPhaseAllowsGeneration: true);

    private static EncounterLifecycle Create(float cooldownSeconds = 30f, int maxReactivations = 2, int id = 7)
    {
        return new EncounterLifecycle(new EncounterId(id), new EncounterLifecycleSettings(cooldownSeconds, maxReactivations));
    }

    private static EncounterLifecycle CreateResolved(double resolvedAt, float cooldownSeconds = 30f, int maxReactivations = 2)
    {
        EncounterLifecycle encounter = Create(cooldownSeconds, maxReactivations);
        Assert.That(encounter.TryActivate(new uint[] { 1u }), Is.True);
        Assert.That(encounter.RegisterValidElimination(1u, resolvedAt), Is.True);
        return encounter;
    }

    // S1 — identity and queryable state

    [Test]
    public void EncounterId_PositiveValues_AreValidAndEqualByValue()
    {
        Assert.That(new EncounterId(3).IsValid, Is.True);
        Assert.That(new EncounterId(3), Is.EqualTo(new EncounterId(3)));
        Assert.That(new EncounterId(3) == new EncounterId(4), Is.False);
        Assert.That(EncounterId.None.IsValid, Is.False);
    }

    [Test]
    public void EncounterId_NonPositiveValue_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncounterId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncounterId(-1));
    }

    [Test]
    public void NewEncounter_ExposesIdAndStartsNotActivated()
    {
        EncounterLifecycle encounter = Create(id: 12);

        Assert.That(encounter.Id, Is.EqualTo(new EncounterId(12)));
        Assert.That(encounter.State, Is.EqualTo(EncounterState.NotActivated));
        Assert.That(encounter.AliveMemberCount, Is.EqualTo(0));
        Assert.That(encounter.ReactivationCount, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_InvalidId_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new EncounterLifecycle(EncounterId.None, new EncounterLifecycleSettings(1f, 0)));
    }

    // S2 — basic cycle and resolution

    [Test]
    public void TryActivate_WithMembers_BecomesActiveAndTracksThem()
    {
        EncounterLifecycle encounter = Create();

        Assert.That(encounter.TryActivate(new uint[] { 10u, 11u }), Is.True);

        Assert.That(encounter.State, Is.EqualTo(EncounterState.Active));
        Assert.That(encounter.AliveMemberCount, Is.EqualTo(2));
        Assert.That(encounter.IsMember(10u) && encounter.IsMember(11u), Is.True);
        Assert.That(encounter.IsMember(12u), Is.False);
    }

    [TestCase(new uint[0])]
    [TestCase(new uint[] { 4u, 4u })]
    public void TryActivate_EmptyOrDuplicateMembers_IsRejectedAndStateUnchanged(uint[] members)
    {
        EncounterLifecycle encounter = Create();

        Assert.That(encounter.TryActivate(members), Is.False);

        Assert.That(encounter.State, Is.EqualTo(EncounterState.NotActivated));
        Assert.That(encounter.AliveMemberCount, Is.EqualTo(0));
        Assert.That(encounter.IsMember(4u), Is.False);
    }

    [Test]
    public void TryActivate_WhenAlreadyActive_IsRejectedAndMembersUnchanged()
    {
        EncounterLifecycle encounter = Create();
        encounter.TryActivate(new uint[] { 1u });

        Assert.That(encounter.TryActivate(new uint[] { 2u, 3u }), Is.False);

        Assert.That(encounter.AliveMemberCount, Is.EqualTo(1));
        Assert.That(encounter.IsMember(2u), Is.False);
    }

    [Test]
    public void RegisterValidElimination_NotLastMember_StaysActive()
    {
        EncounterLifecycle encounter = Create();
        encounter.TryActivate(new uint[] { 1u, 2u });

        Assert.That(encounter.RegisterValidElimination(1u, 5d), Is.True);

        Assert.That(encounter.State, Is.EqualTo(EncounterState.Active));
        Assert.That(encounter.AliveMemberCount, Is.EqualTo(1));
        Assert.That(encounter.IsMember(1u), Is.True, "An eliminated enemy still belongs to its encounter.");
        Assert.That(encounter.IsAliveMember(1u), Is.False);
    }

    [Test]
    public void RegisterValidElimination_LastMember_ResolvesAndStartsCooldownAtThatMoment()
    {
        EncounterLifecycle encounter = Create();
        encounter.TryActivate(new uint[] { 1u, 2u });
        encounter.RegisterValidElimination(1u, 5d);

        Assert.That(encounter.RegisterValidElimination(2u, 42.5d), Is.True);

        Assert.That(encounter.State, Is.EqualTo(EncounterState.Resolved));
        Assert.That(encounter.AliveMemberCount, Is.EqualTo(0));
        Assert.That(encounter.CooldownStartTime, Is.EqualTo(42.5d));
    }

    [Test]
    public void RegisterValidElimination_RepeatedOrNonMember_IsIgnored()
    {
        EncounterLifecycle encounter = Create();
        encounter.TryActivate(new uint[] { 1u, 2u });
        encounter.RegisterValidElimination(1u, 5d);

        Assert.That(encounter.RegisterValidElimination(1u, 6d), Is.False, "Repeated elimination (resimulation) must not count twice.");
        Assert.That(encounter.RegisterValidElimination(99u, 6d), Is.False, "Enemies of another encounter do not resolve this one.");

        Assert.That(encounter.State, Is.EqualTo(EncounterState.Active));
        Assert.That(encounter.AliveMemberCount, Is.EqualTo(1));
    }

    [Test]
    public void RegisterValidElimination_BeforeActivation_IsIgnored()
    {
        EncounterLifecycle encounter = Create();

        Assert.That(encounter.RegisterValidElimination(1u, 1d), Is.False);
        Assert.That(encounter.State, Is.EqualTo(EncounterState.NotActivated));
    }

    [Test]
    public void Advance_AfterResolution_EntersCooldownUntilCooldownElapsedSinceResolution()
    {
        EncounterLifecycle encounter = CreateResolved(resolvedAt: 100d, cooldownSeconds: 30f);

        encounter.Advance(100d);
        Assert.That(encounter.State, Is.EqualTo(EncounterState.Cooldown));

        encounter.Advance(129.9d);
        Assert.That(encounter.State, Is.EqualTo(EncounterState.Cooldown));

        encounter.Advance(130d);
        Assert.That(encounter.State, Is.EqualTo(EncounterState.Reactivable));
    }

    [Test]
    public void Advance_ZeroCooldown_BecomesReactivableImmediately()
    {
        EncounterLifecycle encounter = CreateResolved(resolvedAt: 10d, cooldownSeconds: 0f);

        encounter.Advance(10d);

        Assert.That(encounter.State, Is.EqualTo(EncounterState.Reactivable));
    }

    [Test]
    public void Advance_WhileNotActivatedOrActive_DoesNotChangeState()
    {
        EncounterLifecycle idle = Create();
        EncounterLifecycle active = Create();
        active.TryActivate(new uint[] { 1u });

        idle.Advance(1000d);
        active.Advance(1000d);

        Assert.That(idle.State, Is.EqualTo(EncounterState.NotActivated));
        Assert.That(active.State, Is.EqualTo(EncounterState.Active));
    }

    // S3 — reactivation only under Doc 10 conditions

    [Test]
    public void EvaluateReactivation_NotActivatedOrActive_IsNotResolved()
    {
        EncounterLifecycle idle = Create();
        EncounterLifecycle active = Create();
        active.TryActivate(new uint[] { 1u });

        Assert.That(idle.EvaluateReactivation(ClearWorld), Is.EqualTo(EncounterReactivationBlock.NotResolved));
        Assert.That(active.EvaluateReactivation(ClearWorld), Is.EqualTo(EncounterReactivationBlock.NotResolved));
    }

    [Test]
    public void EvaluateReactivation_DuringCooldown_IsCooldownPending()
    {
        EncounterLifecycle resolved = CreateResolved(resolvedAt: 0d);
        Assert.That(resolved.EvaluateReactivation(ClearWorld), Is.EqualTo(EncounterReactivationBlock.CooldownPending));

        resolved.Advance(10d);
        Assert.That(resolved.State, Is.EqualTo(EncounterState.Cooldown));
        Assert.That(resolved.EvaluateReactivation(ClearWorld), Is.EqualTo(EncounterReactivationBlock.CooldownPending));
    }

    [TestCase(true, true, true, EncounterReactivationBlock.PlayersInGenerationZone)]
    [TestCase(false, false, true, EncounterReactivationBlock.NoGlobalCapacity)]
    [TestCase(false, true, false, EncounterReactivationBlock.DungeonPhaseForbidsGeneration)]
    [TestCase(false, true, true, EncounterReactivationBlock.None)]
    public void EvaluateReactivation_AfterCooldown_RequiresEveryWorldCondition(
        bool playersInZone, bool capacity, bool phaseAllows, EncounterReactivationBlock expected)
    {
        EncounterLifecycle encounter = CreateResolved(resolvedAt: 0d, cooldownSeconds: 30f);
        encounter.Advance(30d);

        var context = new EncounterReactivationContext(playersInZone, capacity, phaseAllows);

        Assert.That(encounter.EvaluateReactivation(context), Is.EqualTo(expected));
    }

    [Test]
    public void TryReactivate_WhenAllowed_BecomesActiveWithNewMembersAndCountsReactivation()
    {
        EncounterLifecycle encounter = CreateResolved(resolvedAt: 0d, cooldownSeconds: 30f);
        encounter.Advance(30d);

        Assert.That(encounter.TryReactivate(ClearWorld, new uint[] { 20u, 21u }), Is.True);

        Assert.That(encounter.State, Is.EqualTo(EncounterState.Active));
        Assert.That(encounter.ReactivationCount, Is.EqualTo(1));
        Assert.That(encounter.AliveMemberCount, Is.EqualTo(2));
        Assert.That(encounter.IsMember(20u), Is.True);
        Assert.That(encounter.IsMember(1u), Is.False, "Members of the previous cycle no longer belong to the reactivated encounter.");
    }

    [Test]
    public void TryReactivate_WhenBlocked_IsRejectedAndStateUnchanged()
    {
        EncounterLifecycle encounter = CreateResolved(resolvedAt: 0d, cooldownSeconds: 30f);
        encounter.Advance(30d);
        var playersInside = new EncounterReactivationContext(true, true, true);

        Assert.That(encounter.TryReactivate(playersInside, new uint[] { 20u }), Is.False);

        Assert.That(encounter.State, Is.EqualTo(EncounterState.Reactivable));
        Assert.That(encounter.ReactivationCount, Is.EqualTo(0));
        Assert.That(encounter.IsMember(20u), Is.False);
    }

    [Test]
    public void TryReactivate_DuringCooldown_IsRejected()
    {
        EncounterLifecycle encounter = CreateResolved(resolvedAt: 0d, cooldownSeconds: 30f);
        encounter.Advance(29d);

        Assert.That(encounter.TryReactivate(ClearWorld, new uint[] { 20u }), Is.False);
        Assert.That(encounter.State, Is.EqualTo(EncounterState.Cooldown));
    }

    [Test]
    public void TryReactivate_EmptyMembers_IsRejectedAndStateUnchanged()
    {
        EncounterLifecycle encounter = CreateResolved(resolvedAt: 0d, cooldownSeconds: 0f);
        encounter.Advance(0d);

        Assert.That(encounter.TryReactivate(ClearWorld, new uint[0]), Is.False);

        Assert.That(encounter.State, Is.EqualTo(EncounterState.Reactivable));
        Assert.That(encounter.ReactivationCount, Is.EqualTo(0));
    }

    [Test]
    public void Reactivation_StopsAtMaxReactivations()
    {
        EncounterLifecycle encounter = CreateResolved(resolvedAt: 0d, cooldownSeconds: 0f, maxReactivations: 1);
        encounter.Advance(0d);
        Assert.That(encounter.TryReactivate(ClearWorld, new uint[] { 2u }), Is.True);
        encounter.RegisterValidElimination(2u, 5d);
        encounter.Advance(5d);

        Assert.That(encounter.State, Is.EqualTo(EncounterState.Reactivable));
        Assert.That(encounter.EvaluateReactivation(ClearWorld), Is.EqualTo(EncounterReactivationBlock.MaxReactivationsReached));
        Assert.That(encounter.TryReactivate(ClearWorld, new uint[] { 3u }), Is.False);
        Assert.That(encounter.ReactivationCount, Is.EqualTo(1));
    }

    [Test]
    public void Reactivation_ZeroMaxReactivations_NeverReactivates()
    {
        EncounterLifecycle encounter = CreateResolved(resolvedAt: 0d, cooldownSeconds: 0f, maxReactivations: 0);
        encounter.Advance(0d);

        Assert.That(encounter.EvaluateReactivation(ClearWorld), Is.EqualTo(EncounterReactivationBlock.MaxReactivationsReached));
        Assert.That(encounter.TryReactivate(ClearWorld, new uint[] { 2u }), Is.False);
    }

    [Test]
    public void Settings_NegativeValues_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncounterLifecycleSettings(-0.1f, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncounterLifecycleSettings(0f, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncounterLifecycleSettings(float.NaN, 0));
    }

    [Test]
    public void Settings_ExposeConfiguredValues()
    {
        var settings = new EncounterLifecycleSettings(45f, 3);

        Assert.That(settings.CooldownSeconds, Is.EqualTo(45f));
        Assert.That(settings.MaxReactivations, Is.EqualTo(3));
    }
}

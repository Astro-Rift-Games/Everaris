using NUnit.Framework;

/// <summary>
/// TASK-330 T5 (S3): the reactivation decision combining the world conditions with the lifecycle ones, in Doc 10 §9.2's terms.
/// </summary>
public sealed class EncounterReactivationRulesTests
{
    private static EncounterLifecycle Reactivable(int maxReactivations = 2, int alreadyReactivated = 0)
    {
        var lifecycle = new EncounterLifecycle(new EncounterId(1), new EncounterLifecycleSettings(0f, maxReactivations));
        Assert.That(lifecycle.TryActivate(new uint[] { 1u }), Is.True);
        for (int cycle = 0; cycle < alreadyReactivated; cycle++)
        {
            ResolveToReactivable(lifecycle, cycle);
            Assert.That(lifecycle.TryReactivate(Open(), new uint[] { 1u }), Is.True);
        }

        ResolveToReactivable(lifecycle, alreadyReactivated);
        Assert.That(lifecycle.State, Is.EqualTo(EncounterState.Reactivable));
        return lifecycle;
    }

    private static void ResolveToReactivable(EncounterLifecycle lifecycle, int cycle)
    {
        double now = 100d * (cycle + 1);
        Assert.That(lifecycle.RegisterValidElimination(1u, now), Is.True);
        lifecycle.Advance(now);
        lifecycle.Advance(now);
    }

    private static EncounterReactivationContext Open() => EncounterReactivationRules.BuildContext(false, 10, 3, true);

    // Capacity

    [TestCase(3, 3, true)]
    [TestCase(10, 3, true)]
    [TestCase(2, 3, false)]
    [TestCase(0, 1, false)]
    [TestCase(5, 0, false)]
    [TestCase(-1, 1, false)]
    public void CapacityFits_RequiresTheWholePlannedCompositionToFit(int available, int planned, bool expected)
    {
        Assert.That(EncounterReactivationRules.CapacityFits(available, planned), Is.EqualTo(expected));
    }

    // Context

    [Test]
    public void BuildContext_MapsEachMeasurementToItsCondition()
    {
        EncounterReactivationContext open = EncounterReactivationRules.BuildContext(false, 5, 5, true);
        Assert.That(open.PlayersInGenerationZone, Is.False);
        Assert.That(open.GlobalCapacityAvailable, Is.True);
        Assert.That(open.DungeonPhaseAllowsGeneration, Is.True);

        EncounterReactivationContext blocked = EncounterReactivationRules.BuildContext(true, 4, 5, false);
        Assert.That(blocked.PlayersInGenerationZone, Is.True);
        Assert.That(blocked.GlobalCapacityAvailable, Is.False);
        Assert.That(blocked.DungeonPhaseAllowsGeneration, Is.False);
    }

    // Decision

    [Test]
    public void Decision_AllConditionsMet_Reactivates()
    {
        Assert.That(Reactivable().EvaluateReactivation(EncounterReactivationRules.BuildContext(false, 3, 3, true)), Is.EqualTo(EncounterReactivationBlock.None));
    }

    [Test]
    public void Decision_PlayerInTheZone_Blocks()
    {
        Assert.That(Reactivable().EvaluateReactivation(EncounterReactivationRules.BuildContext(true, 3, 3, true)), Is.EqualTo(EncounterReactivationBlock.PlayersInGenerationZone));
    }

    [Test]
    public void Decision_PlannedCompositionDoesNotFit_BlocksForCapacity()
    {
        Assert.That(Reactivable().EvaluateReactivation(EncounterReactivationRules.BuildContext(false, 2, 3, true)), Is.EqualTo(EncounterReactivationBlock.NoGlobalCapacity));
    }

    [Test]
    public void Decision_PhaseForbidsGeneration_Blocks()
    {
        Assert.That(Reactivable().EvaluateReactivation(EncounterReactivationRules.BuildContext(false, 3, 3, false)), Is.EqualTo(EncounterReactivationBlock.DungeonPhaseForbidsGeneration));
    }

    [Test]
    public void Decision_MaxReactivationsReached_BlocksEvenWhenTheWorldAllows()
    {
        EncounterLifecycle exhausted = Reactivable(maxReactivations: 1, alreadyReactivated: 1);
        Assert.That(exhausted.EvaluateReactivation(Open()), Is.EqualTo(EncounterReactivationBlock.MaxReactivationsReached));
    }

    [Test]
    public void Decision_MaxReactivationsZero_NeverReactivates()
    {
        Assert.That(Reactivable(maxReactivations: 0).EvaluateReactivation(Open()), Is.EqualTo(EncounterReactivationBlock.MaxReactivationsReached));
    }

    [Test]
    public void Decision_ActiveEncounter_IsNotResolved()
    {
        var lifecycle = new EncounterLifecycle(new EncounterId(1), new EncounterLifecycleSettings(0f, 1));
        lifecycle.TryActivate(new uint[] { 1u });
        Assert.That(lifecycle.EvaluateReactivation(Open()), Is.EqualTo(EncounterReactivationBlock.NotResolved));
    }
}

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spawning;

public sealed class EncounterSpawnAssignmentTests
{
    private const ulong SessionSeed = 0x0123456789ABCDEFUL;

    private static readonly EnemySpawnCandidate<string>[] Candidates =
    {
        new EnemySpawnCandidate<string>("slime", "slime", 45, 1),
        new EnemySpawnCandidate<string>("skeleton", "skeleton", 45, 1),
        new EnemySpawnCandidate<string>("specter", "specter", 10, 2, "catacombs"),
    };

    private static List<EnemySpawnCandidate<string>> Planned(int count)
    {
        var planned = new List<EnemySpawnCandidate<string>>();
        for (int i = 0; i < count; i++) planned.Add(Candidates[i % Candidates.Length]);
        return planned;
    }

    private static EncounterCompositionSpec Spec(float chance = 1f, int min = 1, int max = 4) =>
        new EncounterCompositionSpec(chance, min, max, 0, null, null);

    // Assigner

    [Test]
    public void TryAssign_BindsEachPlannedEnemyToADistinctInRangeSpawnPoint()
    {
        var result = new List<EncounterSpawnAssignment<string>>();
        Assert.That(EncounterSpawnAssigner.TryAssign(Planned(5), 8, new System.Random(1), result), Is.True);
        Assert.That(result, Has.Count.EqualTo(5));
        Assert.That(result.Select(a => a.SpawnPointIndex).Distinct().Count(), Is.EqualTo(5));
        Assert.That(result.Select(a => a.SpawnPointIndex), Has.All.InRange(0, 7));
    }

    [Test]
    public void TryAssign_MemberKeysAreUniqueNonZeroAndFollowPlanOrder()
    {
        var result = new List<EncounterSpawnAssignment<string>>();
        List<EnemySpawnCandidate<string>> planned = Planned(6);
        Assert.That(EncounterSpawnAssigner.TryAssign(planned, 6, new System.Random(2), result), Is.True);
        Assert.That(result.Select(a => a.MemberKey), Is.EqualTo(new uint[] { 1, 2, 3, 4, 5, 6 }));
        Assert.That(result.Select(a => a.Candidate.EnemyId), Is.EqualTo(planned.Select(c => c.EnemyId)));
    }

    [Test]
    public void TryAssign_MoreEnemiesThanSpawnPoints_RejectsAndLeavesNoAssignments()
    {
        var result = new List<EncounterSpawnAssignment<string>> { new EncounterSpawnAssignment<string>(Candidates[0], 0, 1) };
        Assert.That(EncounterSpawnAssigner.TryAssign(Planned(4), 3, new System.Random(1), result), Is.False);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void TryAssign_SameSeed_ProducesSameAssignment()
    {
        var first = new List<EncounterSpawnAssignment<string>>();
        var second = new List<EncounterSpawnAssignment<string>>();
        EncounterSpawnAssigner.TryAssign(Planned(4), 9, new System.Random(77), first);
        EncounterSpawnAssigner.TryAssign(Planned(4), 9, new System.Random(77), second);
        Assert.That(second.Select(a => a.SpawnPointIndex), Is.EqualTo(first.Select(a => a.SpawnPointIndex)));
    }

    [Test]
    public void TryAssign_DifferentSeeds_DoNotAlwaysPickTheSamePoints()
    {
        var distinctLayouts = new HashSet<string>();
        for (int seed = 0; seed < 20; seed++)
        {
            var result = new List<EncounterSpawnAssignment<string>>();
            EncounterSpawnAssigner.TryAssign(Planned(3), 8, new System.Random(seed), result);
            distinctLayouts.Add(string.Join(",", result.Select(a => a.SpawnPointIndex)));
        }

        Assert.That(distinctLayouts.Count, Is.GreaterThan(1));
    }

    [Test]
    public void TryAssign_EmptyPlan_SucceedsWithNoAssignments()
    {
        var result = new List<EncounterSpawnAssignment<string>>();
        Assert.That(EncounterSpawnAssigner.TryAssign(Planned(0), 0, new System.Random(1), result), Is.True);
        Assert.That(result, Is.Empty);
    }

    // Bootstrap planner (seed + composition + assignment)

    private static EncounterPlanResult Plan(EncounterCompositionSpec spec, int pointCount, int encounterId, int generation, out List<EncounterSpawnAssignment<string>> result)
    {
        result = new List<EncounterSpawnAssignment<string>>();
        return EncounterBootstrapPlanner.Plan(Candidates, "catacombs", spec, pointCount, SessionSeed, generation, new EncounterId(encounterId), result);
    }

    [Test]
    public void Plan_Planned_AssignsEachEnemyToADistinctRoomSpawnPointWithMemberKeys()
    {
        Assert.That(Plan(Spec(min: 2, max: 4), 6, 3, 1, out var result), Is.EqualTo(EncounterPlanResult.Planned));
        Assert.That(result.Count, Is.InRange(2, 4));
        Assert.That(result.Select(a => a.SpawnPointIndex).Distinct().Count(), Is.EqualTo(result.Count));
        Assert.That(result.Select(a => a.SpawnPointIndex), Has.All.InRange(0, 5));
        Assert.That(result.Select(a => a.MemberKey).Distinct().Count(), Is.EqualTo(result.Count));
        Assert.That(result.Select(a => a.MemberKey), Has.All.GreaterThan(0u));
    }

    [Test]
    public void Plan_SameSeedInputs_AreDeterministic()
    {
        Plan(Spec(min: 1, max: 6), 8, 3, 1, out var first);
        Plan(Spec(min: 1, max: 6), 8, 3, 1, out var second);
        Assert.That(second.Select(a => (a.Candidate.EnemyId, a.SpawnPointIndex, a.MemberKey)),
            Is.EqualTo(first.Select(a => (a.Candidate.EnemyId, a.SpawnPointIndex, a.MemberKey))));
    }

    [Test]
    public void Plan_UsesTheRoomSeedFromSessionGenerationAndEncounterId()
    {
        var expected = new List<EncounterSpawnAssignment<string>>();
        System.Random random = EncounterSeedRules.CreateRandom(SessionSeed, 4, new EncounterId(11));
        var scratch = new List<EnemySpawnCandidate<string>>();
        EncounterCompositionPlanner.Plan(Candidates, "catacombs", Spec(min: 1, max: 6), 8, random, scratch);
        EncounterSpawnAssigner.TryAssign(scratch, 8, random, expected);

        Plan(Spec(min: 1, max: 6), 8, 11, 4, out var actual);

        Assert.That(actual.Select(a => (a.Candidate.EnemyId, a.SpawnPointIndex, a.MemberKey)),
            Is.EqualTo(expected.Select(a => (a.Candidate.EnemyId, a.SpawnPointIndex, a.MemberKey))));
    }

    [Test]
    public void Plan_NoEncounter_SpawnsNothing()
    {
        Assert.That(Plan(Spec(chance: 0f), 6, 3, 1, out var result), Is.EqualTo(EncounterPlanResult.NoEncounter));
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Plan_Invalid_SpawnsNothing()
    {
        Assert.That(Plan(Spec(min: 5, max: 6), 2, 3, 1, out var result), Is.EqualTo(EncounterPlanResult.Invalid));
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Plan_ClearsPreviousAssignments()
    {
        var result = new List<EncounterSpawnAssignment<string>> { new EncounterSpawnAssignment<string>(Candidates[0], 0, 9) };
        EncounterBootstrapPlanner.Plan(Candidates, "catacombs", Spec(chance: 0f), 4, SessionSeed, 1, new EncounterId(1), result);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Plan_RejectsNoneEncounterId()
    {
        Assert.Throws<System.ArgumentException>(() =>
            EncounterBootstrapPlanner.Plan(Candidates, "catacombs", Spec(), 4, SessionSeed, 1, EncounterId.None, new List<EncounterSpawnAssignment<string>>()));
    }


    // Reactivation cycles (TASK-330 T5, S3)

    private static EncounterPlanResult PlanCycle(int cycle, out List<EncounterSpawnAssignment<string>> result)
    {
        result = new List<EncounterSpawnAssignment<string>>();
        return EncounterBootstrapPlanner.Plan(Candidates, "catacombs", Spec(min: 3, max: 6), 8, SessionSeed, 1, new EncounterId(3), result, cycle);
    }

    private static string Signature(List<EncounterSpawnAssignment<string>> plan) =>
        string.Join(",", plan.Select(a => a.Candidate.EnemyId + "@" + a.SpawnPointIndex));

    [Test]
    public void Plan_CycleZero_IsTheInitialPlanUnchanged()
    {
        Plan(Spec(min: 3, max: 6), 8, 3, 1, out var initial);
        PlanCycle(0, out var cycleZero);
        Assert.That(Signature(cycleZero), Is.EqualTo(Signature(initial)));
    }

    [Test]
    public void Plan_SameCycle_IsReproducible()
    {
        PlanCycle(2, out var first);
        PlanCycle(2, out var second);
        Assert.That(Signature(second), Is.EqualTo(Signature(first)));
    }

    [Test]
    public void Plan_EachCycleDrawsItsOwnComposition()
    {
        var signatures = new HashSet<string>();
        for (int cycle = 0; cycle < 12; cycle++)
        {
            Assert.That(PlanCycle(cycle, out var plan), Is.EqualTo(EncounterPlanResult.Planned));
            signatures.Add(Signature(plan));
        }

        Assert.That(signatures.Count, Is.GreaterThan(1), "Reactivations must not repeat the first composition forever.");
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spawning;
using UnityEditor;
using UnityEngine;

public sealed class EncounterCompositionPlannerTests
{
    private readonly List<EncounterProfile> _profiles = new List<EncounterProfile>();

    private static readonly EnemySpawnCandidate<string>[] Candidates =
    {
        new EnemySpawnCandidate<string>("slime", "slime", 45, 1),
        new EnemySpawnCandidate<string>("skeleton", "skeleton", 45, 1),
        new EnemySpawnCandidate<string>("specter", "specter", 10, 2, "catacombs"),
    };

    [TearDown]
    public void TearDown()
    {
        foreach (EncounterProfile profile in _profiles)
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }

        _profiles.Clear();
    }

    private EncounterProfile Profile(params (string id, float multiplier)[] modifiers)
    {
        var profile = ScriptableObject.CreateInstance<EncounterProfile>();
        _profiles.Add(profile);
        var serialized = new SerializedObject(profile);
        SerializedProperty list = serialized.FindProperty("_weightModifiers");
        list.arraySize = modifiers.Length;
        for (int i = 0; i < modifiers.Length; i++)
        {
            SerializedProperty element = list.GetArrayElementAtIndex(i);
            element.FindPropertyRelative(nameof(EncounterProfileWeightModifier.EnemyId)).stringValue = modifiers[i].id;
            element.FindPropertyRelative(nameof(EncounterProfileWeightModifier.WeightMultiplier)).floatValue = modifiers[i].multiplier;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        return profile;
    }

    private static EncounterCompositionSpec Spec(float chance = 1f, int min = 1, int max = 4, int budget = 0, string[] allowed = null, EncounterProfile profile = null)
    {
        return new EncounterCompositionSpec(chance, min, max, budget, allowed, profile);
    }

    private static EncounterPlanResult Plan(EncounterCompositionSpec spec, out List<EnemySpawnCandidate<string>> result, int capacity = 10, int seed = 1234, string environment = "catacombs")
    {
        result = new List<EnemySpawnCandidate<string>>();
        return EncounterCompositionPlanner.Plan(Candidates, environment, spec, capacity, new System.Random(seed), result);
    }

    // Encounter Profile weights

    [Test]
    public void Profile_WithoutModifierForArchetype_KeepsBaseWeight()
    {
        EncounterProfile profile = Profile(("slime", 2f));

        Assert.That(profile.ApplyWeight("skeleton", 45), Is.EqualTo(45));
    }

    [Test]
    public void Profile_Modifier_MultipliesWeightAndZeroRemovesArchetype()
    {
        EncounterProfile profile = Profile(("slime", 2f), ("skeleton", 0f));

        Assert.That(profile.ApplyWeight("slime", 45), Is.EqualTo(90));
        Assert.That(profile.ApplyWeight("skeleton", 45), Is.EqualTo(0));
    }

    [Test]
    public void Profile_PositiveMultiplier_NeverRoundsArchetypeOut()
    {
        EncounterProfile profile = Profile(("slime", 0.001f));

        Assert.That(profile.ApplyWeight("slime", 45), Is.EqualTo(1));
    }

    [Test]
    public void Profile_ArchetypeIdMatch_IsCaseInsensitive()
    {
        EncounterProfile profile = Profile(("Slime", 3f));

        Assert.That(profile.ApplyWeight("slime", 10), Is.EqualTo(30));
    }

    // Initial weighted validation

    [Test]
    public void Plan_ZeroEncounterChance_HasNoEncounter()
    {
        Assert.That(Plan(Spec(chance: 0f), out var result), Is.EqualTo(EncounterPlanResult.NoEncounter));
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Plan_FullEncounterChance_AlwaysPlans()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            Assert.That(Plan(Spec(chance: 1f), out _, seed: seed), Is.EqualTo(EncounterPlanResult.Planned), $"seed {seed}");
        }
    }

    [Test]
    public void Plan_PartialEncounterChance_SometimesHasNoEncounter()
    {
        int planned = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            if (Plan(Spec(chance: 0.5f), out _, seed: seed) == EncounterPlanResult.Planned)
            {
                planned++;
            }
        }

        Assert.That(planned, Is.InRange(60, 140));
    }

    // Composition limits

    [Test]
    public void Plan_WithoutThreatBudget_FillsMaxEnemies()
    {
        Assert.That(Plan(Spec(min: 2, max: 4), out var result), Is.EqualTo(EncounterPlanResult.Planned));
        Assert.That(result, Has.Count.EqualTo(4));
    }

    [Test]
    public void Plan_ThreatBudget_LimitsCompositionCost()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            Assert.That(Plan(Spec(min: 1, max: 10, budget: 3), out var result, seed: seed), Is.EqualTo(EncounterPlanResult.Planned));
            Assert.That(result.Sum(candidate => candidate.ThreatCost), Is.LessThanOrEqualTo(3), $"seed {seed}");
        }
    }

    [Test]
    public void Plan_AllowedEnemies_RestrictsArchetypes()
    {
        Assert.That(Plan(Spec(max: 8, allowed: new[] { "skeleton" }), out var result), Is.EqualTo(EncounterPlanResult.Planned));
        Assert.That(result.Select(candidate => candidate.EnemyId), Is.All.EqualTo("skeleton"));
    }

    [Test]
    public void Plan_EnvironmentRestriction_StillApplies()
    {
        Assert.That(Plan(Spec(max: 8, allowed: new[] { "specter" }), out var result, environment: "sewers"), Is.EqualTo(EncounterPlanResult.Invalid));
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Plan_SpatialCapacity_CapsMaxEnemies()
    {
        Assert.That(Plan(Spec(min: 1, max: 8), out var result, capacity: 3), Is.EqualTo(EncounterPlanResult.Planned));
        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public void Plan_MinEnemiesAboveSpatialCapacity_IsInvalidAndEmpty()
    {
        Assert.That(Plan(Spec(min: 4, max: 8), out var result, capacity: 3), Is.EqualTo(EncounterPlanResult.Invalid));
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Plan_MinEnemiesUnreachableWithinBudget_IsInvalidAndEmpty()
    {
        Assert.That(Plan(Spec(min: 3, max: 8, budget: 2), out var result), Is.EqualTo(EncounterPlanResult.Invalid));
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Plan_MinAboveMax_IsInvalid()
    {
        Assert.That(Plan(Spec(min: 5, max: 2), out var result), Is.EqualTo(EncounterPlanResult.Invalid));
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Plan_ClearsPreviousResultContents()
    {
        var result = new List<EnemySpawnCandidate<string>> { Candidates[0] };

        EncounterCompositionPlanner.Plan(Candidates, "catacombs", Spec(chance: 0f), 10, new System.Random(1), result);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Plan_SameSeed_ProducesSameComposition()
    {
        Plan(Spec(max: 6), out var first, seed: 99);
        Plan(Spec(max: 6), out var second, seed: 99);

        Assert.That(second.Select(c => c.EnemyId), Is.EqualTo(first.Select(c => c.EnemyId)));
    }

    [Test]
    public void Plan_NullRandom_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            EncounterCompositionPlanner.Plan(Candidates, "catacombs", Spec(), 10, null, new List<EnemySpawnCandidate<string>>()));
    }

    // S4 — changing the profile changes the composition without code

    [Test]
    public void Plan_ChangingOnlyTheProfile_ChangesComposition()
    {
        EncounterProfile skeletonsOnly = Profile(("slime", 0f), ("specter", 0f));
        EncounterProfile slimesOnly = Profile(("skeleton", 0f), ("specter", 0f));

        Plan(Spec(max: 6, profile: skeletonsOnly), out var withSkeletonProfile, seed: 7);
        Plan(Spec(max: 6, profile: slimesOnly), out var withSlimeProfile, seed: 7);

        Assert.That(withSkeletonProfile, Has.Count.EqualTo(6));
        Assert.That(withSkeletonProfile.Select(c => c.EnemyId), Is.All.EqualTo("skeleton"));
        Assert.That(withSlimeProfile, Has.Count.EqualTo(6));
        Assert.That(withSlimeProfile.Select(c => c.EnemyId), Is.All.EqualTo("slime"));
    }

    [Test]
    public void Plan_NeutralProfile_MatchesNoProfile()
    {
        EncounterProfile neutral = Profile();

        Plan(Spec(max: 6), out var withoutProfile, seed: 21);
        Plan(Spec(max: 6, profile: neutral), out var withNeutralProfile, seed: 21);

        Assert.That(withNeutralProfile.Select(c => c.EnemyId), Is.EqualTo(withoutProfile.Select(c => c.EnemyId)));
    }

    [Test]
    public void Plan_DoesNotMutateSharedCandidates()
    {
        EncounterProfile profile = Profile(("slime", 5f));

        Plan(Spec(max: 6, profile: profile), out _);

        Assert.That(Candidates[0].Weight, Is.EqualTo(45));
    }
}

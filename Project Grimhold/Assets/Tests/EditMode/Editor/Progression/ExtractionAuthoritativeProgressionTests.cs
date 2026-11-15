#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;

public sealed class ExtractionAuthoritativeProgressionTests
{
    [Test]
    public void PreparedProjection_AddsNoWovenStateToAvatarOrResolver()
    {
        var avatar = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Prefabs/NetworkPlayer.prefab");
        var participant = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Prefabs/NetworkRaidParticipant.prefab");
        Assert.That(Fusion.NetworkBehaviourUtils.GetWordCount(avatar.GetComponent<PlayerExtractionLootSaver>()), Is.EqualTo(1),
            "The saver retains only its original retry timer; preparation must reuse resolver storage.");
        Assert.That(Fusion.NetworkBehaviourUtils.GetWordCount(participant.GetComponent<PlayerExpeditionProgressionResolver>()), Is.EqualTo(16),
            "Preparation must not increase the resolver's original woven layout either.");
    }

    [Test]
    public void PreparationAndFinalization_UseIdenticalLedgerCandidateAndBaseline()
    {
        var baseline = new ExpeditionProgressionBaseline(1, 90);
        var ledger = new ExpeditionExperienceSnapshot(40, 20, 10, 0);
        Assert.That(ExpeditionExperienceRules.TryApplyExtractedLootReward(ledger, 30, out var prepared, out _), Is.True);
        Assert.That(PlayerExpeditionProgressionResolver.TryCalculateProgression(prepared,
            ExpeditionExperienceResolutionOutcome.Extracted, baseline, out var publishResolution, out var publishApplication), Is.True);
        // ACK applies the same candidate once; the resolver consumes that confirmed ledger.
        Assert.That(ExpeditionExperienceRules.TryApplyExtractedLootReward(ledger, 30, out var confirmed, out _), Is.True);
        Assert.That(PlayerExpeditionProgressionResolver.TryCalculateProgression(confirmed,
            ExpeditionExperienceResolutionOutcome.Extracted, baseline, out var finalResolution, out var finalApplication), Is.True);
        Assert.That(finalResolution.ConsolidatedExperience, Is.EqualTo(publishResolution.ConsolidatedExperience));
        Assert.That(finalApplication.Result.ResultingLevel, Is.EqualTo(publishApplication.Result.ResultingLevel));
        Assert.That(finalApplication.Result.ResultingExperience, Is.EqualTo(publishApplication.Result.ResultingExperience));
    }

    [TestCase(0, 0)]
    [TestCase(1, -1)]
    public void InvalidBaseline_FailsClosedWithoutStoreFallback(int level, long experience)
    {
        Assert.That(PlayerExpeditionProgressionResolver.TryCalculateProgression(
            new ExpeditionExperienceSnapshot(40, 20, 10, 30), ExpeditionExperienceResolutionOutcome.Extracted,
            new ExpeditionProgressionBaseline(level, experience), out _, out _), Is.False);
    }

    [TestCase(0, 0, 0, 0, 0, 1, 0)]
    [TestCase(40, 20, 10, 30, 100, 2, 0)]
    [TestCase(40, 20, 10, 0, 70, 1, 70)]
    public void AuthorityCalculation_PreservesNormalAwards_AndClientPersistsExactProjection(
        long kills, long assists, long exploration, long loot, long expectedXp, int level, long experience)
    {
        Assert.That(PlayerExpeditionProgressionResolver.TryCalculateProgression(
            new ExpeditionExperienceSnapshot(kills, assists, exploration, loot),
            ExpeditionExperienceResolutionOutcome.Extracted, new ExpeditionProgressionBaseline(1, 0),
            out var resolution, out var application), Is.True);
        Assert.That(resolution.ConsolidatedExperience, Is.EqualTo(expectedXp));
        Assert.That(application.Result.ResultingLevel, Is.EqualTo(level));
        Assert.That(application.Result.ResultingExperience, Is.EqualTo(experience));

        var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
            "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
        var profile = new ProfileId("authoritative_projection");
        var repository = new InMemoryLocalProfileRepository();
        Assert.That(repository.Initialize(profile, catalog), Is.True);
        var snapshot = new LocalProfileSnapshot { ProfileId = profile, Level = 3, CurrentExperience = 5 };
        Assert.That(repository.TrySave(snapshot, out _), Is.True);
        var store = new LocalProfileStore(repository, profile, catalog);
        Assert.That(store.TryCommitExtraction(new ExtractionReceipt("raid_projection", profile, 1),
            Array.Empty<StashItem>(), default, resolution.ConsolidatedExperience,
            application.Result.ResultingLevel, application.Result.ResultingExperience), Is.EqualTo(StashOperationResult.Success));
        Assert.That(store.PendingExtractionCommit.ConsolidatedExperience, Is.EqualTo(expectedXp));
        Assert.That(store.GetLevel(), Is.EqualTo(level));
        Assert.That(store.GetCurrentExperience(), Is.EqualTo(experience));
    }
}
#endif

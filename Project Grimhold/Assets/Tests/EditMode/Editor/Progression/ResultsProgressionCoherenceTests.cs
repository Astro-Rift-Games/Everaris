#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;

public sealed class ResultsProgressionCoherenceTests
{
    [Test]
    public void ExpeditionProgressionResult_WithPersistedProgression_CalculatesLevelsGainedAndUpdatesProjection()
    {
        var resolution = new ExpeditionExperienceResolution(
            ExpeditionExperienceResolutionOutcome.Extracted,
            new ExpeditionExperienceSnapshot(100, 50, 20, 30),
            10000,
            200);

        var application = new ExperienceApplicationResult(
            previousLevel: 1,
            previousExperience: 0,
            resultingLevel: 2,
            resultingExperience: 100,
            levelsGained: 1);

        var initial = new ExpeditionProgressionResult(
            resolution,
            application,
            pveKillCount: 5,
            pvpKillCount: 1,
            pveAssistCount: 2,
            pvpAssistCount: 0,
            firstOpenChestCount: 3,
            eligibleExtractedLootValue: 500,
            nextLevelExperienceRequirement: 200,
            isMaxLevel: false);

        ExperienceCurve curve = ProgressionBalanceDefaults.InitialExperienceCurve;

        // When backend/store persists level 3 with 50 XP
        ExpeditionProgressionResult adjusted = initial.WithPersistedProgression(3, 50, curve);

        Assert.That(adjusted.ResultingLevel, Is.EqualTo(3));
        Assert.That(adjusted.ResultingExperience, Is.EqualTo(50));
        Assert.That(adjusted.LevelsGained, Is.EqualTo(2), "LevelsGained must reflect difference from PreviousLevel (3 - 1 = 2).");
        Assert.That(adjusted.PreviousLevel, Is.EqualTo(1));
        Assert.That(adjusted.PveKillCount, Is.EqualTo(5), "Activity counts must be preserved.");
        Assert.That(adjusted.PvpKillCount, Is.EqualTo(1));
        Assert.That(adjusted.FirstOpenChestCount, Is.EqualTo(3));
        Assert.That(adjusted.EligibleExtractedLootValue, Is.EqualTo(500));

        // When values are identical, returns unchanged instance
        ExpeditionProgressionResult identical = adjusted.WithPersistedProgression(3, 50, curve);
        Assert.That(identical, Is.EqualTo(adjusted));
    }

    [Test]
    public void ExtractionLootSaver_CalculatesConsolidatedExperienceWithFullExpeditionLedger()
    {
        Assert.That(PlayerExpeditionProgressionResolver.TryCalculateProgression(
            new ExpeditionExperienceSnapshot(100, 50, 20, 30),
            ExpeditionExperienceResolutionOutcome.Extracted,
            new ExpeditionProgressionBaseline(1, 0), out var resolution, out var application), Is.True);
        Assert.That(resolution.ConsolidatedExperience, Is.EqualTo(200));
        Assert.That(application.Result.ResultingLevel, Is.EqualTo(2));
        Assert.That(application.Result.ResultingExperience, Is.EqualTo(100));
    }

    [TestCase(Grimhold.Backend.BackendCommitStatus.None, 10, false)]
    [TestCase(Grimhold.Backend.BackendCommitStatus.Saving, 10, false)]
    [TestCase(Grimhold.Backend.BackendCommitStatus.Saved, 10, true)]
    [TestCase(Grimhold.Backend.BackendCommitStatus.Saved, 0, false)]
    public void RaidMenuPresenter_OnlyConfirmedBackendDifferenceIsServerCorrection(
        Grimhold.Backend.BackendCommitStatus status, long experience, bool expected)
    {
        bool correction = RaidMenuPresenter.IsServerProgressionCorrection(status, 1, 0, 1, experience);
        Assert.That(correction, Is.EqualTo(expected));
        string feedback = RaidMenuPresenter.ResolveExtractionPersistenceFeedback(
            ExtractionLootSaveStatus.Committed, correction, null, out bool retry);
        Assert.That(feedback, Is.EqualTo(expected ? "Botín asegurado.\n(Progresión ajustada por el servidor)." : "Botín asegurado."));
        Assert.That(retry, Is.False);
    }
}
#endif

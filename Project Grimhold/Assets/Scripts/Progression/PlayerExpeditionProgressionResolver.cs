using Fusion;
using UnityEngine;

/// <summary>
/// Owns the immutable one-shot Expedition Experience resolution and its Level application.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkRaidParticipant))]
[RequireComponent(typeof(PlayerExpeditionExperienceLedger))]
public sealed class PlayerExpeditionProgressionResolver : NetworkBehaviour
{
    [Networked]
    public int BaselineLevel { get; private set; }

    [Networked]
    public long BaselineExperience { get; private set; }

    [Networked]
    public NetworkBool Committed { get; private set; }

    [Networked]
    private ExpeditionExperienceResolutionOutcome CommittedOutcome { get; set; }

    [Networked]
    private int CommittedRetentionBasisPoints { get; set; }

    [Networked]
    private long CommittedConsolidatedExperience { get; set; }

    [Networked]
    private int CommittedResultingLevel { get; set; }

    [Networked]
    private long CommittedResultingExperience { get; set; }

    [Networked]
    private long CommittedEligibleExtractedLootValue { get; set; }

    [Networked]
    private long CommittedNextLevelExperienceRequirement { get; set; }

    [Networked]
    private NetworkBool CommittedIsMaxLevel { get; set; }

    private NetworkRaidParticipant _participant;
    private PlayerExpeditionExperienceLedger _ledger;

    private void Awake()
    {
        _participant = GetComponent<NetworkRaidParticipant>();
        _ledger = GetComponent<PlayerExpeditionExperienceLedger>();
    }

    internal bool TryInitializeBaseline(int level, long experience)
    {
        if (!HasStateAuthority || BaselineLevel != 0 || Committed ||
            !IsValidBaseline(level, experience))
        {
            return false;
        }

        BaselineExperience = experience;
        BaselineLevel = level;
        return true;
    }

    public bool TryGetBaseline(out ExpeditionProgressionBaseline baseline)
    {
        baseline = default;
        if (!IsValidBaseline(BaselineLevel, BaselineExperience))
        {
            return false;
        }

        baseline = new ExpeditionProgressionBaseline(BaselineLevel, BaselineExperience);
        return true;
    }

    public bool TryGetResolution(out ExpeditionExperienceResolution resolution)
    {
        resolution = default;
        if (!Committed || _ledger == null || !_ledger.IsFrozen)
        {
            return false;
        }

        resolution = new ExpeditionExperienceResolution(
            CommittedOutcome,
            _ledger.Snapshot,
            CommittedRetentionBasisPoints,
            CommittedConsolidatedExperience);
        return true;
    }

    public bool TryGetApplication(out ConsolidatedExperienceApplication application)
    {
        application = default;
        if (!Committed || !TryGetBaseline(out ExpeditionProgressionBaseline baseline))
        {
            return false;
        }

        application = ReadStoredApplication(baseline);
        return true;
    }

    /// <summary>
    /// Projects the complete immutable presentation result without resolving or applying gameplay.
    /// </summary>
    public bool TryGetProgressionResult(out ExpeditionProgressionResult result)
    {
        result = default;
        if (_ledger == null || !_ledger.IsFrozen ||
            !TryGetResolution(out ExpeditionExperienceResolution resolution) ||
            !TryGetApplication(out ConsolidatedExperienceApplication application))
        {
            return false;
        }

        result = new ExpeditionProgressionResult(
            resolution,
            application.Result,
            _ledger.PveKillCount,
            _ledger.PvpKillCount,
            _ledger.PveAssistCount,
            _ledger.PvpAssistCount,
            _ledger.FirstOpenChestCount,
            CommittedEligibleExtractedLootValue,
            CommittedNextLevelExperienceRequirement,
            CommittedIsMaxLevel);
        return true;
    }

    /// <summary>
    /// Prepares every fallible rule result before atomically freezing and committing history.
    /// </summary>
    public PlayerExpeditionProgressionFinalizationResult TryFinalize(
        ExpeditionProgressionFinalizationCause cause)
    {
        if (!HasStateAuthority)
        {
            return Result(PlayerExpeditionProgressionFinalizationStatus.MissingStateAuthority);
        }

        if (Committed)
        {
            return Result(PlayerExpeditionProgressionFinalizationStatus.AlreadyCommitted);
        }

        if (!TryGetBaseline(out ExpeditionProgressionBaseline baseline))
        {
            return Result(PlayerExpeditionProgressionFinalizationStatus.MissingOrInvalidBaseline);
        }

        if (_participant == null || _ledger == null || _ledger.IsFrozen ||
            !TryResolveOutcome(cause, out ExpeditionExperienceResolutionOutcome outcome))
        {
            return Result(PlayerExpeditionProgressionFinalizationStatus.IncompatibleLifecycle);
        }

        ExpeditionExperienceSnapshot snapshot = _ledger.Snapshot;
        ExpeditionExperienceResolution resolution;
        ConsolidatedExperienceApplication application;
        if (outcome == ExpeditionExperienceResolutionOutcome.Extracted && CommittedResultingLevel > 0)
        {
            // Preparation already calculated this terminal result before publication. The ACK
            // applied the same loot candidate to the ledger; do not calculate the award again.
            resolution = new ExpeditionExperienceResolution(outcome, snapshot,
                CommittedRetentionBasisPoints, CommittedConsolidatedExperience);
            application = ReadStoredApplication(baseline);
        }
        else
        {
            PlayerExpeditionProgressionFinalizationResult calculation = CalculateProgression(
                snapshot, outcome, baseline, out resolution, out application);
            if (!calculation.IsCompleted) return calculation;
        }

        if (!TryResolveCommittedLevelProgress(
                application.Result,
                out bool isMaxLevel,
                out long nextLevelExperienceRequirement))
        {
            return PlayerExpeditionProgressionFinalizationResult.FromApplicationFailure(
                ConsolidatedExperienceApplicationFailure.InvalidProgressionState);
        }

        if (!TryResolveCommittedEligibleLootValue(
                outcome,
                resolution,
                out long eligibleExtractedLootValue))
        {
            return Result(PlayerExpeditionProgressionFinalizationStatus.IncompatibleLifecycle);
        }

        // Commit contains no validation, callbacks or external fallible work.
        _ledger.CommitFreeze();
        CommittedOutcome = resolution.Outcome;
        CommittedRetentionBasisPoints = resolution.RetentionBasisPoints;
        CommittedConsolidatedExperience = resolution.ConsolidatedExperience;
        CommittedResultingLevel = application.Result.ResultingLevel;
        CommittedResultingExperience = application.Result.ResultingExperience;
        CommittedEligibleExtractedLootValue = eligibleExtractedLootValue;
        CommittedNextLevelExperienceRequirement = nextLevelExperienceRequirement;
        CommittedIsMaxLevel = isMaxLevel;
        Committed = true;
        return Result(PlayerExpeditionProgressionFinalizationStatus.Success);
    }

    /// <summary>
    /// Prepares the extraction award once in existing replicated result storage. Committed stays
    /// false until the persistence ACK and confirmed ledger permit finalization; readers cannot
    /// expose partial Results. The positive resulting level is written last as the validity marker.
    /// </summary>
    public bool TryPrepareExtractionProgression(
        int resultSequence, out long consolidatedExperience, out int resultingLevel, out long resultingExperience)
    {
        consolidatedExperience = 0;
        resultingLevel = 0;
        resultingExperience = 0;
        if (!HasStateAuthority || Committed || _participant == null || _ledger == null || _ledger.IsFrozen ||
            _participant.ResultSequence != resultSequence ||
            _participant.ExtractionExperiencePhase != ExtractionExperienceTransactionPhase.AwaitingPersistenceAck ||
            !_participant.TryGetExtractedLootCandidate(out ExtractedLootExperienceCandidate candidate) ||
            !TryGetBaseline(out ExpeditionProgressionBaseline baseline))
            return false;

        if (CommittedResultingLevel == 0)
        {
            if (!ExpeditionExperienceRules.TryApplyExtractedLootReward(_ledger.Snapshot,
                    candidate.AwardedExperience, out ExpeditionExperienceSnapshot snapshot, out _) ||
                !TryCalculateProgression(snapshot, ExpeditionExperienceResolutionOutcome.Extracted, baseline,
                    out ExpeditionExperienceResolution resolution, out ConsolidatedExperienceApplication application))
                return false;
            CommittedOutcome = resolution.Outcome;
            CommittedRetentionBasisPoints = resolution.RetentionBasisPoints;
            CommittedConsolidatedExperience = resolution.ConsolidatedExperience;
            CommittedResultingExperience = application.Result.ResultingExperience;
            CommittedResultingLevel = application.Result.ResultingLevel;
        }
        consolidatedExperience = CommittedConsolidatedExperience;
        resultingLevel = CommittedResultingLevel;
        resultingExperience = CommittedResultingExperience;
        return true;
    }

    private ConsolidatedExperienceApplication ReadStoredApplication(in ExpeditionProgressionBaseline baseline) =>
        new ConsolidatedExperienceApplication(new ExperienceApplicationResult(
            baseline.Level, baseline.Experience, CommittedResultingLevel, CommittedResultingExperience,
            CommittedResultingLevel - baseline.Level));

    /// <summary>Pure calculation shared by authoritative extraction preparation and finalization.</summary>
    public static bool TryCalculateProgression(
        ExpeditionExperienceSnapshot snapshot,
        ExpeditionExperienceResolutionOutcome outcome,
        ExpeditionProgressionBaseline baseline,
        out ExpeditionExperienceResolution resolution,
        out ConsolidatedExperienceApplication application) =>
        CalculateProgression(snapshot, outcome, baseline, out resolution, out application).IsCompleted;

    private static PlayerExpeditionProgressionFinalizationResult CalculateProgression(
        ExpeditionExperienceSnapshot snapshot,
        ExpeditionExperienceResolutionOutcome outcome,
        ExpeditionProgressionBaseline baseline,
        out ExpeditionExperienceResolution resolution,
        out ConsolidatedExperienceApplication application)
    {
        application = default;
        if (!ExpeditionExperienceResolutionRules.TryResolve(default, snapshot, outcome,
                ProgressionBalanceDefaults.InitialExpeditionExperienceRetentionPolicy,
                out resolution, out ExpeditionExperienceResolutionFailure resolutionFailure))
            return PlayerExpeditionProgressionFinalizationResult.FromResolutionFailure(resolutionFailure);
        if (!ConsolidatedExperienceApplicationRules.TryApply(
                ProgressionBalanceDefaults.InitialExperienceCurve, default, baseline.Level,
                baseline.Experience, resolution, out application,
                out ConsolidatedExperienceApplicationFailure applicationFailure))
            return PlayerExpeditionProgressionFinalizationResult.FromApplicationFailure(applicationFailure);
        return Result(PlayerExpeditionProgressionFinalizationStatus.Success);
    }

    internal static bool IsValidBaseline(int level, long experience) =>
        CharacterProgressionRules.IsValidState(
            ProgressionBalanceDefaults.InitialExperienceCurve,
            level,
            experience);

    internal static bool TryResolveCommittedLevelProgress(
        in ExperienceApplicationResult application,
        out bool isMaxLevel,
        out long nextLevelExperienceRequirement)
    {
        ExperienceCurve curve = ProgressionBalanceDefaults.InitialExperienceCurve;
        isMaxLevel = application.ResultingLevel == curve.MaximumLevel;
        nextLevelExperienceRequirement = 0;
        if (isMaxLevel)
        {
            return application.ResultingExperience == 0;
        }

        return curve.TryGetRequiredExperience(
                application.ResultingLevel,
                out nextLevelExperienceRequirement) &&
            nextLevelExperienceRequirement > 0;
    }

    private bool TryResolveCommittedEligibleLootValue(
        ExpeditionExperienceResolutionOutcome outcome,
        in ExpeditionExperienceResolution resolution,
        out long eligibleExtractedLootValue)
    {
        eligibleExtractedLootValue = 0;
        if (outcome != ExpeditionExperienceResolutionOutcome.Extracted)
        {
            return true;
        }

        if (!_participant.TryGetExtractedLootCandidate(
                out ExtractedLootExperienceCandidate candidate) ||
            _ledger.ExtractedLootResolvedResultSequence != candidate.ResultSequence)
        {
            return false;
        }

        if (!candidate.Matches(_participant.ResultSequence) ||
            candidate.AwardedExperience !=
                resolution.ProvisionalExperience.ExtractedLootExperience)
        {
            return false;
        }

        eligibleExtractedLootValue = candidate.EligibleValue;
        return true;
    }

    private bool TryResolveOutcome(
        ExpeditionProgressionFinalizationCause cause,
        out ExpeditionExperienceResolutionOutcome outcome)
    {
        outcome = default;
        switch (cause)
        {
            case ExpeditionProgressionFinalizationCause.ExtractionConfirmed
                when _participant.State == RaidParticipantState.Extracted &&
                     _participant.ExtractionExperiencePhase ==
                         ExtractionExperienceTransactionPhase.ProgressionPending:
                outcome = ExpeditionExperienceResolutionOutcome.Extracted;
                return true;
            case ExpeditionProgressionFinalizationCause.DefeatConfirmed
                when _participant.State == RaidParticipantState.Defeated:
                outcome = ExpeditionExperienceResolutionOutcome.Defeated;
                return true;
            case ExpeditionProgressionFinalizationCause.DefeatWhileDisconnectedConfirmed
                when _participant.State == RaidParticipantState.Defeated:
                outcome = ExpeditionExperienceResolutionOutcome.DefeatedWhileDisconnected;
                return true;
            case ExpeditionProgressionFinalizationCause.VoluntaryAbandonConfirmed
                when _participant.State == RaidParticipantState.Aborted &&
                     _participant.FinalizationCause == cause:
                outcome = ExpeditionExperienceResolutionOutcome.Abandoned;
                return true;
            case ExpeditionProgressionFinalizationCause.DefinitiveDisconnectConfirmed
                when _participant.State == RaidParticipantState.Aborted &&
                     _participant.FinalizationCause == cause:
                outcome = ExpeditionExperienceResolutionOutcome.DefinitivelyDisconnected;
                return true;
            default:
                return false;
        }
    }

    private static PlayerExpeditionProgressionFinalizationResult Result(
        PlayerExpeditionProgressionFinalizationStatus status) =>
        PlayerExpeditionProgressionFinalizationResult.FromStatus(status);
}

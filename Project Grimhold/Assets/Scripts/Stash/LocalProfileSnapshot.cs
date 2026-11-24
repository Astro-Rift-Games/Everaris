using System.Collections.Generic;

/// <summary>
/// Complete mutable-in-memory representation of one local profile.
/// Instances are cloned before a domain mutation is committed.
/// </summary>
public sealed class LocalProfileSnapshot
{
    public const int CurrentSchemaVersion = 15;
    public const int MaxLoadoutSlots = 30;
    public const int MaxAppliedExtractionReceipts = 256;
    public const int MaxAppliedShopTransactionReceipts = 256;
    public const int MaxAppliedProgressionReceipts = 256;
    public const long InitialCurrency = 0L;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public ProfileId ProfileId { get; set; }
    public long Currency { get; set; } = InitialCurrency;
    public int GuildReputation { get; set; } = 0;
    public MissionRank GetGuildRank(GuildRankProgressionConfig config = null) =>
        GuildRankProgressionRules.ResolveRank(GuildReputation, config);
    public MissionRank GuildRank => GetGuildRank(GuildRankProgressionConfig.Default);
    public int Level { get; set; } = ExperienceCurve.InitialLevel;
    public long CurrentExperience { get; set; }
    public CharacterAttributeState CharacterAttributes { get; set; } =
        ProgressionBalanceDefaults.InitialCharacterAttributeState;
    public int LastAppliedProgressionResultSequence { get; set; }
    public ProgressionReceipt? LastProgressionReceipt { get; set; }
    public List<StashItem> Stash { get; } = new();
    public List<StashItem> Loadout { get; } = new();
    public PreparedEquipmentLoadout PreparedEquipment { get; set; }
    public PendingLoadoutReservation PendingReservation { get; set; }
    /// <summary>
    /// Reservation confirmed locally for raid admission whose backend admission is not yet
    /// proven. It must never be rolled back while this mark holds.
    /// </summary>
    public string AdmissionPendingReservationId { get; set; }
    public PendingExtractionCommit PendingExtractionCommit { get; set; }
    public PendingDefeatCommit PendingDefeatCommit { get; set; }
    public List<ExtractionReceipt> AppliedExtractionReceipts { get; } = new();
    public long ShopIdempotencyWatermark { get; set; } = 0;
    public List<ShopTransactionReceipt> AppliedShopTransactionReceipts { get; } = new();
    public List<ProgressionReceipt> AppliedProgressionReceipts { get; } = new();
    public List<MissionInstanceState> ActiveMissions { get; } = new();
    public DailyMissionRotationState DailyRotation { get; set; } = new();
    public List<AbilityId> UnlockedAbilities { get; } = new();
    public PreparedAbilityLoadout PreparedAbilities { get; set; }
    public QuickSlotBindings QuickSlots { get; set; }
    public int RemoteRevision { get; set; }
    public int InventorySchemaVersion { get; set; } = 1;
    public bool RequiresEquipmentHydration { get; set; }

    public LocalProfileSnapshot Clone()
    {
        var clone = new LocalProfileSnapshot
        {
            SchemaVersion = SchemaVersion,
            ProfileId = ProfileId,
            Currency = Currency,
            GuildReputation = GuildReputation,
            Level = Level,
            CurrentExperience = CurrentExperience,
            CharacterAttributes = CharacterAttributes,
            LastAppliedProgressionResultSequence = LastAppliedProgressionResultSequence,
            LastProgressionReceipt = LastProgressionReceipt,
            PreparedEquipment = PreparedEquipment,
            PendingReservation = PendingReservation?.Clone(),
            AdmissionPendingReservationId = AdmissionPendingReservationId,
            PendingExtractionCommit = PendingExtractionCommit,
            PendingDefeatCommit = PendingDefeatCommit,
            ShopIdempotencyWatermark = ShopIdempotencyWatermark,
            PreparedAbilities = PreparedAbilities,
            QuickSlots = QuickSlots,
            RemoteRevision = RemoteRevision,
            InventorySchemaVersion = InventorySchemaVersion,
            RequiresEquipmentHydration = RequiresEquipmentHydration,
            DailyRotation = DailyRotation != null ? DailyRotation.Clone() : new DailyMissionRotationState()
        };

        clone.Stash.AddRange(Stash);
        clone.Loadout.AddRange(Loadout);
        clone.AppliedExtractionReceipts.AddRange(AppliedExtractionReceipts);
        clone.AppliedShopTransactionReceipts.AddRange(AppliedShopTransactionReceipts);
        clone.AppliedProgressionReceipts.AddRange(AppliedProgressionReceipts);
        clone.UnlockedAbilities.AddRange(UnlockedAbilities);
        // Deep clone mission instances since they contain mutable dictionaries
        foreach (var mission in ActiveMissions)
        {
            var clonedMission = new MissionInstanceState(mission.MissionId, mission.State) 
            { 
                CurrentPhaseIndex = mission.CurrentPhaseIndex 
            };
            foreach (var kvp in mission.ObjectiveProgress)
            {
                clonedMission.ObjectiveProgress.Add(kvp.Key, new ObjectiveProgressState(kvp.Value.CurrentAmount, kvp.Value.ExpeditionId));
            }
            clone.ActiveMissions.Add(clonedMission);
        }
        return clone;
    }
}

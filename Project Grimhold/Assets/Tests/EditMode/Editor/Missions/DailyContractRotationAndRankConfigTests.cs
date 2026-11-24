using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Missions
{
    public sealed class DailyContractRotationAndRankConfigTests
    {
        private const string GuildConfigAssetPath = "Assets/Scriptable Objects/Missions/MissionGuildConfiguration.asset";
        private const string PersistenceConfigAssetPath = "Assets/Resources/LocalProfilePersistenceConfiguration.asset";
        private const string CatalogAssetPath = "Assets/Scriptable Objects/Missions/MissionDefinitionCatalog.asset";

        private readonly List<UnityEngine.Object> _createdObjects = new();

        private sealed class FakeMissionClock : IMissionClock
        {
            public DateTimeOffset UtcNow { get; set; }

            public FakeMissionClock(DateTimeOffset initialUtc)
            {
                UtcNow = initialUtc;
            }
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _createdObjects.Count - 1; i >= 0; i--)
            {
                if (_createdObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_createdObjects[i]);
                }
            }
            _createdObjects.Clear();
        }

        [Test]
        public void ProductiveAssets_MissionGuildConfiguration_ExistsValidatesAndIsWiredIntoPersistenceConfig()
        {
            var guildConfig = AssetDatabase.LoadAssetAtPath<MissionGuildConfiguration>(GuildConfigAssetPath);
            Assert.That(guildConfig, Is.Not.Null, $"MissionGuildConfiguration asset must exist at '{GuildConfigAssetPath}'.");
            Assert.That(guildConfig.TryValidate(out string error), Is.True, $"MissionGuildConfiguration invalid: {error}");

            Assert.That(guildConfig.DailyOfferedContracts, Is.EqualTo(6));
            Assert.That(guildConfig.GlobalResetHourUtc, Is.EqualTo(0));
            Assert.That(guildConfig.GlobalResetMinuteUtc, Is.EqualTo(0));

            var rankConfig = guildConfig.BuildRankProgressionConfig();
            Assert.That(GuildRankProgressionRules.ResolveRank(0, rankConfig), Is.EqualTo(MissionRank.E));
            Assert.That(GuildRankProgressionRules.ResolveRank(100, rankConfig), Is.EqualTo(MissionRank.D));
            Assert.That(GuildRankProgressionRules.ResolveRank(300, rankConfig), Is.EqualTo(MissionRank.C));

            var persistenceConfig = AssetDatabase.LoadAssetAtPath<LocalProfilePersistenceConfiguration>(PersistenceConfigAssetPath);
            Assert.That(persistenceConfig, Is.Not.Null);
            Assert.That(persistenceConfig.MissionGuildConfiguration, Is.SameAs(guildConfig),
                "LocalProfilePersistenceConfiguration must reference MissionGuildConfiguration.asset.");
        }

        [Test]
        public void GuildRank_ReachesRankC_WhenReputationMeetsConfiguredThreshold()
        {
            var profileId = new ProfileId("test-rank-c-progression");
            var repository = new InMemoryLocalProfileRepository();
            var lootCatalog = Track(ScriptableObject.CreateInstance<LootDefinitionCatalog>());
            repository.Initialize(profileId, lootCatalog);

            var snapshot = new LocalProfileSnapshot
            {
                ProfileId = profileId,
                GuildReputation = 300
            };
            repository.TrySave(snapshot, out _);

            var store = new LocalProfileStore(repository, profileId);

            Assert.That(store.GuildRank, Is.EqualTo(MissionRank.C),
                "Rank C must be reachable once reputation reaches the Rank C threshold.");
        }

        [Test]
        public void GuildRank_ChangingThresholdsInInspectorConfig_ChangesProgressionAndAcceptanceWithoutCodeChanges()
        {
            var guildConfig = Track(ScriptableObject.CreateInstance<MissionGuildConfiguration>());
            SetRankThresholds(guildConfig,
                (MissionRank.E, 0),
                (MissionRank.D, 40),
                (MissionRank.C, 90));

            var profileId = new ProfileId("test-custom-thresholds");
            var repository = new InMemoryLocalProfileRepository();
            var lootCatalog = Track(ScriptableObject.CreateInstance<LootDefinitionCatalog>());
            repository.Initialize(profileId, lootCatalog);
            repository.TrySave(new LocalProfileSnapshot
            {
                ProfileId = profileId,
                GuildReputation = 95
            }, out _);

            var store = new LocalProfileStore(
                repository,
                profileId,
                missionGuildConfiguration: guildConfig);

            Assert.That(store.GuildRank, Is.EqualTo(MissionRank.C),
                "At 95 RP with Rank C threshold=90, player must already be Rank C.");

            var rankCContract = CreateMission("CON_C_001", MissionType.Normal, MissionRank.C);
            Assert.That(store.TryAcceptMission(rankCContract), Is.EqualTo(StashOperationResult.Success),
                "Player must be allowed to accept a Rank C contract when configured threshold is met.");

            // Raise Rank C threshold in Inspector config live -> same 95 RP is now Rank D without code changes!
            SetRankThresholds(guildConfig,
                (MissionRank.E, 0),
                (MissionRank.D, 40),
                (MissionRank.C, 150));

            Assert.That(store.GuildRank, Is.EqualTo(MissionRank.D),
                "Raising Rank C threshold to 150 in config must demote 95 RP evaluation to Rank D immediately.");
            var progress = store.GetGuildRankProgress();
            Assert.That(progress.HasNextRank, Is.True);
            Assert.That(progress.NextRank, Is.EqualTo(MissionRank.C));
            Assert.That(progress.RemainingReputation, Is.EqualTo(55));
        }

        [Test]
        public void GuildViewHeader_AtRankD_ShowsRemainingReputationTowardsRankC()
        {
            var viewGo = Track(new GameObject("TestGuildView"));
            var view = viewGo.AddComponent<TownMissionGuildView>();

            var rankTextGo = new GameObject("RankText");
            rankTextGo.transform.SetParent(viewGo.transform, false);
            var rankText = rankTextGo.AddComponent<TextMeshProUGUI>();

            typeof(TownMissionGuildView)
                .GetField("_rankText", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(view, rankText);

            view.PresentHeader(MissionRank.D, 150, 0, TownMissionGuildPresenter.MaxActiveContracts);

            Assert.That(rankText.text, Does.Contain("150 RP"),
                "Rank D header should display remaining reputation required for Rank C, not Max Rank.");
            Assert.That(rankText.text, Does.Contain("Rango C"));
            Assert.That(rankText.text, Does.Not.Contain("Rango Máximo"));
        }

        [Test]
        public void GlobalResetWindow_IsIdenticalForDifferentLoginTimes_AndFlipsAtConfiguredUtcHour()
        {
            var config = new DailyContractRotationConfig(
                offeredContractCount: 6,
                globalResetHourUtc: 6,
                globalResetMinuteUtc: 30);

            var earlyPlayerWindow = DailyContractRotationEngine.ComputeWindow(
                new DateTimeOffset(2026, 11, 23, 6, 31, 0, TimeSpan.Zero), config);
            var latePlayerWindow = DailyContractRotationEngine.ComputeWindow(
                new DateTimeOffset(2026, 11, 24, 6, 29, 59, TimeSpan.Zero), config);
            var afterResetWindow = DailyContractRotationEngine.ComputeWindow(
                new DateTimeOffset(2026, 11, 24, 6, 30, 0, TimeSpan.Zero), config);

            Assert.That(earlyPlayerWindow.WindowId, Is.EqualTo(latePlayerWindow.WindowId),
                "Players entering at different hours within the same global cycle must share the exact same window.");
            Assert.That(earlyPlayerWindow.NextResetUtc, Is.EqualTo(latePlayerWindow.NextResetUtc));
            Assert.That(earlyPlayerWindow.NextResetUtc, Is.EqualTo(new DateTimeOffset(2026, 11, 24, 6, 30, 0, TimeSpan.Zero)));

            Assert.That(afterResetWindow.WindowId, Is.Not.EqualTo(earlyPlayerWindow.WindowId),
                "Crossing the global reset anchor must immediately start the next window.");
            Assert.That(afterResetWindow.CycleStartUtc, Is.EqualTo(new DateTimeOffset(2026, 11, 24, 6, 30, 0, TimeSpan.Zero)));
        }

        [Test]
        public void DailyRotation_OffersAllAvailableContracts_WhenEligiblePoolHasFewerThanSix()
        {
            var catalog = CreateNormalCatalog(3, MissionRank.E);
            var clock = new FakeMissionClock(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero));
            var store = CreateStore("test-fewer-than-six", catalog, clock);

            var offered = store.GetDailyOfferedMissions();
            Assert.That(offered.Count, Is.EqualTo(3));
            Assert.That(offered.Select(m => m.Id).Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void DailyRotation_OffersAllSixCanonicalRankEContracts_WithCurrentProductiveCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MissionDefinitionCatalog>(CatalogAssetPath);
            var guildConfig = AssetDatabase.LoadAssetAtPath<MissionGuildConfiguration>(GuildConfigAssetPath);
            var clock = new FakeMissionClock(new DateTimeOffset(2026, 11, 23, 15, 0, 0, TimeSpan.Zero));
            var store = CreateStore("test-canonical-six", catalog, clock, guildConfig: guildConfig);

            var offered = store.GetDailyOfferedMissions();
            Assert.That(offered.Count, Is.EqualTo(6));
            Assert.That(offered.Select(m => m.Id).Distinct().Count(), Is.EqualTo(6));
        }

        [Test]
        public void DailyRotation_OffersExactlySixDistinctContracts_AndChangesAfterGlobalReset_WhilePreservingActiveMissions()
        {
            var catalog = CreateNormalCatalog(12, MissionRank.E);
            var clock = new FakeMissionClock(new DateTimeOffset(2026, 11, 23, 23, 50, 0, TimeSpan.Zero));
            var store = CreateStore("test-global-reset-cycle", catalog, clock);

            // Day 1 offer (10 minutes before global 00:00 UTC reset)
            var day1Offers = store.GetDailyOfferedMissions();
            string day1Window = store.GetDailyRotationState().RotationWindowId;
            Assert.That(day1Offers.Count, Is.EqualTo(6));
            Assert.That(day1Offers.Select(m => m.Id).Distinct().Count(), Is.EqualTo(6));

            // Accept one offered contract and verify an unoffered contract is rejected
            var acceptedContract = day1Offers[0];
            Assert.That(store.TryAcceptMission(acceptedContract), Is.EqualTo(StashOperationResult.Success));

            var unofferedContract = catalog.Definitions.First(d => day1Offers.All(o => o.Id != d.Id));
            Assert.That(store.TryAcceptMission(unofferedContract), Is.EqualTo(StashOperationResult.InvalidInventory),
                "A catalog contract outside today's daily offer must not be acceptable.");

            // Still before global reset -> same window, exact same offer
            clock.UtcNow = new DateTimeOffset(2026, 11, 23, 23, 59, 59, TimeSpan.Zero);
            var stillDay1Offers = store.GetDailyOfferedMissions();
            Assert.That(stillDay1Offers.Select(m => m.Id).ToArray(), Is.EqualTo(day1Offers.Select(m => m.Id).ToArray()));
            Assert.That(store.GetDailyRotationState().RotationWindowId, Is.EqualTo(day1Window));

            // Cross global 00:00 UTC reset -> window changes, new offer generated, active contract untouched!
            clock.UtcNow = new DateTimeOffset(2026, 11, 24, 0, 0, 1, TimeSpan.Zero);
            var day2Offers = store.GetDailyOfferedMissions();
            var rotationState = store.GetDailyRotationState();

            Assert.That(rotationState.RotationWindowId, Is.Not.EqualTo(day1Window));
            Assert.That(day2Offers.Count, Is.EqualTo(6));
            Assert.That(day2Offers.Select(m => m.Id).Distinct().Count(), Is.EqualTo(6));
            Assert.That(day2Offers.Select(m => m.Id).ToArray(), Is.Not.EqualTo(day1Offers.Select(m => m.Id).ToArray()),
                "Crossing the global reset must generate a new daily offer when the pool exceeds 6 contracts.");
            Assert.That(rotationState.RecentRotations.Count, Is.EqualTo(1),
                "Previous rotation must be recorded into recent history for anti-repetition weighting.");

            var activeMissions = store.GetActiveMissions();
            Assert.That(activeMissions.Count, Is.EqualTo(1));
            Assert.That(activeMissions[0].MissionId, Is.EqualTo(acceptedContract.MissionId));
            Assert.That(activeMissions[0].State, Is.EqualTo(MissionState.Activa),
                "Accepted contracts must remain active across global daily resets (Doc 12 §12.1).");
        }

        [Test]
        public void DailyRotation_RespectsRecentHistoryWeightPenalty_WithoutPermanentlyExcludingContracts()
        {
            var catalog = CreateNormalCatalog(12, MissionRank.E);
            var config = new DailyContractRotationConfig(
                offeredContractCount: 6,
                globalResetHourUtc: 0,
                globalResetMinuteUtc: 0,
                defaultRankWeight: 1f,
                rankWeights: null,
                recentAppearanceWeightMultiplier: 0.01f,
                recentHistoryRotationCount: 1);

            var window1 = DailyContractRotationEngine.ComputeWindow(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero), config);
            var day1 = DailyContractRotationEngine.SelectDailyOffers(catalog.Definitions, MissionRank.E, window1, config, null, "player-1");
            Assert.That(day1.Count, Is.EqualTo(6));

            // Day 2 with heavy recent penalty (0.01x) should strongly favor the 6 unseen contracts
            var history = new List<DailyRotationHistoryRecord> { new(window1.WindowId, day1) };
            var window2 = DailyContractRotationEngine.ComputeWindow(new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero), config);
            var day2 = DailyContractRotationEngine.SelectDailyOffers(catalog.Definitions, MissionRank.E, window2, config, history, "player-1");
            int repeatedCount = day2.Count(id => day1.Contains(id));
            Assert.That(repeatedCount, Is.LessThan(3),
                "Strong recent-appearance de-weighting should significantly reduce consecutive repetitions.");

            // Even if all 6 contracts in a 6-contract pool were recently offered, none are permanently excluded (Doc 12 §17)
            var smallCatalog = CreateNormalCatalog(6, MissionRank.E);
            var smallDay1 = DailyContractRotationEngine.SelectDailyOffers(smallCatalog.Definitions, MissionRank.E, window1, config, null, "player-1");
            var smallHistory = new List<DailyRotationHistoryRecord> { new(window1.WindowId, smallDay1) };
            var smallDay2 = DailyContractRotationEngine.SelectDailyOffers(smallCatalog.Definitions, MissionRank.E, window2, config, smallHistory, "player-1");
            Assert.That(smallDay2.Count, Is.EqualTo(6),
                "Recent history must never permanently exclude contracts from reappearing.");
        }

        [Test]
        public void Presenter_AvailableTab_OffersAtMostSixDailyContracts_WhenCatalogHasMore_AndUpdatesAfterSimulatedReset()
        {
            var catalog = CreateNormalCatalog(10, MissionRank.E);
            var clock = new FakeMissionClock(new DateTimeOffset(2026, 11, 23, 18, 0, 0, TimeSpan.Zero));
            var store = CreateStore("test-presenter-daily-rotation", catalog, clock);

            var presenterGo = Track(new GameObject("TestGuildPresenter"));
            var presenter = presenterGo.AddComponent<TownMissionGuildPresenter>();
            presenter.BindProfile(store);

            var viewGo = Track(new GameObject("TestGuildView"));
            var view = viewGo.AddComponent<TownMissionGuildView>();
            var entryContainerGo = new GameObject("EntryContainer");
            entryContainerGo.transform.SetParent(viewGo.transform, false);
            typeof(TownMissionGuildView)
                .GetField("_entryContainer", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(view, entryContainerGo.transform);

            presenter.SetView(view);
            view.SetTab(GuildViewTab.Available);
            presenter.Refresh();

            Assert.That(view.DisplayedMissionCount, Is.EqualTo(6),
                "Daily rotation must offer 6 contracts when more than 6 are eligible in the catalog.");
            Assert.That(entryContainerGo.transform.childCount, Is.EqualTo(6));

            string windowBefore = store.GetDailyRotationState().RotationWindowId;

            // Advance clock past global reset and refresh
            clock.UtcNow = new DateTimeOffset(2026, 11, 24, 0, 5, 0, TimeSpan.Zero);
            presenter.Refresh();

            Assert.That(store.GetDailyRotationState().RotationWindowId, Is.Not.EqualTo(windowBefore));
            Assert.That(view.DisplayedMissionCount, Is.EqualTo(6));
        }

        [Test]
        public void LocalProfileSaveCodec_RoundTripsDailyRotationState_AndMigratesLegacySchemaV14()
        {
            var profileId = new ProfileId("test-rotation-codec");
            var lootCatalog = Track(ScriptableObject.CreateInstance<LootDefinitionCatalog>());

            var snapshot = new LocalProfileSnapshot { ProfileId = profileId };
            snapshot.DailyRotation.RotationWindowId = "2026-11-23T00:00Z";
            snapshot.DailyRotation.OfferedMissionIds.Add(new MissionId("CON_E_001"));
            snapshot.DailyRotation.OfferedMissionIds.Add(new MissionId("CON_E_002"));
            snapshot.DailyRotation.RecentRotations.Add(new DailyRotationHistoryRecord(
                "2026-11-22T00:00Z",
                new[] { new MissionId("CON_E_003"), new MissionId("CON_E_004") }));

            string json = LocalProfileSaveCodec.Encode(snapshot);
            Assert.That(LocalProfileSaveCodec.TryDecode(json, profileId, lootCatalog, out var decoded, out var status, out string error), Is.True, error);
            Assert.That(status, Is.EqualTo(LocalProfilePersistenceStatus.Ready));
            Assert.That(decoded.DailyRotation.RotationWindowId, Is.EqualTo("2026-11-23T00:00Z"));
            Assert.That(decoded.DailyRotation.OfferedMissionIds.Select(m => m.Value).ToArray(),
                Is.EqualTo(new[] { "CON_E_001", "CON_E_002" }));
            Assert.That(decoded.DailyRotation.RecentRotations.Count, Is.EqualTo(1));
            Assert.That(decoded.DailyRotation.RecentRotations[0].WindowId, Is.EqualTo("2026-11-22T00:00Z"));
            Assert.That(decoded.DailyRotation.RecentRotations[0].OfferedMissionIds.Select(m => m.Value).ToArray(),
                Is.EqualTo(new[] { "CON_E_003", "CON_E_004" }));

            // Backward compatibility: v14 save without dailyRotation field loads cleanly with empty rotation
            string legacyV14Json = json.Replace($"\"schemaVersion\": {LocalProfileSnapshot.CurrentSchemaVersion}", "\"schemaVersion\": 14");
            var jObj = Newtonsoft.Json.Linq.JObject.Parse(legacyV14Json);
            jObj.Remove("dailyRotation");
            Assert.That(LocalProfileSaveCodec.TryDecode(jObj.ToString(), profileId, lootCatalog, out var legacyDecoded, out var legacyStatus, out string legacyError), Is.True, legacyError);
            Assert.That(legacyStatus, Is.EqualTo(LocalProfilePersistenceStatus.Ready));
            Assert.That(legacyDecoded.DailyRotation.HasActiveRotation, Is.False);
            Assert.That(legacyDecoded.DailyRotation.OfferedMissionIds, Is.Empty);
        }

        private LocalProfileStore CreateStore(
            string profileKey,
            MissionDefinitionCatalog catalog,
            IMissionClock clock,
            int initialReputation = 0,
            MissionGuildConfiguration guildConfig = null)
        {
            var profileId = new ProfileId(profileKey);
            var repository = new InMemoryLocalProfileRepository();
            var lootCatalog = Track(ScriptableObject.CreateInstance<LootDefinitionCatalog>());
            repository.Initialize(profileId, lootCatalog);
            repository.TrySave(new LocalProfileSnapshot
            {
                ProfileId = profileId,
                GuildReputation = initialReputation
            }, out _);

            return new LocalProfileStore(
                repository,
                profileId,
                lootCatalog: lootCatalog,
                missionCatalog: catalog,
                missionGuildConfiguration: guildConfig,
                missionClock: clock);
        }

        private MissionDefinitionCatalog CreateNormalCatalog(int count, MissionRank rank)
        {
            var catalog = Track(ScriptableObject.CreateInstance<MissionDefinitionCatalog>());
            var definitions = new List<MissionDefinition>();
            for (int i = 1; i <= count; i++)
            {
                definitions.Add(CreateMission($"CON_{rank}_{i:D3}", MissionType.Normal, rank));
            }

            typeof(MissionDefinitionCatalog)
                .GetField("_definitions", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(catalog, definitions);

            return catalog;
        }

        private MissionDefinition CreateMission(string id, MissionType type, MissionRank rank)
        {
            var def = Track(ScriptableObject.CreateInstance<MissionDefinition>());
            typeof(MissionDefinition)
                .GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(def, id);
            def.Title = $"Contract {id}";
            def.Type = type;
            def.RankRequired = rank;
            return def;
        }

        private static void SetRankThresholds(MissionGuildConfiguration config, params (MissionRank rank, int rep)[] entries)
        {
            var list = new List<MissionGuildConfiguration.GuildRankThresholdSetting>();
            foreach (var (rank, rep) in entries)
            {
                list.Add(new MissionGuildConfiguration.GuildRankThresholdSetting
                {
                    Rank = rank,
                    RequiredReputation = rep
                });
            }

            typeof(MissionGuildConfiguration)
                .GetField("_rankThresholds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(config, list);
        }

        private T Track<T>(T obj) where T : UnityEngine.Object
        {
            _createdObjects.Add(obj);
            return obj;
        }
    }
}

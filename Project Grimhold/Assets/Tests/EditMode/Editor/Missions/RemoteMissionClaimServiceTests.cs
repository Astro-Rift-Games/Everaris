using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Missions
{
    public sealed class RemoteMissionClaimServiceTests
    {
        private const string CatalogAssetPath = "Assets/Scriptable Objects/Missions/MissionDefinitionCatalog.asset";

        private readonly List<UnityEngine.Object> _created = new();

        private sealed class FakeMissionClock : IMissionClock
        {
            public DateTimeOffset UtcNow { get; set; }
            public FakeMissionClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null) UnityEngine.Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        [Test]
        public void ClaimMissionAsync_CreditsAuthoritativeGoldAndRevision_AndMarksMissionReclaimed()
        {
            var (store, service, mission, _) = CreateRig(initialCurrency: 100, initialRevision: 4);

            string sentTxId = null;
            string sentMissionId = null;
            string sentClaimKey = null;
            int sentRevision = -1;
            int reconcileCalls = 0;

            service.Initialize(
                store,
                (txId, missionId, claimKey, expectedRevision) =>
                {
                    sentTxId = txId;
                    sentMissionId = missionId;
                    sentClaimKey = claimKey;
                    sentRevision = expectedRevision;
                    // Return authoritative balance (e.g. 120) and incremented revision (5)
                    return Task.FromResult((true, new ClaimMissionResult
                    {
                        alreadySecured = false,
                        transactionId = txId,
                        missionId = missionId,
                        claimKey = claimKey,
                        goldGranted = 20,
                        currency = 120,
                        revision = 5
                    }, default(BackendError)));
                },
                () =>
                {
                    reconcileCalls++;
                    return Task.FromResult(true);
                });

            var (result, _) = service.ClaimMissionAsync(mission).GetAwaiter().GetResult();

            Assert.That(result, Is.EqualTo(StashOperationResult.Success));
            Assert.That(sentTxId, Is.Not.Null.And.Not.Empty);
            Assert.That(sentMissionId, Is.EqualTo("CON_E_001"));
            Assert.That(sentClaimKey, Is.EqualTo("CON_E_001:2026-11-24T00:00Z"));
            Assert.That(sentRevision, Is.EqualTo(4));
            Assert.That(store.GetCurrency(), Is.EqualTo(120), "Local gold must match backend authoritative balance.");
            Assert.That(store.RemoteRevision, Is.EqualTo(5), "Local revision must match backend revision.");
            Assert.That(store.GetActiveMissions()[0].State, Is.EqualTo(MissionState.Reclamada));
            Assert.That(reconcileCalls, Is.EqualTo(0));
        }

        [Test]
        public void ClaimMissionAsync_NetworkRetryReusesTransactionId_AndHandlesAlreadySecuredWithoutDoubleGold()
        {
            var (store, service, mission, _) = CreateRig(initialCurrency: 100, initialRevision: 2);
            var seenTxIds = new List<string>();
            int attempt = 0;
            int reconcileCalls = 0;

            service.Initialize(
                store,
                (txId, missionId, claimKey, expectedRevision) =>
                {
                    seenTxIds.Add(txId);
                    attempt++;
                    if (attempt == 1)
                    {
                        return Task.FromResult((false, default(ClaimMissionResult), new BackendError
                        {
                            error = "NETWORK_ERROR",
                            message = "timeout"
                        }));
                    }

                    return Task.FromResult((true, new ClaimMissionResult
                    {
                        alreadySecured = true,
                        transactionId = txId,
                        missionId = missionId,
                        claimKey = claimKey,
                        goldGranted = 20,
                        currency = 120,
                        revision = 3
                    }, default(BackendError)));
                },
                () =>
                {
                    reconcileCalls++;
                    return Task.FromResult(true);
                });

            // Attempt 1 fails with transient network error -> mission stays claimable, gold untouched
            var first = service.ClaimMissionAsync(mission).GetAwaiter().GetResult();
            Assert.That(first.result, Is.EqualTo(StashOperationResult.PersistenceFailed));
            Assert.That(store.GetCurrency(), Is.EqualTo(100));
            Assert.That(store.GetActiveMissions()[0].State, Is.EqualTo(MissionState.PendienteDeReclamar));
            Assert.That(reconcileCalls, Is.EqualTo(1));

            // Attempt 2 retries with the exact same transactionId -> succeeds cleanly with authoritative balance 120
            var second = service.ClaimMissionAsync(mission).GetAwaiter().GetResult();
            Assert.That(second.result, Is.EqualTo(StashOperationResult.Success));
            Assert.That(seenTxIds.Count, Is.EqualTo(2));
            Assert.That(seenTxIds[1], Is.EqualTo(seenTxIds[0]), "Retry must reuse the exact same transactionId.");
            Assert.That(store.GetCurrency(), Is.EqualTo(120));
            Assert.That(store.RemoteRevision, Is.EqualTo(3));
            Assert.That(store.GetActiveMissions()[0].State, Is.EqualTo(MissionState.Reclamada));
        }

        [TestCase("MISSION_ALREADY_CLAIMED", StashOperationResult.AlreadyApplied)]
        [TestCase("REVISION_CONFLICT", StashOperationResult.InvalidInventory)]
        [TestCase("RESERVATION_PENDING", StashOperationResult.ReservationPending)]
        [TestCase("MISSION_NOT_FOUND", StashOperationResult.InvalidInventory)]
        public void ClaimMissionAsync_BackendRejectionLeavesMissionUnclaimedAndTriggersReconciliation(
            string errorCode,
            StashOperationResult expectedResult)
        {
            var (store, service, mission, _) = CreateRig(initialCurrency: 100, initialRevision: 2);
            int reconcileCalls = 0;

            service.Initialize(
                store,
                (_, _, _, _) => Task.FromResult((false, default(ClaimMissionResult), new BackendError
                {
                    error = errorCode,
                    message = "rejected"
                })),
                () =>
                {
                    reconcileCalls++;
                    return Task.FromResult(true);
                });

            var (result, _) = service.ClaimMissionAsync(mission).GetAwaiter().GetResult();

            Assert.That(result, Is.EqualTo(expectedResult));
            Assert.That(store.GetCurrency(), Is.EqualTo(100), "Rejected claim must never credit Gold locally.");
            Assert.That(store.GetActiveMissions()[0].State, Is.EqualTo(MissionState.PendienteDeReclamar));
            Assert.That(reconcileCalls, Is.EqualTo(1));
        }

        [Test]
        public void Presenter_OnClaimRequested_DelegatesToRemoteClaimService_AndSyncsAuthoritativeGold()
        {
            var (store, service, mission, _) = CreateRig(initialCurrency: 50, initialRevision: 1);

            service.Initialize(
                store,
                (txId, missionId, claimKey, _) => Task.FromResult((true, new ClaimMissionResult
                {
                    alreadySecured = false,
                    transactionId = txId,
                    missionId = missionId,
                    claimKey = claimKey,
                    goldGranted = 20,
                    currency = 70,
                    revision = 2
                }, default(BackendError))),
                () => Task.FromResult(true));

            var presenterGo = Track(new GameObject("TestGuildPresenter"));
            var presenter = presenterGo.AddComponent<TownMissionGuildPresenter>();
            presenter.BindProfile(store);
            presenter.SetRemoteClaimService(service);

            var viewGo = Track(new GameObject("TestGuildView"));
            var view = viewGo.AddComponent<TownMissionGuildView>();
            var feedback = new GameObject("Feedback").AddComponent<TMPro.TextMeshProUGUI>();
            feedback.transform.SetParent(viewGo.transform, false);
            typeof(TownMissionGuildView)
                .GetField("_feedbackText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(view, feedback);
            presenter.SetView(view);

            presenter.OnClaimRequested(mission);

            Assert.That(store.GetCurrency(), Is.EqualTo(70));
            Assert.That(store.RemoteRevision, Is.EqualTo(2));
            Assert.That(store.GetActiveMissions()[0].State, Is.EqualTo(MissionState.Reclamada));
            Assert.That(view.FeedbackText, Does.Contain("reclamada con éxito"));
        }

        [Test]
        public void HydrateSnapshot_WithZeroBackendProgressionSequence_ClearsLocalMissionProgressionReceiptsAndEncodesCleanly()
        {
            var (store, _, mission, repo) = CreateRig(initialCurrency: 50, initialRevision: 1);
            Assert.That(store.TryClaimMissionWithAuthority(mission, 70, 2), Is.EqualTo(StashOperationResult.Success));
            Assert.That(repo.Snapshot.AppliedProgressionReceipts.Count, Is.GreaterThan(0));

            var hydrated = repo.Snapshot.Clone();
            var progData = new ProgressionData
            {
                level = 1,
                experience = 0,
                lastAppliedProgressionResultSequence = 0,
                characterAttributes = new CharacterAttributesData
                {
                    vitality = 5, resistance = 5, strength = 5, dexterity = 5, intelligence = 5, luck = 5, availablePoints = 10
                },
                revision = 2
            };

            bool ok = ApplicationStashServiceBootstrapper.HydrateSnapshot(
                store.ProfileId,
                hydrated,
                null,
                progData,
                store.LootCatalog,
                null);

            Assert.That(ok, Is.True);
            Assert.That(hydrated.AppliedProgressionReceipts, Is.Empty);

            string json = LocalProfileSaveCodec.Encode(hydrated);
            Assert.That(
                LocalProfileSaveCodec.TryDecode(json, store.ProfileId, store.LootCatalog, out _, out _, out string error),
                Is.True,
                $"Hydrated snapshot after mission claim must round-trip cleanly: {error}");
        }

        private (LocalProfileStore store, RemoteMissionClaimService service, MissionDefinition mission, InMemoryLocalProfileRepository repo)
            CreateRig(long initialCurrency, int initialRevision)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MissionDefinitionCatalog>(CatalogAssetPath);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.TryGet("CON_E_001", out var mission), Is.True);

            var profileId = new ProfileId("test-remote-claim-player");
            var repo = new InMemoryLocalProfileRepository();
            var lootCatalog = Track(ScriptableObject.CreateInstance<LootDefinitionCatalog>());
            repo.Initialize(profileId, lootCatalog);

            var snapshot = new LocalProfileSnapshot
            {
                ProfileId = profileId,
                Currency = initialCurrency,
                RemoteRevision = initialRevision
            };
            snapshot.ActiveMissions.Add(new MissionInstanceState(mission.MissionId, MissionState.PendienteDeReclamar));
            repo.TrySave(snapshot, out _);

            var clock = new FakeMissionClock(new DateTimeOffset(2026, 11, 24, 15, 0, 0, TimeSpan.Zero));
            var store = new LocalProfileStore(repo, profileId, lootCatalog: lootCatalog, missionCatalog: catalog, missionClock: clock);

            var serviceGo = Track(new GameObject("TestRemoteMissionClaimService"));
            var service = serviceGo.AddComponent<RemoteMissionClaimService>();
            return (store, service, mission, repo);
        }

        private T Track<T>(T obj) where T : UnityEngine.Object
        {
            _created.Add(obj);
            return obj;
        }
    }
}

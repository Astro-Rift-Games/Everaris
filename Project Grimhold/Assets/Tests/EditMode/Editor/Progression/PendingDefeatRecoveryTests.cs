#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Threading.Tasks;
using Grimhold.Backend;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

public sealed class PendingDefeatRecoveryTests
{
    private LootDefinitionCatalog _catalog;
    private ProfileId _profile;
    private FaultingRepository _repository;
    private LocalProfileStore _store;
    private ProgressionReceipt _receipt;

    [SetUp]
    public void SetUp()
    {
        _catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
            "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
        _profile = new ProfileId("pending_defeat_profile");
        _repository = new FaultingRepository();
        Assert.That(_repository.Initialize(_profile, _catalog), Is.True);
        var initial = _repository.Snapshot.Clone();
        initial.PendingReservation = new PendingLoadoutReservation("played_reservation", null);
        Assert.That(_repository.TrySave(initial, out _), Is.True);
        _store = new LocalProfileStore(_repository, _profile, _catalog);
        _receipt = new ProgressionReceipt("raid_defeat_pending", _profile, 1, 70, 1);
        Assert.That(_store.TryCommitDefeatProgression(_receipt, 1, 70), Is.EqualTo(StashOperationResult.Success));
    }

    [Test]
    public void Commit_CodecRoundTrip_PreservesPendingReceipt_AndDuplicateDoesNotCreditTwice()
    {
        string before = LocalProfileSaveCodec.Encode(_repository.Snapshot);
        var data = JObject.Parse(before);
        Assert.That(data["pendingDefeatCommit"]?["receipt"]?["raidId"]?.Value<string>(), Is.EqualTo(_receipt.RaidId));
        Assert.That(data["pendingDefeatCommit"]?["receipt"]?["resultSequence"]?.Value<int>(), Is.EqualTo(1));
        Assert.That(data["pendingDefeatCommit"]?["receipt"]?["consolidatedExperience"]?.Value<long>(), Is.EqualTo(70));
        Assert.That(LocalProfileSaveCodec.TryDecode(before, _profile, _catalog, out var restored, out _, out var error), Is.True, error);
        Assert.That(LocalProfileSaveCodec.Encode(restored), Is.EqualTo(before));
        Assert.That(_store.TryCommitDefeatProgression(_receipt, 1, 70), Is.EqualTo(StashOperationResult.Success));
        Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(before));
    }

    [Test]
    public void OldProfileWithoutPendingDefeat_RemainsReadable()
    {
        var data = JObject.Parse(LocalProfileSaveCodec.Encode(new LocalProfileSnapshot { ProfileId = _profile }));
        data["schemaVersion"] = 12;
        data.Remove("pendingDefeatCommit");
        Assert.That(LocalProfileSaveCodec.TryDecode(data.ToString(), _profile, _catalog, out var restored, out _, out var error), Is.True, error);
        Assert.That(restored.CurrentExperience, Is.Zero);
    }

    [Test]
    public void LoginHydration_DoesNotEraseUnsentDefeatProgression()
    {
        var snapshot = _repository.Snapshot.Clone();
        string before = LocalProfileSaveCodec.Encode(snapshot);
        Assert.That(ApplicationStashServiceBootstrapper.HydrateSnapshot(_profile, snapshot, null,
            new ProgressionData { level = 1, experience = 0, revision = 4 }, _catalog, null), Is.True);
        Assert.That(LocalProfileSaveCodec.Encode(snapshot), Is.EqualTo(before));
    }

    [Test]
    public void PendingDefeat_DoesNotSuppressBackendAbilityHydration()
    {
        var snapshot = _repository.Snapshot.Clone();
        Assert.That(ApplicationStashServiceBootstrapper.HydrateSnapshot(_profile, snapshot, null,
            new ProgressionData { level = 1, experience = 0, revision = 4,
                unlockedAbilities = new[] { "backend_ability" } }, _catalog, null), Is.True);
        Assert.That(snapshot.CurrentExperience, Is.EqualTo(70));
        Assert.That(snapshot.UnlockedAbilities, Is.EqualTo(new[] { new AbilityId("backend_ability") }));
        Assert.That(snapshot.PendingDefeatCommit.Receipt, Is.EqualTo(_receipt));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Replay_BackendConfirmation_ClearsPendingAndAcceptsAuthoritativeProgressionOnce(bool alreadySecured)
    {
        int calls = 0;
        Assert.That(await Replay(receipt =>
        {
            calls++;
            Assert.That(receipt, Is.EqualTo(_receipt));
            return Task.FromResult((true, new CommitDefeatUnifiedResult
            { alreadySecured = alreadySecured, level = 1, experience = 75, revision = 4 }, default(BackendError)));
        }), Is.True);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(_store.GetCurrentExperience(), Is.EqualTo(75));
        var data = JObject.Parse(LocalProfileSaveCodec.Encode(_repository.Snapshot));
        Assert.That(data["pendingDefeatCommit"].Type, Is.EqualTo(JTokenType.Null));
        string before = LocalProfileSaveCodec.Encode(_repository.Snapshot);
        Assert.That(_store.TryCommitDefeatProgression(_receipt, 1, 70), Is.EqualTo(StashOperationResult.Success));
        Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(before), "A late live callback must not apply the original XP again after recovery.");
        Assert.That(await Replay(_ => throw new Exception("Acknowledged commit must not be replayed.")), Is.True);
        Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(before));
    }

    [TestCase("TIMEOUT")]
    [TestCase("CONCURRENT_MUTATION")]
    [TestCase("RESULT_ALREADY_CONSUMED")]
    [TestCase("RESULT_CONFLICT")]
    public async Task Replay_FailureIncludingUnprovenConsumed_RetainsExactPendingState(string code)
    {
        string before = LocalProfileSaveCodec.Encode(_repository.Snapshot);
        int calls = 0;
        Assert.That(await Replay(receipt =>
        {
            calls++;
            Assert.That(receipt, Is.EqualTo(_receipt));
            return Task.FromResult((false, default(CommitDefeatUnifiedResult), new BackendError { error = code }));
        }), Is.False);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(before));
    }

    [TestCase("profileId", "foreign_profile")]
    [TestCase("raidId", "")]
    [TestCase("resultSequence", 0)]
    [TestCase("consolidatedExperience", -1)]
    public void Codec_RejectsInvalidPendingReceipt(string field, object value)
    {
        var data = JObject.Parse(LocalProfileSaveCodec.Encode(_repository.Snapshot));
        data["pendingDefeatCommit"]["receipt"][field] = JToken.FromObject(value);
        Assert.That(LocalProfileSaveCodec.TryDecode(data.ToString(), _profile, _catalog, out _, out _, out _), Is.False);
    }

    [Test]
    public void PendingReceipt_CannotBeOverwrittenByAnotherParticipation()
    {
        string before = LocalProfileSaveCodec.Encode(_repository.Snapshot);
        Assert.That(_store.TryCommitDefeatProgression(new ProgressionReceipt("other_raid", _profile, 2, 10, 1), 1, 80),
            Is.EqualTo(StashOperationResult.ReservationPending));
        Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(before));
    }

    [Test]
    public async Task Acknowledgement_SaveFailureRetainsPendingForIdempotentReplay()
    {
        string before = LocalProfileSaveCodec.Encode(_repository.Snapshot);
        _repository.FailSave = true;
        UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
            "[LocalProfileStore] Commit failed. Available=True; Status=Ready; Error=Injected atomic-write failure.");
        Assert.That(await Replay(_ => Task.FromResult((true,
            new CommitDefeatUnifiedResult { level = 1, experience = 75, revision = 4 }, default(BackendError)))), Is.False);
        Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(before));
        _repository.FailSave = false;
        Assert.That(await Replay(_ => Task.FromResult((true,
            new CommitDefeatUnifiedResult { alreadySecured = true, level = 1, experience = 75, revision = 4 }, default(BackendError)))), Is.True);
        Assert.That(_store.PendingDefeatCommit, Is.Null);
        Assert.That(_store.GetCurrentExperience(), Is.EqualTo(75));
    }

    [TestCase(0, 70, 4)]
    [TestCase(1, -1, 4)]
    [TestCase(1, 70, -1)]
    public async Task InvalidAcknowledgement_PreservesPendingAndProgression(int level, long experience, int revision)
    {
        string before = LocalProfileSaveCodec.Encode(_repository.Snapshot);
        Assert.That(await Replay(_ => Task.FromResult((true,
            new CommitDefeatUnifiedResult { level = level, experience = experience, revision = revision }, default(BackendError)))), Is.False);
        Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(before));
    }

    [TestCase("played_reservation", true)]
    [TestCase("newer_reservation", false)]
    public async Task Completion_DiscardsOnlyThePlayedReservation(string reservationId, bool cleared)
    {
        var hydrated = _repository.Snapshot.Clone();
        hydrated.PendingReservation = new PendingLoadoutReservation(reservationId, null);
        Assert.That(_repository.TrySave(hydrated, out _), Is.True);
        Assert.That(await Replay(_ => Task.FromResult((true,
            new CommitDefeatUnifiedResult { level = 1, experience = 75, revision = 4 }, default(BackendError)))), Is.True);
        Assert.That(_store.PendingReservation == null, Is.EqualTo(cleared));
        if (!cleared) Assert.That(_store.PendingReservation.ReservationId, Is.EqualTo(reservationId));
    }

    [Test]
    public async Task FailedReplay_DoesNotBlockNewReservation_AndOldCompletionPreservesIt()
    {
        Assert.That(await Replay(_ => Task.FromResult((false, default(CommitDefeatUnifiedResult),
            new BackendError { error = "TIMEOUT" }))), Is.False);
        // Hydration can supply the next prepared loadout while the old receipt remains pending.
        var snapshot = _repository.Snapshot.Clone();
        snapshot.PreparedEquipment = new PreparedEquipmentLoadout(new LootId("arming_sword"), default);
        Assert.That(_repository.TrySave(snapshot, out _), Is.True);
        Assert.That(_store.TryCreateLoadoutReservation("next", out var reservation), Is.EqualTo(StashOperationResult.Success));
        Assert.That(reservation.ReservationId, Is.EqualTo("next"));
        Assert.That(_store.PendingDefeatCommit.Receipt, Is.EqualTo(_receipt));
        Assert.That(await Replay(_ => Task.FromResult((true,
            new CommitDefeatUnifiedResult { level = 1, experience = 75, revision = 4 }, default(BackendError)))), Is.True);
        Assert.That(_store.PendingReservation.ReservationId, Is.EqualTo("next"));
        Assert.That(_store.PendingReservation.PreparedEquipment, Is.EqualTo(reservation.PreparedEquipment));
    }

    [Test]
    public async Task MissingAuthoritativeResult_RetiresOnce_ThenOrdinaryHydrationUsesBackendProgression()
    {
        var snapshot = _repository.Snapshot.Clone();
        snapshot.PendingReservation = new PendingLoadoutReservation("next", null);
        Assert.That(_repository.TrySave(snapshot, out _), Is.True);
        int hydrations = 0;
        UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
            "[ApplicationStashServiceBootstrapper] Pending defeat retired: the backend can no longer credit this result. Backend progression remains authoritative.");
        Assert.That(await ApplicationStashServiceBootstrapper.RetryPendingDefeatAsync(_store,
            _ => Task.FromResult((false, default(CommitDefeatUnifiedResult), new BackendError { error = "NO_AUTHORITATIVE_RESULT" })),
            () =>
            {
                hydrations++;
                Assert.That(_store.PendingDefeatCommit, Is.Null);
                return Task.FromResult(_store.ReconcileRemoteState(BackendInventory("next"),
                    new ProgressionData { level = 1, experience = 12, revision = 4 }, _catalog, null) == StashOperationResult.Success);
            }), Is.False, "An uncreditable result must never be presented as saved.");
        Assert.That(hydrations, Is.EqualTo(1));
        Assert.That(_store.PendingDefeatCommit, Is.Null);
        Assert.That(_store.PendingReservation.ReservationId, Is.EqualTo("next"));
        Assert.That(_store.GetCurrentExperience(), Is.EqualTo(12));
        string after = LocalProfileSaveCodec.Encode(_repository.Snapshot);
        Assert.That(await Replay(_ => throw new Exception("Retired record must not replay.")), Is.True);
        Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(after));
        UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
    }

    [TestCase("played_reservation", true)]
    [TestCase("played_reservation", false)]
    [TestCase("newer_reservation", true)]
    public async Task MissingAuthoritativeResult_DoesNotResurrectPlayedReservationFromBackend(string backendReservation, bool clearSucceeds)
    {
        int clears = 0;
        UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
            "[ApplicationStashServiceBootstrapper] Pending defeat retired: the backend can no longer credit this result. Backend progression remains authoritative.");
        Assert.That(await ApplicationStashServiceBootstrapper.RetryPendingDefeatAsync(_store,
            _ => Task.FromResult((false, default(CommitDefeatUnifiedResult), new BackendError { error = "NO_AUTHORITATIVE_RESULT" })),
            () =>
            {
                Assert.That(clears, Is.EqualTo(1), "Loss clear must precede reconciliation.");
                return Task.FromResult(_store.ReconcileRemoteState(BackendInventory(backendReservation),
                    new ProgressionData { level = 1, experience = 12, revision = 4 }, _catalog, null) == StashOperationResult.Success);
            },
            clearLoss: id =>
            {
                clears++;
                Assert.That(id, Is.EqualTo("played_reservation"));
                // The expected-ID backend guard rejects a different current reservation with 409.
                var outcome = id != backendReservation
                    ? (success: false, error: new BackendError { error = "RESERVATION_MISMATCH" })
                    : (success: clearSucceeds, error: default(BackendError));
                if (outcome.success) backendReservation = null;
                return Task.FromResult(outcome.success);
            }), Is.False);
        Assert.That(clears, Is.EqualTo(1));
        Assert.That(_store.PendingDefeatCommit, Is.Null);
        Assert.That(_store.PendingReservation?.ReservationId, Is.EqualTo(backendReservation));
        Assert.That(_store.GetCurrentExperience(), Is.EqualTo(12));
        UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public async Task LoginShapedRetirement_ClearsPlayedReservationBeforeHydration()
    {
        var s = _repository.Snapshot.Clone();
        s.PendingReservation = new PendingLoadoutReservation("played_reservation", null);
        Assert.That(_repository.TrySave(s, out _), Is.True);
        string backend = "played_reservation";
        UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
            "[ApplicationStashServiceBootstrapper] Pending defeat retired: the backend can no longer credit this result. Backend progression remains authoritative.");
        Assert.That(await ApplicationStashServiceBootstrapper.RetryPendingDefeatAsync(_store,
            _ => Task.FromResult((false, default(CommitDefeatUnifiedResult), new BackendError { error = "NO_AUTHORITATIVE_RESULT" })),
            () => Task.FromResult(_store.ReconcileRemoteState(BackendInventory(backend),
                new ProgressionData { level = 1, experience = 12, revision = 4 }, _catalog, null) == StashOperationResult.Success),
            id => { Assert.That(_store.PendingReservation, Is.Null); if (id == backend) backend = null; return Task.FromResult(true); }), Is.False);
        Assert.That(_store.PendingReservation, Is.Null);
        UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public async Task RetirementWithoutPlayedId_NeverClears()
    {
        var repo = new FaultingRepository();
        Assert.That(repo.Initialize(_profile, _catalog), Is.True);
        var store = new LocalProfileStore(repo, _profile, _catalog);
        var receipt = new ProgressionReceipt("raid_no_reservation", _profile, 1, 70, 1);
        Assert.That(store.TryCommitDefeatProgression(receipt, 1, 70), Is.EqualTo(StashOperationResult.Success));
        int clears = 0;
        UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
            "[ApplicationStashServiceBootstrapper] Pending defeat retired: the backend can no longer credit this result. Backend progression remains authoritative.");
        Assert.That(await ApplicationStashServiceBootstrapper.RetryPendingDefeatAsync(store,
            _ => Task.FromResult((false, default(CommitDefeatUnifiedResult), new BackendError { error = "NO_AUTHORITATIVE_RESULT" })),
            null, _ => { clears++; return Task.FromResult(true); }), Is.False);
        Assert.That(clears, Is.Zero);
        Assert.That(store.PendingDefeatCommit, Is.Null);
        UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
    }

    private static InventoryData BackendInventory(string reservationId) => new InventoryData
    {
        inventorySchemaVersion = 3, stash = new InventoryItemData[0], loadout = new InventoryItemData[0], revision = 4,
        pendingReservation = reservationId == null ? default : new PendingReservationData
        {
            reservationId = reservationId, admittedAt = "2026-11-14T00:00:00.000Z", items = new InventoryItemData[0]
        }
    };

    [Test]
    public void SecondDefeat_StopsLocalRetriesAndExplainsThatItWasNotRecorded()
    {
        string before = LocalProfileSaveCodec.Encode(_repository.Snapshot);
        var go = new UnityEngine.GameObject("defeat feedback test");
        try
        {
            var participant = go.AddComponent<NetworkRaidParticipant>();
            Assert.That(participant.ShouldRetryLocalDefeatCommit, Is.True);
            var result = _store.TryCommitDefeatProgression(new ProgressionReceipt("other_raid", _profile, 2, 10, 1), 1, 80);
            Assert.That(participant.AcceptLocalDefeatCommitResult(result), Is.False);
            Assert.That(participant.ShouldRetryLocalDefeatCommit, Is.False);
            Assert.That(participant.BackendDefeatFeedback.Status, Is.EqualTo(BackendCommitStatus.Failed));
            Assert.That(participant.BackendDefeatFeedback.Message, Is.EqualTo(
                "No se pudo registrar esta derrota localmente porque hay otra derrota pendiente de guardar."));
            Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(before));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [TestCase(StashOperationResult.Success, true)]
    [TestCase(StashOperationResult.AlreadySecured, true)]
    [TestCase(StashOperationResult.InvalidInventory, false)]
    public void LocalDefeatDecision_PreservesAcceptedAndRetryableOutcomes(StashOperationResult result, bool accepted)
    {
        var go = new UnityEngine.GameObject("defeat decision test");
        try
        {
            var participant = go.AddComponent<NetworkRaidParticipant>();
            Assert.That(participant.AcceptLocalDefeatCommitResult(result), Is.EqualTo(accepted));
            Assert.That(participant.ShouldRetryLocalDefeatCommit, Is.EqualTo(!accepted));
            Assert.That(participant.BackendDefeatFeedback.Status, Is.EqualTo(BackendCommitStatus.None));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public async Task MissingAuthoritativeResult_SaveFailureRetainsExactPendingState()
    {
        string before = LocalProfileSaveCodec.Encode(_repository.Snapshot);
        _repository.FailSave = true;
        UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
            "[LocalProfileStore] Commit failed. Available=True; Status=Ready; Error=Injected atomic-write failure.");
        Assert.That(await ApplicationStashServiceBootstrapper.RetryPendingDefeatAsync(_store,
            _ => Task.FromResult((false, default(CommitDefeatUnifiedResult), new BackendError { error = "NO_AUTHORITATIVE_RESULT" })),
            () => throw new Exception("Failed retirement must not hydrate.")), Is.False);
        Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(before));
    }

    [TestCase(null)]
    [TestCase("")]
    public async Task UnsetReservationId_FallsBackToPlayedReservation_AndCompletionClearsIt(string reservationId)
    {
        var repository = new FaultingRepository();
        Assert.That(repository.Initialize(_profile, _catalog), Is.True);
        var snapshot = repository.Snapshot.Clone();
        snapshot.PendingReservation = new PendingLoadoutReservation("played", null);
        Assert.That(repository.TrySave(snapshot, out _), Is.True);
        var store = new LocalProfileStore(repository, _profile, _catalog);
        Assert.That(store.TryCommitDefeatProgression(_receipt, 1, 70, reservationId), Is.EqualTo(StashOperationResult.Success));
        Assert.That(store.PendingDefeatCommit.ReservationId, Is.EqualTo("played"));
        snapshot = repository.Snapshot.Clone();
        snapshot.PendingReservation = new PendingLoadoutReservation("played", null);
        Assert.That(repository.TrySave(snapshot, out _), Is.True);
        Assert.That(await ApplicationStashServiceBootstrapper.RetryPendingDefeatAsync(store, _ => Task.FromResult((true,
            new CommitDefeatUnifiedResult { level = 1, experience = 75, revision = 4 }, default(BackendError)))), Is.True);
        Assert.That(store.PendingReservation, Is.Null);
    }

    [Test]
    public void DefeatAttempts_RetryAfterOneSecond_NotEveryFrame()
    {
        var go = new UnityEngine.GameObject("defeat retry test");
        try
        {
            var participant = go.AddComponent<NetworkRaidParticipant>();
            Assert.That(participant.TryBeginLocalDefeatCommitAttempt(10), Is.True);
            Assert.That(participant.TryBeginLocalDefeatCommitAttempt(10), Is.False);
            Assert.That(participant.TryBeginLocalDefeatCommitAttempt(10.99f), Is.False);
            Assert.That(participant.TryBeginLocalDefeatCommitAttempt(11), Is.True);
            Assert.That(participant.TryBeginDefeatPublishAttempt(10), Is.True);
            Assert.That(participant.TryBeginDefeatPublishAttempt(10), Is.False);
            Assert.That(participant.TryBeginDefeatPublishAttempt(10.99f), Is.False);
            Assert.That(participant.TryBeginDefeatPublishAttempt(11), Is.True);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    private Task<bool> Replay(Func<ProgressionReceipt, Task<(bool success, CommitDefeatUnifiedResult result, BackendError error)>> commit) =>
        ApplicationStashServiceBootstrapper.RetryPendingDefeatAsync(_store, commit);

    private sealed class FaultingRepository : ILocalProfileRepository
    {
        private readonly InMemoryLocalProfileRepository _inner = new InMemoryLocalProfileRepository();
        public bool FailSave;
        public LocalProfilePersistenceStatus Status => _inner.Status;
        public string LastError => _inner.LastError;
        public LocalProfileSnapshot Snapshot => _inner.Snapshot;
        public bool Initialize(ProfileId profileId, LootDefinitionCatalog catalog) => _inner.Initialize(profileId, catalog);
        public bool TrySave(LocalProfileSnapshot snapshot, out string error)
        {
            if (FailSave) { error = "Injected atomic-write failure"; return false; }
            return _inner.TrySave(snapshot, out error);
        }
    }
}
#endif

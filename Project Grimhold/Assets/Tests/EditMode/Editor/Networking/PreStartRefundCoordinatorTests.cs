#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;
using LogType = UnityEngine.LogType;

/// <summary>
/// FIX-95 coordinator behavior through the real HTTP boundary and a real profile store: an ending
/// confirmed before the raid reached InProgress is refunded; every other ending stays a loss.
/// </summary>
public sealed class PreStartRefundCoordinatorTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string RefundPath = "/character/me/inventory/reservation/refund";
    private const string ReservationPath = "/character/me/inventory/reservation";
    private const string RaidStartedPath = "/character/me/inventory/reservation/raid-started";
    private const string RaidId = "raid-123456";
    private const string ReservationId = "reservation-a";
    private static readonly ProfileId Profile = new("47474747474747474747474747474747");

    private LootDefinitionCatalog _catalog;
    private LocalProfileStore _store;
    private PendingLoadoutReservation _admitted;
    private LoopbackBackendServer _server;
    private RemoteServiceFixture _remote;
    private ApplicationStashContext _context;
    private SessionConnectionCoordinator _coordinator;

    [SetUp]
    public void SetUp()
    {
        _catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
            "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
        var repository = new InMemoryLocalProfileRepository();
        Assert.That(repository.Initialize(Profile, _catalog), Is.True);
        LocalProfileSnapshot snapshot = repository.Snapshot.Clone();
        snapshot.Loadout.Add(new StashItem(new LootId("bone"), 2, 0));
        snapshot.PreparedEquipment = new PreparedEquipmentLoadout(new LootId("rapier"), default);
        Assert.That(repository.TrySave(snapshot, out string error), Is.True, error);
        _store = new LocalProfileStore(repository, Profile, _catalog);
        Assert.That(_store.TryCreateLoadoutReservation(ReservationId, out _admitted), Is.EqualTo(StashOperationResult.Success));
        // Admission already consumed the reservation locally; the player owns nothing in Town.
        Assert.That(_store.TryConfirmLoadoutReservation(ReservationId), Is.EqualTo(StashOperationResult.Success));
    }

    [TearDown]
    public void TearDown()
    {
        _remote?.Dispose();
        _server?.Dispose();
    }

    private void Start(params LoopbackBackendServer.Reply[] replies)
    {
        _server = new LoopbackBackendServer(replies);
        _remote = new RemoteServiceFixture(_server.BaseUrl, _store);
        _context = _remote.Root.AddComponent<ApplicationStashContext>();
        _context.Initialize(_store, null, null, null, null);
        _coordinator = _remote.Root.AddComponent<SessionConnectionCoordinator>();
        Set("_recoveryStashContext", _context);
        Set("_currentProfileId", Profile);
        Set("_reservationParticipationRaidId", RaidId);
        Set("_reservationParticipationReservationId", ReservationId);
    }

    private void Set(string field, object value) =>
        typeof(SessionConnectionCoordinator).GetField(field, Private).SetValue(_coordinator, value);

    private T Get<T>(string field) =>
        (T)typeof(SessionConnectionCoordinator).GetField(field, Private).GetValue(_coordinator);

    private Task EndParticipation()
    {
        typeof(SessionConnectionCoordinator).GetMethod("ClearLostParticipationReservation", Private)
            .Invoke(_coordinator, null);
        return Get<Task>("_participationOutcomeTask");
    }

    private void ObserveRaid(bool isStateAuthority, bool started) =>
        typeof(SessionConnectionCoordinator).GetMethod("ApplyRaidStartObservation", Private)
            .Invoke(_coordinator, new object[] { isStateAuthority, started });

    private RaidLossNoticeSlot CapturePendingLossNotice()
    {
        Assert.That(RaidLossNotice.TryCreate(RaidLossNoticeReason.RaidStartFailed, _admitted,
            out RaidLossNotice notice, out string error), Is.True, error);
        var slot = Get<RaidLossNoticeSlot>("_lossNotice");
        Assert.That(slot.Capture(notice), Is.True);
        return slot;
    }

    private void AssertLocalOwnershipRestoredOnce()
    {
        Assert.That(_store.GetLoadout(), Is.EqualTo(new[] { new StashItem(new LootId("bone"), 2, 0) }));
        Assert.That(_store.GetPreparedEquipment().WeaponSetAMainHand, Is.EqualTo(new LootId("rapier")));
    }

    private void AssertLocalOwnershipStillAdmitted()
    {
        Assert.That(_store.GetLoadout(), Is.Empty);
        Assert.That(_store.GetPreparedEquipment().HasAnyEquipment, Is.False);
    }

    // ---- S1, S4: pre-start failure refunds ---------------------------------------------------------------

    [Test]
    public async Task PreStartFailure_RequestsTheRefundNotTheLossClear_AndRestoresLocalOwnership()
    {
        Start(LoopbackBackendServer.Reply.Ok("{\"pendingReservation\":null,\"revision\":9,\"refunded\":true}"));
        Set("_admittedReservation", _admitted);
        RaidLossNoticeSlot notice = CapturePendingLossNotice();

        await EndParticipation();

        Assert.That(_server.Requests.Select(r => r.Method + " " + r.Path),
            Is.EqualTo(new[] { "POST " + RefundPath }));
        Assert.That(JsonUtility.FromJson<Grimhold.Backend.RefundReservationRequest>(_server.Requests[0].Body).reservationId,
            Is.EqualTo(ReservationId));
        AssertLocalOwnershipRestoredOnce();
        Assert.That(_store.RemoteRevision, Is.EqualTo(9));
        Assert.That(notice.HasPending, Is.False, "A refunded failure carries no loss to report in Town.");
    }

    [Test]
    public async Task PreStartFailure_TransientFailureIsRetriedAndOwnershipIsRestoredOnlyOnce()
    {
        Start(LoopbackBackendServer.Reply.TransportFailure(),
            LoopbackBackendServer.Reply.Ok("{\"pendingReservation\":null,\"revision\":4,\"refunded\":true}"));
        Set("_admittedReservation", _admitted);

        await EndParticipation();

        Assert.That(_server.Requests.Select(r => r.Method + " " + r.Path),
            Is.EqualTo(new[] { "POST " + RefundPath, "POST " + RefundPath }));
        AssertLocalOwnershipRestoredOnce();
        // A second ending of the same participation must not restore (or request) anything again.
        await EndParticipation();
        Assert.That(_server.Requests, Has.Count.EqualTo(2));
        AssertLocalOwnershipRestoredOnce();
    }

    [Test]
    public async Task PreStartFailure_BackendNoOp_DoesNotRestoreLocallyNorApplyTheLossClear()
    {
        // Nothing was active on the backend (for example a retry whose first attempt already refunded):
        // the backend owns no items to give back and no reservation to consume.
        Start(LoopbackBackendServer.Reply.Ok("{\"pendingReservation\":null,\"revision\":9,\"refunded\":false}"));
        Set("_admittedReservation", _admitted);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
            "Pre-start refund found no active reservation.*" + ReservationId));

        await EndParticipation();

        Assert.That(_server.Requests.Select(r => r.Method + " " + r.Path),
            Is.EqualTo(new[] { "POST " + RefundPath }));
        AssertLocalOwnershipStillAdmitted();
    }

    [Test]
    public async Task PreStartFailure_TerminalRejectionFallsBackToTheLossClear()
    {
        Start(LoopbackBackendServer.Reply.Rejected(409, "RAID_ALREADY_STARTED"),
            LoopbackBackendServer.Reply.Ok("{\"pendingReservation\":null,\"revision\":6}"));
        Set("_admittedReservation", _admitted);
        RaidLossNoticeSlot notice = CapturePendingLossNotice();

        await EndParticipation();

        Assert.That(_server.Requests.Select(r => r.Method + " " + r.Path),
            Is.EqualTo(new[] { "POST " + RefundPath, "DELETE " + ReservationPath }));
        Assert.That(_server.Requests[1].Query, Does.Contain("expectedReservationId=" + ReservationId));
        Assert.That(_server.Requests[1].Query, Does.Not.Contain("rollback"));
        AssertLocalOwnershipStillAdmitted();
        Assert.That(_store.RemoteRevision, Is.EqualTo(6));
        Assert.That(notice.HasPending, Is.True, "A rejected refund is still a loss and keeps its Town notice.");
    }

    // ---- S8, S4: other endings stay losses / unchanged ---------------------------------------------------

    [Test]
    public async Task EndingAfterTheRaidReachedInProgress_NeverRequestsARefund()
    {
        Start(LoopbackBackendServer.Reply.Ok("{\"pendingReservation\":null,\"revision\":6}"));
        Set("_admittedReservation", _admitted);
        Set("_raidStartObserved", true);

        await EndParticipation();

        Assert.That(_server.Requests.Select(r => r.Method + " " + r.Path),
            Is.EqualTo(new[] { "DELETE " + ReservationPath }));
        AssertLocalOwnershipStillAdmitted();
    }

    [Test]
    public async Task EndingWithoutAnAdmittedSnapshot_KeepsTheLossClear()
    {
        Start(LoopbackBackendServer.Reply.Ok("{\"pendingReservation\":null,\"revision\":6}"));

        await EndParticipation();

        Assert.That(_server.Requests.Select(r => r.Method + " " + r.Path),
            Is.EqualTo(new[] { "DELETE " + ReservationPath }));
        AssertLocalOwnershipStillAdmitted();
    }

    [Test]
    public async Task ProxyThatSawTheRaidStart_IsALossAfterwards()
    {
        Start(LoopbackBackendServer.Reply.Ok("{\"pendingReservation\":null,\"revision\":6}"));
        Set("_admittedReservation", _admitted);

        ObserveRaid(isStateAuthority: false, started: true);
        await EndParticipation();

        Assert.That(_server.Requests.Select(r => r.Method + " " + r.Path),
            Is.EqualTo(new[] { "DELETE " + ReservationPath }));
    }

    // ---- S5: Host start marker ---------------------------------------------------------------------------

    private async Task AwaitMarkerReport() => await Get<Task>("_raidStartedReportTask");

    [Test]
    public async Task Host_ReportsRaidStartedOncePerRaid()
    {
        Start(LoopbackBackendServer.Reply.Ok("{\"raidId\":\"" + RaidId + "\",\"marked\":1}"));
        Set("_admittedReservation", _admitted);

        ObserveRaid(isStateAuthority: true, started: true);
        ObserveRaid(isStateAuthority: true, started: true);
        await AwaitMarkerReport();
        ObserveRaid(isStateAuthority: true, started: true);
        await AwaitMarkerReport();

        Assert.That(_server.Requests.Select(r => r.Method + " " + r.Path),
            Is.EqualTo(new[] { "POST " + RaidStartedPath }));
        Assert.That(JsonUtility.FromJson<Grimhold.Backend.RaidStartedRequest>(_server.Requests[0].Body).raidId, Is.EqualTo(RaidId));
    }

    [Test]
    public async Task Proxy_NeverReportsRaidStarted()
    {
        Start(LoopbackBackendServer.Reply.Ok("{\"raidId\":\"" + RaidId + "\",\"marked\":1}"));
        Set("_admittedReservation", _admitted);

        ObserveRaid(isStateAuthority: false, started: true);
        ObserveRaid(isStateAuthority: false, started: true);
        await AwaitMarkerReport();

        Assert.That(_server.Requests, Is.Empty);
    }

    [Test]
    public async Task Host_BeforeInProgress_DoesNotReportAndDoesNotLatchTheStart()
    {
        Start(LoopbackBackendServer.Reply.Ok("{\"raidId\":\"" + RaidId + "\",\"marked\":1}"));
        Set("_admittedReservation", _admitted);

        ObserveRaid(isStateAuthority: true, started: false);
        await AwaitMarkerReport();

        Assert.That(_server.Requests, Is.Empty);
        Assert.That(Get<bool>("_raidStartObserved"), Is.False);
    }

    [Test]
    public async Task Host_WithoutAnAdmittedReservation_DoesNotReport()
    {
        Start(LoopbackBackendServer.Reply.Ok("{\"raidId\":\"" + RaidId + "\",\"marked\":0}"));

        ObserveRaid(isStateAuthority: true, started: true);
        await AwaitMarkerReport();

        Assert.That(_server.Requests, Is.Empty);
    }

    [Test]
    public async Task Host_WaitsForTheAdmissionRequestBeforeReporting()
    {
        Start(LoopbackBackendServer.Reply.Ok("{\"raidId\":\"" + RaidId + "\",\"marked\":1}"));
        Set("_admittedReservation", _admitted);
        var admission = new TaskCompletionSource<bool>();
        Set("_reservationAdmissionTask", admission.Task);

        ObserveRaid(isStateAuthority: true, started: true);
        await Task.Delay(100);
        Assert.That(_server.Requests, Is.Empty, "raid-started needs the admitted reservation on the backend.");

        admission.SetResult(true);
        await AwaitMarkerReport();
        Assert.That(_server.Requests.Select(r => r.Path), Is.EqualTo(new[] { RaidStartedPath }));
    }

    [Test]
    public async Task Host_ReportFailure_IsLoggedNeverThrownAndNotRepeated()
    {
        Start(LoopbackBackendServer.Reply.Rejected(403, "HOST_NOT_IN_RAID"));
        Set("_admittedReservation", _admitted);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*raid-started.*HOST_NOT_IN_RAID.*"));

        ObserveRaid(isStateAuthority: true, started: true);
        await AwaitMarkerReport();
        ObserveRaid(isStateAuthority: true, started: true);
        await AwaitMarkerReport();

        Assert.That(_server.Requests, Has.Count.EqualTo(1));
    }
}

/// <summary>PRESERVE: the pre-admission rollback keeps using the rollback endpoint and never the refund.</summary>
public sealed class PreAdmissionRollbackPreservedTests
{
    private static readonly ProfileId Profile = new("57575757575757575757575757575757");
    private GameObject _root;
    private LoopbackBackendServer _server;
    private RemoteServiceFixture _remote;
    private ProfileId _previousProfile;

    [SetUp]
    public void SetUp() => _previousProfile = LocalProfileProvider.GetOrCreateLocalProfile();

    [TearDown]
    public void TearDown()
    {
        _remote?.Dispose();
        _server?.Dispose();
        LocalProfileProvider.SetRemoteCharacterId(_previousProfile);
    }

    [Test]
    public async Task PreAdmissionRollback_RestoresLocallyAndCallsRollbackEndpointOnly()
    {
        LocalProfileProvider.SetRemoteCharacterId(Profile);
        var catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
            "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
        var repository = new InMemoryLocalProfileRepository();
        Assert.That(repository.Initialize(Profile, catalog), Is.True);
        LocalProfileSnapshot snapshot = repository.Snapshot.Clone();
        snapshot.Loadout.Add(new StashItem(new LootId("bone"), 2, 0));
        snapshot.PreparedEquipment = new PreparedEquipmentLoadout(new LootId("rapier"), default);
        Assert.That(repository.TrySave(snapshot, out string error), Is.True, error);
        var store = new LocalProfileStore(repository, Profile, catalog);
        Assert.That(store.TryCreateLoadoutReservation("reservation-r", out PendingLoadoutReservation reservation),
            Is.EqualTo(StashOperationResult.Success));

        _server = new LoopbackBackendServer(LoopbackBackendServer.Reply.Ok("{\"pendingReservation\":null,\"revision\":3}"));
        _remote = new RemoteServiceFixture(_server.BaseUrl, store);
        _root = _remote.Root;
        var loadoutService = _root.AddComponent<InMemoryPlayerLoadoutService>();
        loadoutService.Initialize(store);
        var context = _root.AddComponent<ApplicationStashContext>();
        context.Initialize(store, null, loadoutService, null, null);
        var coordinator = _root.AddComponent<SessionConnectionCoordinator>();

        Assert.That(RaidCode.TryParse("123456", out RaidCode code), Is.True);
        Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId team), Is.True);
        Assert.That(RaidLaunchContext.TryCreate(code, Profile, new[] { new RaidLaunchParticipant(Profile, team) },
            Profile, 1, out RaidLaunchContext launch), Is.True);
        var ticket = new RaidTransitionTicket(new RaidConnectionRequest(code, RaidConnectionRole.Host),
            reservation, SessionConnectionState.Town, launch);
        typeof(SessionConnectionCoordinator).GetField("_activeTicket", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(coordinator, ticket);

        bool rolledBack = (bool)typeof(SessionConnectionCoordinator)
            .GetMethod("TryRollbackActiveReservation", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(coordinator, null);

        Assert.That(rolledBack, Is.True);
        Assert.That(store.PendingReservation, Is.Null);
        Assert.That(store.GetLoadout(), Is.EqualTo(new[] { new StashItem(new LootId("bone"), 2, 0) }));
        Assert.That(store.GetPreparedEquipment().WeaponSetAMainHand, Is.EqualTo(new LootId("rapier")));
        // The backend rollback is the existing fire-and-forget call: wait for its single request.
        for (int i = 0; i < 100 && _server.Requests.Count == 0; i++) await Task.Delay(50);
        Assert.That(_server.Requests.Select(r => r.Method + " " + r.Path),
            Is.EqualTo(new[] { "DELETE /character/me/inventory/reservation" }));
        Assert.That(_server.Requests[0].Query, Does.Contain("rollback=true"));
        Assert.That(_server.Requests[0].Query, Does.Contain("expectedReservationId=reservation-r"));
    }
}
#endif

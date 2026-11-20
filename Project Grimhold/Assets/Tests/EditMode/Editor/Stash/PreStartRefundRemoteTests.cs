#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>FIX-95 client side of the refund and raid-started endpoints, through the real HTTP boundary.</summary>
public sealed class PreStartRefundRemoteTests
{
    private const string RefundPath = "/character/me/inventory/reservation/refund";
    private const string RaidStartedPath = "/character/me/inventory/reservation/raid-started";
    private readonly ProfileId _profile = new("47474747474747474747474747474747");
    private LocalProfileStore _store;

    [SetUp]
    public void SetUp()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
            "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
        var repository = new InMemoryLocalProfileRepository();
        Assert.That(repository.Initialize(_profile, catalog), Is.True);
        _store = new LocalProfileStore(repository, _profile, catalog);
    }

    [Test]
    public async Task Refund_PostsTheReservationIdAndAdvancesTheRevision()
    {
        using var server = new LoopbackBackendServer(
            LoopbackBackendServer.Reply.Ok("{\"pendingReservation\":null,\"revision\":9}"));
        using var fixture = new RemoteServiceFixture(server.BaseUrl, _store);

        var (success, error) = await fixture.Service.RefundPendingReservationAsync("reservation-a");

        Assert.That(success, Is.True, error.error);
        Assert.That(server.Requests, Has.Count.EqualTo(1));
        Assert.That(server.Requests[0].Method, Is.EqualTo("POST"));
        Assert.That(server.Requests[0].Path, Is.EqualTo(RefundPath));
        Assert.That(JsonUtility.FromJson<RefundReservationRequest>(server.Requests[0].Body).reservationId,
            Is.EqualTo("reservation-a"));
        Assert.That(_store.RemoteRevision, Is.EqualTo(9));
    }

    [Test]
    public async Task Refund_TransientTransportFailure_IsRetriedWithTheSameReservationAndSucceedsOnce()
    {
        using var server = new LoopbackBackendServer(
            LoopbackBackendServer.Reply.TransportFailure(),
            LoopbackBackendServer.Reply.Ok("{\"pendingReservation\":null,\"revision\":4}"));
        using var fixture = new RemoteServiceFixture(server.BaseUrl, _store);

        var (success, error) = await fixture.Service.RefundPendingReservationAsync("reservation-a");

        Assert.That(success, Is.True, error.error);
        Assert.That(server.Requests, Has.Count.EqualTo(2));
        foreach (var request in server.Requests)
        {
            Assert.That(request.Path, Is.EqualTo(RefundPath));
            Assert.That(JsonUtility.FromJson<RefundReservationRequest>(request.Body).reservationId,
                Is.EqualTo("reservation-a"));
        }
        Assert.That(fixture.Delays, Has.Count.EqualTo(1));
        Assert.That(_store.RemoteRevision, Is.EqualTo(4));
    }

    [TestCase("RESERVATION_MISMATCH")]
    [TestCase("RAID_ALREADY_STARTED")]
    [TestCase("REFUND_WINDOW_EXPIRED")]
    [TestCase("EXTRACTION_RESULT_PENDING")]
    [TestCase("RESULT_ALREADY_CONSUMED")]
    public async Task Refund_TerminalRejection_StopsAfterOneAttemptAndLeavesTheRevision(string code)
    {
        using var server = new LoopbackBackendServer(LoopbackBackendServer.Reply.Rejected(409, code));
        using var fixture = new RemoteServiceFixture(server.BaseUrl, _store);
        int revisionBefore = _store.RemoteRevision;

        var (success, error) = await fixture.Service.RefundPendingReservationAsync("reservation-a");

        Assert.That(success, Is.False);
        Assert.That(error.error, Is.EqualTo(code));
        Assert.That(server.Requests, Has.Count.EqualTo(1));
        Assert.That(fixture.Delays, Is.Empty);
        Assert.That(_store.RemoteRevision, Is.EqualTo(revisionBefore));
    }

    [Test]
    public async Task RaidStarted_PostsTheRaidId()
    {
        using var server = new LoopbackBackendServer(
            LoopbackBackendServer.Reply.Ok("{\"raidId\":\"raid-123456\",\"marked\":2}"));
        using var fixture = new RemoteServiceFixture(server.BaseUrl, _store);

        var (success, error) = await fixture.Service.ReportRaidStartedAsync("raid-123456");

        Assert.That(success, Is.True, error.error);
        Assert.That(server.Requests, Has.Count.EqualTo(1));
        Assert.That(server.Requests[0].Method, Is.EqualTo("POST"));
        Assert.That(server.Requests[0].Path, Is.EqualTo(RaidStartedPath));
        Assert.That(JsonUtility.FromJson<RaidStartedRequest>(server.Requests[0].Body).raidId, Is.EqualTo("raid-123456"));
    }

    [Test]
    public async Task RaidStarted_TransientTransportFailure_IsRetriedIdempotently()
    {
        using var server = new LoopbackBackendServer(
            LoopbackBackendServer.Reply.TransportFailure(),
            LoopbackBackendServer.Reply.Ok("{\"raidId\":\"raid-123456\",\"marked\":0}"));
        using var fixture = new RemoteServiceFixture(server.BaseUrl, _store);

        var (success, error) = await fixture.Service.ReportRaidStartedAsync("raid-123456");

        Assert.That(success, Is.True, error.error);
        Assert.That(server.Requests, Has.Count.EqualTo(2));
        Assert.That(server.Requests[1].Body, Is.EqualTo(server.Requests[0].Body));
    }

    [Test]
    public async Task RaidStarted_HostNotInRaid_IsTerminalAndNotRetried()
    {
        using var server = new LoopbackBackendServer(LoopbackBackendServer.Reply.Rejected(403, "HOST_NOT_IN_RAID"));
        using var fixture = new RemoteServiceFixture(server.BaseUrl, _store);

        var (success, error) = await fixture.Service.ReportRaidStartedAsync("raid-123456");

        Assert.That(success, Is.False);
        Assert.That(error.error, Is.EqualTo("HOST_NOT_IN_RAID"));
        Assert.That(server.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Unauthenticated_NeitherCallReachesTheBackend()
    {
        using var server = new LoopbackBackendServer(LoopbackBackendServer.Reply.Ok("{}"));
        using var fixture = new RemoteServiceFixture(server.BaseUrl, _store);
        ApplicationAuthContext.Instance.Clear();
        LogAssert.Expect(LogType.Error, "[RemoteInventoryService] RefundPendingReservationAsync: Not authenticated.");
        LogAssert.Expect(LogType.Error, "[RemoteInventoryService] ReportRaidStartedAsync: Not authenticated.");

        var refund = await fixture.Service.RefundPendingReservationAsync("reservation-a");
        var started = await fixture.Service.ReportRaidStartedAsync("raid-123456");

        Assert.That(refund.success, Is.False);
        Assert.That(refund.error.error, Is.EqualTo("UNAUTHORIZED"));
        Assert.That(started.success, Is.False);
        Assert.That(started.error.error, Is.EqualTo("UNAUTHORIZED"));
        Assert.That(server.Requests, Is.Empty);
    }
}
#endif

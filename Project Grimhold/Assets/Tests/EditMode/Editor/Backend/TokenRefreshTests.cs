#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Grimhold.Tests.Backend
{
    /// <summary>
    /// FIX-34: an expired access token is renewed once, transparently, at the shared authenticated HTTP
    /// layer. Every case goes through a real loopback HTTP boundary and the public clients/services.
    /// </summary>
    [TestFixture]
    [Category("FIX34")]
    public sealed class TokenRefreshTests
    {
        private const string OldAccess = "access-old";
        private const string NewAccess = "access-new";
        private const string FirstRefresh = "refresh-1";
        private const string NextRefresh = "refresh-2";
        private const string RefreshPath = "/auth/refresh";
        private const string CommitPath = "/character/me/extraction/commit";
        private const string InventoryPath = "/character/me/inventory";
        private const string RefreshOk =
            "{\"token\":\"" + NewAccess + "\",\"expiresIn\":60,\"refreshToken\":\"" + NextRefresh + "\",\"refreshExpiresIn\":86400}";
        private const string CommitOk = "{\"revision\":7,\"alreadySecured\":false}";
        private const string InventoryOk = "{\"revision\":3}";

        private readonly ProfileId _profile = new("47474747474747474747474747474747");
        private BackendConfiguration _config;
        private GameObject _authObject;
        private ApplicationAuthContext _auth;
        private RemoteInventoryService _service;
        private GameObject _stashObject;
        private ApplicationStashContext _stashMarker;
        private bool _sessionInvalidated;
        private int _refreshCalls;

        [SetUp]
        public void SetUp()
        {
            LocalProfileProvider.SetRemoteCharacterId(_profile);
            _config = ScriptableObject.CreateInstance<BackendConfiguration>();
            _authObject = new GameObject("TokenRefreshAuth");
            _auth = _authObject.AddComponent<ApplicationAuthContext>();
            // EditMode does not run MonoBehaviour.Awake automatically for this component.
            if (ApplicationAuthContext.Instance != _auth)
                typeof(ApplicationAuthContext).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_auth, null);
            Assert.That(ApplicationAuthContext.Instance, Is.SameAs(_auth));
            _auth.SessionInvalidated += () => _sessionInvalidated = true;
            _sessionInvalidated = false;
            _refreshCalls = 0;

            // A stash composition root with a service on it: ResetForLogout would destroy that service.
            _stashObject = new GameObject("TokenRefreshStash");
            _stashMarker = _stashObject.AddComponent<ApplicationStashContext>();
            _service = _stashObject.AddComponent<RemoteInventoryService>();

            RemoteIdempotentRetryPolicy.BackoffMs = 0;
            RemoteIdempotentRetryPolicy.DelayAsync = _ => Task.CompletedTask;
        }

        [TearDown]
        public void TearDown()
        {
            RemoteIdempotentRetryPolicy.BackoffMs = 1000;
            RemoteIdempotentRetryPolicy.DelayAsync = Task.Delay;
            typeof(ApplicationStashServiceBootstrapper)
                .GetField("_context", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            LocalProfileProvider.ClearRemoteCharacterId();
            if (_stashObject != null) UnityEngine.Object.DestroyImmediate(_stashObject);
            if (_authObject != null) UnityEngine.Object.DestroyImmediate(_authObject);
            if (_config != null) UnityEngine.Object.DestroyImmediate(_config);
        }

        // ------------------------------------------------------------------
        // S1 / S3 — the extraction commit survives an expired access token
        // ------------------------------------------------------------------

        [Test]
        public async Task ExtractionCommit_AccessTokenExpired_RefreshesRetriesOnceAndKeepsTheSession()
        {
            using var server = new LoopbackBackendServer(request => Route(request, expiredCode: "TOKEN_EXPIRED"));
            StartSession(server);

            var (success, result, error) = await _service.CommitExtractionUnifiedAsync(
                new ExtractionReceipt("raid-1", _profile, 1),
                new[] { new StashItem(new LootId("bone"), 2, 0) },
                default, 100, 2);

            Assert.That(success, Is.True, error.error);
            Assert.That(result.revision, Is.EqualTo(7));
            var requests = server.Requests;
            Assert.That(requests.Select(r => r.Path), Is.EqualTo(new[] { CommitPath, RefreshPath, CommitPath }));
            Assert.That(requests[0].Authorization, Is.EqualTo("Bearer " + OldAccess));
            Assert.That(requests[1].Authorization, Is.Null, "The refresh request is unauthenticated.");
            Assert.That(JsonUtility.FromJson<RefreshRequest>(requests[1].Body).refreshToken, Is.EqualTo(FirstRefresh));
            Assert.That(requests[2].Authorization, Is.EqualTo("Bearer " + NewAccess));
            Assert.That(requests[2].Body, Is.EqualTo(requests[0].Body), "The original request is replayed unchanged.");
            AssertSessionKept(expectedAccess: NewAccess, expectedRefresh: NextRefresh);
        }

        [Test]
        public async Task ConcurrentExpiredRequests_ShareASingleRefreshCall()
        {
            using var server = new LoopbackBackendServer(request => Route(request, "TOKEN_EXPIRED", refreshDelayMs: 150));
            StartSession(server);

            var calls = Enumerable.Range(0, 4)
                .Select(_ => InventoryClient.GetInventoryAsync(_config, OldAccess)).ToArray();
            var results = await Task.WhenAll(calls);

            Assert.That(results.All(r => r.success), Is.True, string.Join(",", results.Select(r => r.error.error)));
            Assert.That(server.Requests.Count(r => r.Path == RefreshPath), Is.EqualTo(1));
            Assert.That(server.Requests.Where(r => r.Path == InventoryPath && r.Authorization == "Bearer " + NewAccess).Count(),
                Is.EqualTo(4), "Every request is replayed exactly once with the renewed token.");
            AssertSessionKept(NewAccess, NextRefresh);
        }

        [Test]
        public async Task RequestStartedWhileRefreshing_WaitsForTheRenewedTokenInsteadOfSendingTheExpiredOne()
        {
            using var server = new LoopbackBackendServer(request => Route(request, "TOKEN_EXPIRED", refreshDelayMs: 400));
            StartSession(server);

            var first = InventoryClient.GetInventoryAsync(_config, OldAccess);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!_auth.IsRefreshInFlight && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.That(_auth.IsRefreshInFlight, Is.True, "The first request never started a refresh.");

            var second = await InventoryClient.GetInventoryAsync(_config, _auth.Token);
            var firstResult = await first;

            Assert.That(firstResult.success, Is.True, firstResult.error.error);
            Assert.That(second.success, Is.True, second.error.error);
            var inventoryCalls = server.Requests.Where(r => r.Path == InventoryPath).ToList();
            Assert.That(inventoryCalls.Count(r => r.Authorization == "Bearer " + OldAccess), Is.EqualTo(1),
                "Only the first request may ever carry the expired token.");
            Assert.That(inventoryCalls.Count(r => r.Authorization == "Bearer " + NewAccess), Is.EqualTo(2));
            Assert.That(server.Requests.Count(r => r.Path == RefreshPath), Is.EqualTo(1));
        }

        [Test]
        public async Task StaleCapturedToken_AfterARefresh_UsesTheRenewedTokenWithoutAnotherRefresh()
        {
            using var server = new LoopbackBackendServer(request => Route(request, "TOKEN_EXPIRED"));
            StartSession(server);
            Assert.That((await InventoryClient.GetInventoryAsync(_config, OldAccess)).success, Is.True);

            // A service that captured the pre-refresh token (a verifier, a pending retry) still works.
            var stale = await InventoryClient.GetInventoryAsync(_config, OldAccess);

            Assert.That(stale.success, Is.True, stale.error.error);
            Assert.That(server.Requests.Count(r => r.Path == RefreshPath), Is.EqualTo(1));
            Assert.That(server.Requests.Last().Authorization, Is.EqualTo("Bearer " + NewAccess));
            AssertSessionKept(NewAccess, NextRefresh);
        }

        [Test]
        public async Task SessionVerifiers_UseTheRenewedTokenAfterARefresh()
        {
            using var server = new LoopbackBackendServer(request => Route(request, "TOKEN_EXPIRED"));
            StartSession(server);
            Assert.That((await InventoryClient.GetInventoryAsync(_config, OldAccess)).success, Is.True);

            await _auth.TownTicketVerifier.VerifyTownTicketAsync("raw-ticket");

            var ticketCall = server.Requests.Last(r => r.Path != RefreshPath);
            Assert.That(ticketCall.Path, Does.Not.EqualTo(InventoryPath));
            Assert.That(ticketCall.Authorization, Is.EqualTo("Bearer " + NewAccess));
            Assert.That(server.Requests.Count(r => r.Authorization == "Bearer " + OldAccess), Is.EqualTo(1),
                "The verifier must not rediscover the expiry on its own.");
        }

        [Test]
        public async Task Logout_AccessTokenExpired_RefreshesSoTheBackendCanRevokeTheFamily()
        {
            using var server = new LoopbackBackendServer(request => Route(request, "TOKEN_EXPIRED"));
            StartSession(server);

            bool loggedOut = await AuthenticationClient.PostLogoutAsync(_config, OldAccess);

            Assert.That(loggedOut, Is.True);
            Assert.That(server.Requests.Select(r => r.Path), Is.EqualTo(new[] { "/auth/logout", RefreshPath, "/auth/logout" }));
            Assert.That(server.Requests.Last().Authorization, Is.EqualTo("Bearer " + NewAccess));
        }

        // ------------------------------------------------------------------
        // S2 — real unauthorized states keep the existing close-session path
        // ------------------------------------------------------------------

        [Test]
        public async Task RefreshInvalid_ReportsUnauthorizedAndTheCallerClosesTheSession()
        {
            using var server = new LoopbackBackendServer(request =>
                request.Path == RefreshPath ? CountRefresh(LoopbackBackendServer.Reply.Rejected(401, "REFRESH_INVALID"))
                : LoopbackBackendServer.Reply.Rejected(401, "TOKEN_EXPIRED"));
            StartSession(server, registerStash: false);

            var (success, _, error) = await CommitExtraction();

            Assert.That(success, Is.False);
            Assert.That(error.error, Is.EqualTo(BackendErrorUtility.Unauthorized));
            Assert.That(server.Requests.Select(r => r.Path), Is.EqualTo(new[] { CommitPath, RefreshPath }),
                "A rejected refresh must not replay the request.");
            AssertSessionClosed();
            Assert.That(BackendErrorMessages.ForError(error), Does.Contain("Vuelve a iniciar sesión"));
        }

        [Test]
        public async Task RevokedToken_Unauthorized_NeverRefreshesAndClosesTheSession()
        {
            using var server = new LoopbackBackendServer(request => LoopbackBackendServer.Reply.Rejected(401, "UNAUTHORIZED"));
            StartSession(server, registerStash: false);

            var (success, _, error) = await CommitExtraction();

            Assert.That(success, Is.False);
            Assert.That(error.error, Is.EqualTo(BackendErrorUtility.Unauthorized));
            Assert.That(server.Requests.Select(r => r.Path), Is.EqualTo(new[] { CommitPath }));
            AssertSessionClosed();
        }

        [Test]
        public async Task SecondTokenExpiredAfterRefresh_DoesNotLoopAndClosesTheSession()
        {
            using var server = new LoopbackBackendServer(request =>
                request.Path == RefreshPath ? CountRefresh(LoopbackBackendServer.Reply.Ok(RefreshOk))
                : LoopbackBackendServer.Reply.Rejected(401, "TOKEN_EXPIRED"));
            StartSession(server, registerStash: false);

            var (success, _, error) = await CommitExtraction();

            Assert.That(success, Is.False);
            Assert.That(error.error, Is.EqualTo(BackendErrorUtility.Unauthorized));
            Assert.That(server.Requests.Select(r => r.Path), Is.EqualTo(new[] { CommitPath, RefreshPath, CommitPath }));
            AssertSessionClosed();
        }

        [Test]
        public async Task TokenExpired_WithoutARefreshToken_ReportsUnauthorizedAndClosesTheSession()
        {
            using var server = new LoopbackBackendServer(request => LoopbackBackendServer.Reply.Rejected(401, "TOKEN_EXPIRED"));
            StartSession(server, refreshToken: null, registerStash: false);

            var (success, _, error) = await CommitExtraction();

            Assert.That(success, Is.False);
            Assert.That(error.error, Is.EqualTo(BackendErrorUtility.Unauthorized));
            Assert.That(server.Requests.Select(r => r.Path), Is.EqualTo(new[] { CommitPath }));
            AssertSessionClosed();
        }

        [Test]
        public async Task RefreshTransportFailure_KeepsTheSessionAndTheRefreshTokenForALaterAttempt()
        {
            using var server = new LoopbackBackendServer(request =>
                request.Path == RefreshPath ? CountRefresh(LoopbackBackendServer.Reply.TransportFailure())
                : LoopbackBackendServer.Reply.Rejected(401, "TOKEN_EXPIRED"));
            StartSession(server);

            var (success, _, error) = await InventoryClient.GetInventoryAsync(_config, OldAccess);

            Assert.That(success, Is.False);
            Assert.That(BackendErrorUtility.IsTransportFailure(error.error), Is.True, error.error);
            Assert.That(_sessionInvalidated, Is.False);
            Assert.That(_refreshCalls, Is.EqualTo(1), "A failed refresh is never retried with the same refresh token.");
            Assert.That(server.Requests.Count(r => r.Path == InventoryPath), Is.EqualTo(1), "The request is not replayed.");
            AssertSessionKept(OldAccess, FirstRefresh);
        }

        [TestCase(400, "VALIDATION_FAILED")]
        [TestCase(429, "TOO_MANY_REQUESTS")]
        [TestCase(500, "INTERNAL_SERVER_ERROR")]
        public async Task RefreshRejectedWithoutInvalidatingCredentials_IsATransientFailureThatKeepsTheSession(int status, string code)
        {
            using var server = new LoopbackBackendServer(request =>
                request.Path == RefreshPath ? CountRefresh(LoopbackBackendServer.Reply.Rejected(status, code))
                : LoopbackBackendServer.Reply.Rejected(401, "TOKEN_EXPIRED"));
            StartSession(server);

            var (success, _, error) = await InventoryClient.GetInventoryAsync(_config, OldAccess);

            Assert.That(success, Is.False);
            Assert.That(error.error, Is.EqualTo(code), "The refresh failure is surfaced, not disguised as UNAUTHORIZED.");
            Assert.That(_refreshCalls, Is.EqualTo(1));
            Assert.That(server.Requests.Count(r => r.Path == InventoryPath), Is.EqualTo(1));
            AssertSessionKept(OldAccess, FirstRefresh);
        }

        [Test]
        public async Task SessionClearedWhileRefreshing_DiscardsTheRenewedTokens()
        {
            using var server = new LoopbackBackendServer(request => Route(request, "TOKEN_EXPIRED", refreshDelayMs: 200));
            StartSession(server);

            var pending = InventoryClient.GetInventoryAsync(_config, OldAccess);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!_auth.IsRefreshInFlight && DateTime.UtcNow < deadline) await Task.Delay(10);
            _auth.Clear();
            var (success, _, error) = await pending;

            Assert.That(success, Is.False);
            Assert.That(error.error, Is.Not.EqualTo("TOKEN_EXPIRED"));
            Assert.That(_auth.Token, Is.Null, "A cleared session must not be resurrected by a late refresh.");
            Assert.That(_auth.RefreshToken, Is.Null);
            Assert.That(server.Requests.Count(r => r.Path == InventoryPath), Is.EqualTo(1));
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private LoopbackBackendServer.Reply Route(LoopbackBackendServer.Recorded request, string expiredCode, int refreshDelayMs = 0)
        {
            if (request.Path == RefreshPath)
            {
                bool valid = JsonUtility.FromJson<RefreshRequest>(request.Body).refreshToken == FirstRefresh && _refreshCalls == 0;
                var reply = CountRefresh(valid
                    ? LoopbackBackendServer.Reply.Ok(RefreshOk)
                    : LoopbackBackendServer.Reply.Rejected(401, "REFRESH_INVALID"));
                return refreshDelayMs > 0 ? reply.After(refreshDelayMs) : reply;
            }

            if (request.Authorization != "Bearer " + NewAccess)
                return LoopbackBackendServer.Reply.Rejected(401, expiredCode);
            return LoopbackBackendServer.Reply.Ok(request.Path == CommitPath ? CommitOk : InventoryOk);
        }

        private LoopbackBackendServer.Reply CountRefresh(LoopbackBackendServer.Reply reply)
        {
            _refreshCalls++;
            return reply;
        }

        /// <param name="registerStash">
        /// True makes the stash root the live one, so an (unwanted) reset would destroy the service. Sessions
        /// expected to close leave it unregistered: Destroy is not allowed in EditMode.
        /// </param>
        private void StartSession(LoopbackBackendServer server, string refreshToken = FirstRefresh, bool registerStash = true)
        {
            _config.BaseUrl = server.BaseUrl;
            _auth.Initialize(OldAccess, new CharacterData { characterId = _profile.Value, name = "Test" }, default,
                _config, refreshToken);
            if (registerStash)
                typeof(ApplicationStashServiceBootstrapper)
                    .GetField("_context", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _stashMarker);
            typeof(RemoteInventoryService).GetField("_backendConfig", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_service, _config);
        }

        private Task<(bool success, CommitExtractionUnifiedResult result, BackendError error)> CommitExtraction() =>
            _service.CommitExtractionUnifiedAsync(
                new ExtractionReceipt("raid-1", _profile, 1),
                new[] { new StashItem(new LootId("bone"), 2, 0) }, default, 100, 2);

        private void AssertSessionKept(string expectedAccess, string expectedRefresh)
        {
            Assert.That(_sessionInvalidated, Is.False, "A renewable expiry must not invalidate the session.");
            Assert.That(_auth.IsAuthenticated, Is.True);
            Assert.That(_auth.Token, Is.EqualTo(expectedAccess));
            Assert.That(_auth.RefreshToken, Is.EqualTo(expectedRefresh));
            Assert.That(_service != null && _stashMarker != null, Is.True, "Stash services must survive the renewal.");
            Assert.That(LocalProfileProvider.GetOrCreateLocalProfile(), Is.EqualTo(_profile));
        }

        private void AssertSessionClosed()
        {
            Assert.That(_sessionInvalidated, Is.True, "SessionInvalidated must fire on the existing unauthorized path.");
            Assert.That(_auth.IsAuthenticated, Is.False);
            Assert.That(_auth.Token, Is.Null);
            Assert.That(_auth.RefreshToken, Is.Null, "The refresh token is cleared wherever the access token is.");
            Assert.That(LocalProfileProvider.GetOrCreateLocalProfile().IsValid, Is.False);
        }
    }
}
#endif

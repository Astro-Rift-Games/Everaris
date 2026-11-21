#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEngine;

namespace Grimhold.Tests.Backend
{
    /// <summary>FIX-34: wire contract and in-memory storage of the refresh token.</summary>
    [TestFixture]
    [Category("FIX34")]
    public sealed class RefreshSessionContractTests
    {
        private const string TokensJson =
            "{\"token\":\"access-a\",\"expiresIn\":60,\"refreshToken\":\"refresh-a\",\"refreshExpiresIn\":86400}";

        private BackendConfiguration _config;
        private GameObject _authObject;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<BackendConfiguration>();

        [TearDown]
        public void TearDown()
        {
            LocalProfileProvider.ClearRemoteCharacterId();
            if (_authObject != null) Object.DestroyImmediate(_authObject);
            if (_config != null) Object.DestroyImmediate(_config);
        }

        [Test]
        public async Task Login_ReturnsTheRefreshTokenAndItsLifetime()
        {
            using var server = new LoopbackBackendServer(LoopbackBackendServer.Reply.Ok(TokensJson));
            _config.BaseUrl = server.BaseUrl;

            var (success, result, error) = await AuthenticationClient.PostLoginAsync(_config, "alice", "secret1");

            Assert.That(success, Is.True, error.error);
            Assert.That(result.token, Is.EqualTo("access-a"));
            Assert.That(result.expiresIn, Is.EqualTo(60));
            Assert.That(result.refreshToken, Is.EqualTo("refresh-a"));
            Assert.That(result.refreshExpiresIn, Is.EqualTo(86400));
            Assert.That(server.Requests[0].Path, Is.EqualTo("/auth/login"));
        }

        [Test]
        public async Task Register_ReturnsTheRefreshTokenAndItsLifetime()
        {
            using var server = new LoopbackBackendServer(LoopbackBackendServer.Reply.Ok(TokensJson));
            _config.BaseUrl = server.BaseUrl;

            var (success, result, error) = await AuthenticationClient.PostRegisterAsync(_config, "alice", "secret1");

            Assert.That(success, Is.True, error.error);
            Assert.That(result.refreshToken, Is.EqualTo("refresh-a"));
            Assert.That(result.refreshExpiresIn, Is.EqualTo(86400));
            Assert.That(server.Requests[0].Path, Is.EqualTo("/auth/register"));
        }

        [Test]
        public async Task Refresh_PostsTheRefreshTokenWithoutBearerCredentialAndReturnsTheNewPair()
        {
            using var server = new LoopbackBackendServer(LoopbackBackendServer.Reply.Ok(TokensJson));
            _config.BaseUrl = server.BaseUrl;

            var (success, result, error) = await AuthenticationClient.PostRefreshAsync(_config, "refresh-0");

            Assert.That(success, Is.True, error.error);
            Assert.That(result.token, Is.EqualTo("access-a"));
            Assert.That(result.expiresIn, Is.EqualTo(60));
            Assert.That(result.refreshToken, Is.EqualTo("refresh-a"));
            Assert.That(result.refreshExpiresIn, Is.EqualTo(86400));
            var request = server.Requests[0];
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo("/auth/refresh"));
            Assert.That(request.Authorization, Is.Null);
            Assert.That(JsonUtility.FromJson<RefreshRequest>(request.Body).refreshToken, Is.EqualTo("refresh-0"));
        }

        [Test]
        public async Task Refresh_Rejected_ReportsTheBackendErrorCode()
        {
            using var server = new LoopbackBackendServer(LoopbackBackendServer.Reply.Rejected(401, "REFRESH_INVALID"));
            _config.BaseUrl = server.BaseUrl;

            var (success, _, error) = await AuthenticationClient.PostRefreshAsync(_config, "refresh-0");

            Assert.That(success, Is.False);
            Assert.That(error.error, Is.EqualTo("REFRESH_INVALID"));
        }

        [Test]
        public async Task Refresh_MalformedSuccessBody_IsReportedAsAnInvalidResponseInsteadOfThrowing()
        {
            using var server = new LoopbackBackendServer(LoopbackBackendServer.Reply.Ok("<html>not json</html>"));
            _config.BaseUrl = server.BaseUrl;

            var (success, _, error) = await AuthenticationClient.PostRefreshAsync(_config, "refresh-0");

            Assert.That(success, Is.False);
            Assert.That(error.error, Is.EqualTo("INVALID_REFRESH_RESPONSE"));
        }

        [Test]
        public void Context_KeepsTheRefreshTokenInMemoryAndClearsItWithTheAccessToken()
        {
            var context = NewContext();
            context.Initialize("access-a", new CharacterData { characterId = "char-1", name = "Hero" }, default, null, "refresh-a");

            Assert.That(context.RefreshToken, Is.EqualTo("refresh-a"));

            context.Clear();

            Assert.That(context.Token, Is.Null);
            Assert.That(context.RefreshToken, Is.Null);
        }

        [Test]
        public void Context_Unauthorized_ClearsTheRefreshToken()
        {
            var context = NewContext();
            LocalProfileProvider.SetRemoteCharacterId(new ProfileId("char-1"));
            context.Initialize("access-a", new CharacterData { characterId = "char-1", name = "Hero" }, default, null, "refresh-a");

            context.HandleUnauthorizedSession();

            Assert.That(context.Token, Is.Null);
            Assert.That(context.RefreshToken, Is.Null);
        }

        [Test]
        public void Context_InitializeWithoutRefreshToken_KeepsTheLegacyBehaviour()
        {
            var context = NewContext();
            context.Initialize("access-a", new CharacterData { characterId = "char-1", name = "Hero" }, default);

            Assert.That(context.IsAuthenticated, Is.True);
            Assert.That(context.Token, Is.EqualTo("access-a"));
            Assert.That(context.RefreshToken, Is.Null);
        }

        [Test]
        public async Task LoginFlow_StoresTheRefreshTokenInTheSessionContext()
        {
            var context = NewContext();
            var controllerObject = new GameObject("RefreshLoginController");
            try
            {
                var controller = controllerObject.AddComponent<LoginFlowController>();
                controller.ConfigureForTesting(new RefreshingLoginBackend(), null, context);

                var result = await controller.ExecuteLoginAsync("alice", "secret1");

                Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
                Assert.That(context.Token, Is.EqualTo("access-a"));
                Assert.That(context.RefreshToken, Is.EqualTo("refresh-a"));
            }
            finally { Object.DestroyImmediate(controllerObject); }
        }

        private ApplicationAuthContext NewContext()
        {
            _authObject = new GameObject("RefreshContractAuth");
            return _authObject.AddComponent<ApplicationAuthContext>();
        }

        private sealed class RefreshingLoginBackend : ILoginFlowBackend
        {
            public Task<(bool success, LoginResult result, BackendError error)> PostLoginAsync(
                BackendConfiguration config, string username, string password)
                => Task.FromResult<(bool, LoginResult, BackendError)>(
                    (true, new LoginResult { token = "access-a", refreshToken = "refresh-a", refreshExpiresIn = 86400 }, default));

            public Task<(bool success, CharacterData data, BackendError error)> GetCharacterAsync(BackendConfiguration config, string token)
                => Task.FromResult<(bool, CharacterData, BackendError)>((true, new CharacterData { characterId = "char-1", name = "Hero" }, default));

            public Task<(bool success, CharacterProfileData data, BackendError error)> GetProfileAsync(BackendConfiguration config, string token)
                => Task.FromResult<(bool, CharacterProfileData, BackendError)>((true, new CharacterProfileData { characterId = "char-1" }, default));

            public Task<(bool success, InventoryData data, BackendError error)> GetInventoryAsync(BackendConfiguration config, string token)
                => Task.FromResult<(bool, InventoryData, BackendError)>((true, new InventoryData { revision = 1 }, default));

            public Task<(bool success, ProgressionData data, BackendError error)> GetProgressionAsync(BackendConfiguration config, string token)
                => Task.FromResult<(bool, ProgressionData, BackendError)>((true, new ProgressionData { revision = 1 }, default));

            public Task<(bool success, TownSessionTicketResponse data, BackendError error)> GetTownSessionTicketAsync(BackendConfiguration config, string token)
                => Task.FromResult<(bool, TownSessionTicketResponse, BackendError)>((true, new TownSessionTicketResponse
                {
                    ticket = "ticket-raw", characterId = "char-1", characterName = "Hero", sessionName = "town-1", expiresAt = 1
                }, default));

            public bool InitializeStash(ProfileId characterId, InventoryData inventory, ProgressionData progression) => true;
        }
    }
}
#endif

#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class RemoteCommitRetryTests
{
    [TestCase(false, "NO_AUTHORITATIVE_RESULT", 3)]
    [TestCase(true, "NO_AUTHORITATIVE_RESULT", 3)]
    [TestCase(false, "TOO_MANY_REQUESTS", 3)]
    [TestCase(true, "TOO_MANY_REQUESTS", 3)]
    [TestCase(false, "CONCURRENT_MUTATION", 3)]
    [TestCase(true, "CONCURRENT_MUTATION", 3)]
    [TestCase(false, "EXTRACTION_RESULT_PENDING", 3)]
    [TestCase(true, "EXTRACTION_RESULT_PENDING", 3)]
    [TestCase(false, "RESERVATION_PENDING", 1)]
    [TestCase(true, "RESERVATION_PENDING", 1)]
    [TestCase(false, "REVISION_CONFLICT", 3)]
    [TestCase(true, "REVISION_CONFLICT", 1)]
    public async Task Commit_PublicHttpBoundary_BoundsRetriesAndReportsDisposition(bool defeat, string code, int expectedAttempts)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var config = ScriptableObject.CreateInstance<BackendConfiguration>();
        config.BaseUrl = $"http://localhost:{port}";
        var authObject = new GameObject("CommitRetryAuth");
        var serviceObject = new GameObject("CommitRetryService");
        var delays = new List<int>();
        RemoteIdempotentRetryPolicy.BackoffMs = 0;
        RemoteIdempotentRetryPolicy.DelayAsync = ms => { delays.Add(ms); return Task.CompletedTask; };
        try
        {
            var auth = authObject.AddComponent<ApplicationAuthContext>();
            // EditMode does not run MonoBehaviour.Awake automatically for this component.
            if (ApplicationAuthContext.Instance != auth)
                typeof(ApplicationAuthContext).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(auth, null);
            Assert.That(ApplicationAuthContext.Instance, Is.SameAs(auth));
            auth.Initialize("test-token", new CharacterData { characterId = "test-profile", name = "Test" }, default);
            var service = serviceObject.AddComponent<RemoteInventoryService>();
            typeof(RemoteInventoryService).GetField("_backendConfig", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, config);
            if (defeat)
                for (int i = 0; i < expectedAttempts; i++)
                    LogAssert.Expect(LogType.Error, $"[RemoteInventoryService] CommitDefeatUnifiedAsync failed (raidId=raid-test, seq=1): Error={code}: test");
            if (expectedAttempts == 3)
                LogAssert.Expect(LogType.Error, $"[RemoteIdempotentRetryPolicy] Max retries reached for error {code}. Aborting.");

            var participantObject = new GameObject("DefeatFeedbackSource");
            participantObject.SetActive(false);
            participantObject.transform.SetParent(serviceObject.transform);
            var feedback = defeat
                ? participantObject.AddComponent<NetworkRaidParticipant>().BackendDefeatFeedback
                : new BackendCommitFeedback();
            feedback.Begin();
            Task<(bool success, BackendError error)> request = Commit(service, defeat, feedback);
            for (int i = 0; i < expectedAttempts; i++)
            {
                var next = listener.GetContextAsync();
                Assert.That(await Task.WhenAny(next, request), Is.SameAs(next), "Operation dropped a retryable before its attempt budget was exhausted.");
                var context = await next;
                Assert.That(context.Request.HttpMethod, Is.EqualTo("POST"));
                Assert.That(context.Request.Url.AbsolutePath, Is.EqualTo(defeat ? "/character/me/defeat/commit" : "/character/me/extraction/commit"));
                using var reader = new System.IO.StreamReader(context.Request.InputStream);
                string payload = await reader.ReadToEndAsync();
                Assert.That(payload, Does.Contain("\"raidId\":\"raid-test\""));
                Assert.That(payload, Does.Contain("\"resultSequence\":1"));
                context.Response.StatusCode = 409;
                byte[] body = Encoding.UTF8.GetBytes($"{{\"error\":\"{code}\",\"message\":\"test\"}}");
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body, 0, body.Length);
                context.Response.Close();
            }
            var result = await request;
            feedback.Complete(result.success, result.error);
            Assert.That(result.success, Is.False);
            Assert.That(result.error.error, Is.EqualTo(code));
            Assert.That(result.error.message, Is.EqualTo("test"));
            Assert.That(delays.Count, Is.EqualTo(expectedAttempts - 1));
            Assert.That(feedback.Status, Is.EqualTo(BackendCommitStatus.DeferredUntilLogin));
            string message = BackendErrorMessages.ForCommit(feedback);
            Assert.That(message, Does.StartWith("No se pudo guardar"));
            Assert.That(message, Does.Contain("se reintentará al iniciar sesión"));
        }
        finally
        {
            RemoteIdempotentRetryPolicy.BackoffMs = 1000;
            RemoteIdempotentRetryPolicy.DelayAsync = Task.Delay;
            Object.DestroyImmediate(serviceObject);
            Object.DestroyImmediate(authObject);
            Object.DestroyImmediate(config);
        }
    }

    [TestCase("clear")]
    [TestCase("rollback")]
    [TestCase("orphan-clear")]
    [TestCase("orphan-rollback")]
    [TestCase("participation-loss")]
    [TestCase("reconnected-participation-loss")]
    [TestCase("legacy-participation-loss")]
    public async Task ReservationCleanup_PublicHttpBoundary_RetainsIdentityAcrossTransportRetry(string path)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var config = ScriptableObject.CreateInstance<BackendConfiguration>();
        config.BaseUrl = $"http://localhost:{port}";
        var authObject = new GameObject("ReservationRetryAuth");
        var root = new GameObject("ReservationRetryService");
        var delays = new List<int>();
        RemoteIdempotentRetryPolicy.BackoffMs = 0;
        RemoteIdempotentRetryPolicy.DelayAsync = ms => { delays.Add(ms); return Task.CompletedTask; };
        try
        {
            var auth = authObject.AddComponent<ApplicationAuthContext>();
            if (ApplicationAuthContext.Instance != auth)
                typeof(ApplicationAuthContext).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(auth, null);
            auth.Initialize("test-token", new CharacterData { characterId = "test-profile", name = "Test" }, default);
            var service = root.AddComponent<RemoteInventoryService>();
            typeof(RemoteInventoryService).GetField("_backendConfig", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, config);
            const string expectedId = "reservation-old";
            Task request;
            if (path.StartsWith("orphan-"))
            {
                var method = typeof(ApplicationStashServiceBootstrapper).GetMethod("ClearOrphanedReservationAsync", BindingFlags.Static | BindingFlags.NonPublic);
                request = (Task)method.Invoke(null, new object[] { service, path == "orphan-rollback", expectedId });
            }
            else if (path.EndsWith("participation-loss"))
            {
                var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
                    "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
                var profile = new ProfileId("47474747474747474747474747474747");
                var repository = new InMemoryLocalProfileRepository();
                Assert.That(repository.Initialize(profile, catalog), Is.True);
                var snapshot = repository.Snapshot.Clone();
                snapshot.PendingReservation = new PendingLoadoutReservation(expectedId,
                    new[] { new StashItem(new LootId("bone"), 2, 0) },
                    new PreparedEquipmentLoadout(new LootId("rapier"), default));
                Assert.That(repository.TrySave(snapshot, out string error), Is.True, error);
                var store = new LocalProfileStore(repository, profile, catalog);
                var context = root.AddComponent<ApplicationStashContext>();
                context.Initialize(store, null, null, null, null);
                var coordinator = root.AddComponent<SessionConnectionCoordinator>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(SessionConnectionCoordinator).GetField("_recoveryStashContext", flags).SetValue(coordinator, context);
                typeof(SessionConnectionCoordinator).GetField("_currentProfileId", flags).SetValue(coordinator, profile);
                typeof(SessionConnectionCoordinator).GetField("_reservationParticipationRaidId", flags).SetValue(coordinator, "raid-old");
                if (path == "reconnected-participation-loss" || path == "legacy-participation-loss")
                {
                    // Successful reconnect retains its ticket's reservation proof after local admission consumed the pending snapshot.
                    Assert.That(store.TryConfirmLoadoutReservation(expectedId), Is.EqualTo(StashOperationResult.Success));
                    if (path == "reconnected-participation-loss")
                        typeof(SessionConnectionCoordinator).GetField("_reservationParticipationReservationId", flags).SetValue(coordinator, expectedId);
                }
                typeof(SessionConnectionCoordinator).GetMethod("ClearLostParticipationReservation", flags).Invoke(coordinator, null);
                request = (Task)typeof(SessionConnectionCoordinator).GetField("_participationOutcomeTask", flags).GetValue(coordinator);
                // A replacement after dispatch must never change the identity captured by the retry.
                snapshot.PendingReservation = new PendingLoadoutReservation("reservation-new", snapshot.PendingReservation.Items,
                    snapshot.PendingReservation.PreparedEquipment);
                Assert.That(repository.TrySave(snapshot, out error), Is.True, error);
            }
            else if (path == "rollback") request = service.RollbackPendingReservationAsync(expectedId);
            else
            {
                request = service.ClearPendingReservationAsync(expectedId);
            }
            var ids = new List<string>();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var next = listener.GetContextAsync();
                Assert.That(await Task.WhenAny(next, request, Task.Delay(5000)), Is.SameAs(next));
                var context = await next;
                Assert.That(context.Request.HttpMethod, Is.EqualTo("DELETE"));
                Assert.That(context.Request.Url.AbsolutePath, Is.EqualTo("/character/me/inventory/reservation"));
                Assert.That(context.Request.QueryString["rollback"], Is.EqualTo(path.Contains("rollback") ? "true" : null));
                ids.Add(context.Request.QueryString["expectedReservationId"]);
                // A truncated response is a real transport failure, not a retryable application code.
                byte[] body = Encoding.UTF8.GetBytes("{\"pendingReservation\":null,\"revision\":8}");
                context.Response.StatusCode = 200;
                context.Response.ContentLength64 = attempt == 0 ? body.Length + 100 : body.Length;
                await context.Response.OutputStream.WriteAsync(body, 0, body.Length);
                context.Response.Close();
            }
            await request;
            string wireId = path == "legacy-participation-loss" ? null : expectedId;
            Assert.That(ids, Is.EqualTo(new[] { wireId, wireId }));
            Assert.That(delays.Count, Is.EqualTo(1));
        }
        finally
        {
            RemoteIdempotentRetryPolicy.BackoffMs = 1000;
            RemoteIdempotentRetryPolicy.DelayAsync = Task.Delay;
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(authObject);
            Object.DestroyImmediate(config);
        }
    }

    [TestCase("save", 2, "REVISION_CONFLICT")]
    [TestCase("admit", 2, "REVISION_CONFLICT")]
    [TestCase("clear", 2, "REVISION_CONFLICT")]
    [TestCase("rollback", 2, "REVISION_CONFLICT")]
    [TestCase("extraction", 2, "REVISION_CONFLICT")]
    [TestCase("publish-extraction", 1, "REVISION_CONFLICT")]
    [TestCase("publish-defeat", 1, "REVISION_CONFLICT")]
    [TestCase("clear", 1, "EXTRACTION_RESULT_PENDING")]
    [TestCase("defeat", 1, "RESULT_ALREADY_CONSUMED")]
    public async Task OperationSpecificRetry_PublicHttpBoundary(string operation, int expectedAttempts, string code)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var config = ScriptableObject.CreateInstance<BackendConfiguration>();
        config.BaseUrl = $"http://localhost:{port}";
        var authObject = new GameObject("RevisionRetryAuth");
        var root = new GameObject("RevisionRetryService");
        var delays = new List<int>();
        RemoteIdempotentRetryPolicy.DelayAsync = ms => { delays.Add(ms); return Task.CompletedTask; };
        try
        {
            var auth = authObject.AddComponent<ApplicationAuthContext>();
            if (ApplicationAuthContext.Instance != auth)
                typeof(ApplicationAuthContext).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(auth, null);
            auth.Initialize("test-token", new CharacterData { characterId = "test-profile", name = "Test" }, default);
            var service = root.AddComponent<RemoteInventoryService>();
            typeof(RemoteInventoryService).GetField("_backendConfig", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, config);
            var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
                "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
            var profile = new ProfileId("47474747474747474747474747474747");
            var repository = new InMemoryLocalProfileRepository();
            Assert.That(repository.Initialize(profile, catalog), Is.True);
            var store = new LocalProfileStore(repository, profile, catalog);
            typeof(RemoteInventoryService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, store);
            int revisionBefore = store.RemoteRevision;
            var feedback = new BackendCommitFeedback(operation != "defeat");
            if (operation == "defeat")
                LogAssert.Expect(LogType.Error, "[RemoteInventoryService] CommitDefeatUnifiedAsync failed (raidId=raid-test, seq=1): Error=RESULT_ALREADY_CONSUMED: ");
            var receipt = new ExtractionReceipt("raid-test", new ProfileId("test-profile"), 1);
            Task<(bool success, BackendError error)> request = operation switch
            {
                "save" => service.SavePendingReservationAsync(new PendingLoadoutReservation("reservation-test", System.Array.Empty<StashItem>(), default)),
                "admit" => service.MarkPendingReservationAdmittedAsync("reservation-test"),
                "clear" => service.ClearPendingReservationAsync("reservation-test"),
                "rollback" => service.RollbackPendingReservationAsync("reservation-test"),
                "extraction" => Commit(service, false, feedback),
                "defeat" => Commit(service, true, feedback),
                "publish-extraction" => service.PublishExtractionResultAsync(receipt, System.Array.Empty<StashItem>(), default, 0),
                _ => service.PublishDefeatResultAsync("raid-test", receipt.ProfileId, 1, 0)
            };
            int attempts = 0;
            while (attempts < 3)
            {
                var next = listener.GetContextAsync();
                var completed = await Task.WhenAny(next, request, Task.Delay(5000));
                if (completed == request) break;
                Assert.That(completed, Is.SameAs(next));
                var context = await next;
                bool reject = attempts == 0 || code == "EXTRACTION_RESULT_PENDING";
                context.Response.StatusCode = reject ? 409 : 201;
                byte[] body = Encoding.UTF8.GetBytes(reject ? $"{{\"error\":\"{code}\"}}" : "{\"revision\":9}");
                attempts++;
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body, 0, body.Length);
                context.Response.Close();
            }
            var outcome = await request;
            feedback.Complete(outcome.success, outcome.error);
            Assert.That(outcome.success, Is.EqualTo(expectedAttempts == 2));
            if (operation == "defeat")
            {
                Assert.That(feedback.Status, Is.EqualTo(BackendCommitStatus.Failed));
                Assert.That(BackendErrorMessages.ForCommit(feedback), Does.Not.Contain("Resultado guardado."));
            }
            Assert.That(outcome.error.error, expectedAttempts == 2 ? Is.Null.Or.Empty : Is.EqualTo(code));
            Assert.That(store.RemoteRevision, Is.EqualTo(operation == "extraction" || expectedAttempts == 1 ? revisionBefore : 9),
                "Extraction must not advance revision before receipt-bound hydration; rejection must leave it unchanged.");
            Assert.That(attempts, Is.EqualTo(expectedAttempts));
            Assert.That(delays.Count, Is.EqualTo(expectedAttempts - 1));
        }
        finally
        {
            RemoteIdempotentRetryPolicy.DelayAsync = Task.Delay;
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(authObject);
            Object.DestroyImmediate(config);
        }
    }

    private static async Task<(bool success, BackendError error)> Commit(RemoteInventoryService service, bool defeat, BackendCommitFeedback feedback)
    {
        var profile = new ProfileId("test-profile");
        if (defeat)
        {
            var r = await service.CommitDefeatUnifiedAsync(new ProgressionReceipt("raid-test", profile, 1, 0, 1), feedback.Retrying);
            return (r.success, r.error);
        }
        var result = await service.CommitExtractionUnifiedAsync(new ExtractionReceipt("raid-test", profile, 1),
            System.Array.Empty<StashItem>(), default, 0, 1, feedback.Retrying);
        return (result.success, result.error);
    }
}
#endif

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
    [TestCase(false, "REVISION_CONFLICT", 1)]
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
            Assert.That(feedback.Status, Is.EqualTo(!defeat && expectedAttempts == 3
                ? BackendCommitStatus.DeferredUntilLogin : BackendCommitStatus.Failed));
            string message = BackendErrorMessages.ForCommit(feedback);
            Assert.That(message, Does.StartWith("No se pudo guardar"));
            if (defeat) Assert.That(message, Does.Not.Contain("se reintentará al iniciar sesión"));
            else if (expectedAttempts == 3) Assert.That(message, Does.Contain("se reintentará al iniciar sesión"));
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

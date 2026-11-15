#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEngine;

public sealed class BackendHttpErrorTests
{
    [TestCase("inventory")]
    [TestCase("character")]
    [TestCase("progression")]
    [TestCase("login")]
    [TestCase("register")]
    public async Task Http429_CapturesHeaderAndBodyWaitHint(string client)
    {
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        using var listener = new HttpListener();
        string url = $"http://localhost:{port}";
        listener.Prefixes.Add(url + "/");
        listener.Start();
        var config = ScriptableObject.CreateInstance<BackendConfiguration>();
        config.BaseUrl = url;
        try
        {
            Task<BackendError> request = Request(client, config);
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = 429;
            context.Response.Headers["Retry-After"] = "7";
            byte[] body = Encoding.UTF8.GetBytes("{\"error\":\"TOO_MANY_REQUESTS\",\"message\":\"not for presentation\",\"retryAfter\":3}");
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body, 0, body.Length);
            context.Response.Close();
            var error = await request;
            Assert.That(error.error, Is.EqualTo("TOO_MANY_REQUESTS"));
            Assert.That(error.message, Is.EqualTo("not for presentation"));
            Assert.That(error.retryAfter, Is.EqualTo(7));
        }
        finally { Object.DestroyImmediate(config); }
    }

    [TestCase("extraction", 17)]
    [TestCase("defeat", 17)]
    [TestCase("legacy", 0)]
    public async Task ConsumedCommit_IsIdempotentSuccessWithAuthoritativeMetadata(string client, int expectedRevision)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var listener = new HttpListener();
        string url = $"http://localhost:{port}";
        listener.Prefixes.Add(url + "/");
        listener.Start();
        var config = ScriptableObject.CreateInstance<BackendConfiguration>();
        config.BaseUrl = url;
        try
        {
            var request = Commit(client, config);
            var context = await listener.GetContextAsync();
            await Respond(context, 409, "{\"error\":\"RESULT_ALREADY_CONSUMED\"}");
            // The previous implementation returns immediately. Avoid hanging the RED test
            // waiting for a metadata request which only the corrected client will issue.
            var next = listener.GetContextAsync();
            var completed = await Task.WhenAny(request, next);
            if (completed == next)
            {
                var metadata = await next;
                Assert.That(metadata.Request.HttpMethod, Is.EqualTo("GET"));
                await Respond(metadata, 200, "{\"revision\":17,\"level\":4,\"experience\":12,\"loadout\":[]}");
            }
            var outcome = await request;
            Assert.That(outcome.success, Is.True);
            Assert.That(outcome.secured, Is.True);
            Assert.That(outcome.revision, Is.EqualTo(expectedRevision));
            Assert.That(outcome.error.error, Is.Null.Or.Empty);
        }
        finally { Object.DestroyImmediate(config); }
    }

    private static async Task Respond(HttpListenerContext context, int status, string text)
    {
        context.Response.StatusCode = status;
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        context.Response.Close();
    }

    private static async Task<(bool success, bool secured, int revision, BackendError error)> Commit(string client, BackendConfiguration config)
    {
        if (client == "defeat")
        {
            var r = await InventoryClient.CommitDefeatUnifiedAsync(config, "test", default);
            return (r.success, r.data.alreadySecured, r.data.revision, r.error);
        }
        if (client == "legacy")
        {
            var r = await InventoryClient.CommitExtractionAsync(config, "test", default);
            return (r.success, r.data.alreadySecured, 0, r.error); // This legacy DTO has no revision.
        }
        var result = await InventoryClient.CommitExtractionUnifiedAsync(config, "test", default);
        return (result.success, result.data.alreadySecured, result.data.revision, result.error);
    }

    private static async Task<BackendError> Request(string client, BackendConfiguration config)
    {
        switch (client)
        {
            case "inventory": return (await InventoryClient.GetInventoryAsync(config, "test")).error;
            case "character": return (await CharacterClient.GetCharacterAsync(config, "test")).error;
            case "progression": return (await ProgressionClient.GetProgressionAsync(config, "test")).error;
            case "login": return (await AuthenticationClient.PostLoginAsync(config, "test", "test")).error;
            default: return (await AuthenticationClient.PostRegisterAsync(config, "test", "test")).error;
        }
    }
}
#endif

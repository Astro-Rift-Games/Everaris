using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEngine;

public sealed class BackendHealthClientTests
{
    [TestCase(200, true)]
    [TestCase(503, false)]
    public async Task Health_UsesConfiguredGetWithoutCredentials(int status, bool online)
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
        config.BaseUrl = url + "/";
        try
        {
            var request = new BackendHealthClient().CheckAsync(config);
            var incoming = listener.GetContextAsync();
            Assert.That(await Task.WhenAny(incoming, Task.Delay(3000)), Is.SameAs(incoming), "GET /health was not sent");
            var context = await incoming;
            Assert.That(context.Request.HttpMethod, Is.EqualTo("GET"));
            Assert.That(context.Request.Url.AbsolutePath, Is.EqualTo("/health"));
            Assert.That(context.Request.Headers["Authorization"], Is.Null);
            context.Response.StatusCode = status;
            context.Response.ContentLength64 = 0;
            context.Response.Close();
            Assert.That(await request, Is.EqualTo(online));
        }
        finally { Object.DestroyImmediate(config); }
    }

    [Test]
    public async Task MissingConfiguration_IsUnreachable()
        => Assert.That(await new BackendHealthClient().CheckAsync(null), Is.False);
}

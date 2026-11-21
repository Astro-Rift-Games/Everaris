#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Grimhold.Backend;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Real loopback HTTP boundary for the inventory endpoints: it records what the client actually
/// sent and answers from a script (the last reply repeats), so retries and error handling are
/// exercised through <see cref="RemoteInventoryService"/> and <see cref="InventoryClient"/>.
/// </summary>
internal sealed class LoopbackBackendServer : IDisposable
{
    internal readonly struct Recorded
    {
        public Recorded(string method, string path, string query, string body, string authorization = null)
        { Method = method; Path = path; Query = query; Body = body; Authorization = authorization; }

        public string Method { get; }
        public string Path { get; }
        public string Query { get; }
        public string Body { get; }
        /// <summary>The raw Authorization header the client sent, or null.</summary>
        public string Authorization { get; }
    }

    internal readonly struct Reply
    {
        private Reply(int status, string body, bool truncated, int delayMs = 0)
        { Status = status; Body = body; Truncated = truncated; DelayMs = delayMs; }
        public int Status { get; }
        public string Body { get; }
        /// <summary>Milliseconds the server waits before answering, so requests can overlap in flight.</summary>
        public int DelayMs { get; }
        public Reply After(int delayMs) => new Reply(Status, Body, Truncated, delayMs);
        /// <summary>Declares more bytes than it sends: a genuine transport failure for the client.</summary>
        public bool Truncated { get; }

        public static Reply Ok(string body) => new Reply(200, body, false);
        public static Reply Rejected(int status, string code) =>
            new Reply(status, "{\"error\":\"" + code + "\",\"message\":\"test\"}", false);
        public static Reply TransportFailure() => new Reply(200, "{\"revision\":1}", true);
    }

    private readonly HttpListener _listener = new HttpListener();
    private readonly Queue<Reply> _script;
    private readonly Func<Recorded, Reply> _responder;
    private readonly List<Recorded> _requests = new List<Recorded>();
    private readonly object _gate = new object();
    private Reply _last;

    public LoopbackBackendServer(params Reply[] replies) : this(replies, null) { }

    /// <summary>Answers every request from <paramref name="responder"/>, which can route by path and credential.</summary>
    public LoopbackBackendServer(Func<Recorded, Reply> responder) : this(Array.Empty<Reply>(), responder) { }

    private LoopbackBackendServer(Reply[] replies, Func<Recorded, Reply> responder)
    {
        _script = new Queue<Reply>(replies);
        _responder = responder;
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        BaseUrl = $"http://localhost:{port}";
        _listener.Prefixes.Add($"{BaseUrl}/");
        _listener.Start();
        _ = ServeAsync();
    }

    public string BaseUrl { get; }

    public List<Recorded> Requests
    {
        get { lock (_gate) return new List<Recorded>(_requests); }
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch { return; }

            // Each exchange is handled on its own so a delayed reply never blocks an overlapping request.
            _ = HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        string body;
        using (var reader = new StreamReader(context.Request.InputStream))
            body = await reader.ReadToEndAsync();
        var recorded = new Recorded(context.Request.HttpMethod, context.Request.Url.AbsolutePath,
            context.Request.Url.Query, body, context.Request.Headers["Authorization"]);
        Reply reply;
        lock (_gate)
        {
            _requests.Add(recorded);
            if (_responder == null)
            {
                if (_script.Count > 0) _last = _script.Dequeue();
                reply = _last;
            }
            else
            {
                reply = _responder(recorded);
            }
        }

        if (reply.DelayMs > 0) await Task.Delay(reply.DelayMs);
        byte[] payload = Encoding.UTF8.GetBytes(reply.Body ?? string.Empty);
        context.Response.StatusCode = reply.Status;
        context.Response.ContentLength64 = reply.Truncated ? payload.Length + 100 : payload.Length;
        try
        {
            await context.Response.OutputStream.WriteAsync(payload, 0, payload.Length);
            context.Response.Close();
        }
        catch { /* the client of a truncated reply may have gone away */ }
    }

    public void Dispose()
    {
        if (_listener.IsListening) _listener.Close();
    }
}

/// <summary>A <see cref="RemoteInventoryService"/> wired to a loopback backend and a real profile store.</summary>
internal sealed class RemoteServiceFixture : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly GameObject _authObject = new GameObject("RefundFixtureAuth");
    private readonly BackendConfiguration _config;

    public RemoteServiceFixture(string baseUrl, LocalProfileStore store)
    {
        _config = ScriptableObject.CreateInstance<BackendConfiguration>();
        _config.BaseUrl = baseUrl;
        Root = new GameObject("RefundFixtureService");
        var auth = _authObject.AddComponent<ApplicationAuthContext>();
        // EditMode does not run MonoBehaviour.Awake automatically for this component.
        if (ApplicationAuthContext.Instance != auth)
            typeof(ApplicationAuthContext).GetMethod("Awake", Private).Invoke(auth, null);
        auth.Initialize("test-token", new CharacterData { characterId = "test-profile", name = "Test" }, default);
        Service = Root.AddComponent<RemoteInventoryService>();
        typeof(RemoteInventoryService).GetField("_backendConfig", Private).SetValue(Service, _config);
        typeof(RemoteInventoryService).GetField("_store", Private).SetValue(Service, store);
        Delays = new List<int>();
        RemoteIdempotentRetryPolicy.BackoffMs = 0;
        RemoteIdempotentRetryPolicy.DelayAsync = ms => { Delays.Add(ms); return Task.CompletedTask; };
    }

    public GameObject Root { get; }
    public RemoteInventoryService Service { get; }
    public List<int> Delays { get; }

    public void Dispose()
    {
        RemoteIdempotentRetryPolicy.BackoffMs = 1000;
        RemoteIdempotentRetryPolicy.DelayAsync = Task.Delay;
        Object.DestroyImmediate(Root);
        Object.DestroyImmediate(_authObject);
        Object.DestroyImmediate(_config);
    }
}
#endif

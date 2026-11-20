#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Grimhold.Backend;
using UnityEngine;

namespace Tests.EditMode.Stash
{
    /// <summary>One request received by the local HttpListener standing in for the backend.</summary>
    public readonly struct RecordedRequest
    {
        public RecordedRequest(string method, string path, string rollback, string expectedReservationId, string body)
        {
            Method = method;
            Path = path;
            Rollback = rollback;
            ExpectedReservationId = expectedReservationId;
            Body = body;
        }

        public string Method { get; }
        public string Path { get; }
        public string Rollback { get; }
        public string ExpectedReservationId { get; }
        public string Body { get; }
        public bool IsAdmit => Method == "POST" && Path == "/character/me/inventory/reservation/admit";
        public bool IsDelete => Method == "DELETE" && Path == "/character/me/inventory/reservation";
    }

    /// <summary>What the stub backend answers; Truncated cuts the body short and drops the connection to simulate a real transport failure.</summary>
    public readonly struct BackendReply
    {
        public BackendReply(int status, string body, bool truncated = false)
        {
            Status = status;
            Body = body;
            Truncated = truncated;
        }

        public int Status { get; }
        public string Body { get; }
        public bool Truncated { get; }

        public static BackendReply Ok(int revision = 9) => new(200, $"{{\"revision\":{revision}}}");
        public static BackendReply TransportFailure() => new(200, "{\"revision\":9}", truncated: true);
        public static BackendReply Error(int status, string code) => new(status, $"{{\"error\":\"{code}\",\"message\":\"test\"}}");
    }

    /// <summary>Seeds a profile whose backend-hydrated reservation holds 2 bone and a rapier on top of a 3-bone stash.</summary>
    public static class ReservationSeed
    {
        public const string ReservationId = "reservation-a";
        public static readonly ProfileId Profile = new("56565656565656565656565656565656");

        public static LootDefinitionCatalog Catalog() => UnityEditor.AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
            "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");

        public static InventoryData Inventory(string reservationId = ReservationId, string admittedAt = null) => new()
        {
            revision = 7,
            stash = new[] { new InventoryItemData { lootId = "bone", amount = 3 } },
            loadout = Array.Empty<InventoryItemData>(),
            pendingReservation = string.IsNullOrEmpty(reservationId) ? default : new PendingReservationData
            {
                reservationId = reservationId,
                admittedAt = admittedAt,
                items = new[] { new InventoryItemData { lootId = "bone", amount = 2 } },
                preparedEquipment = new PreparedEquipmentData { weaponSlot1 = "rapier" }
            }
        };

        /// <summary>Repository hydrated from <paramref name="inventory"/>, as a login would leave it.</summary>
        public static InMemoryLocalProfileRepository Hydrated(InventoryData inventory)
        {
            var catalog = Catalog();
            var repository = new InMemoryLocalProfileRepository();
            if (!repository.Initialize(Profile, catalog)) throw new InvalidOperationException("repository init failed");
            LocalProfileSnapshot snapshot = repository.Snapshot.Clone();
            if (!ApplicationStashServiceBootstrapper.HydrateSnapshot(Profile, snapshot, inventory, null, catalog, null))
                throw new InvalidOperationException("hydration failed");
            if (!repository.TrySave(snapshot, out string error)) throw new InvalidOperationException(error);
            return repository;
        }

        /// <summary>A later login: the backend payload is hydrated over the cache, which keeps its local-only state.</summary>
        public static LocalProfileStore Relogin(InMemoryLocalProfileRepository repository, InventoryData inventory)
        {
            var catalog = Catalog();
            LocalProfileSnapshot snapshot = repository.Snapshot.Clone();
            if (!ApplicationStashServiceBootstrapper.HydrateSnapshot(Profile, snapshot, inventory, null, catalog, null))
                throw new InvalidOperationException("hydration failed");
            if (!repository.TrySave(snapshot, out string error)) throw new InvalidOperationException(error);
            return new LocalProfileStore(repository, Profile, catalog);
        }
    }

    /// <summary>
    /// Drives <see cref="RemoteInventoryService"/> through the real HTTP boundary (HttpListener on localhost)
    /// and stubs the retry delays; Dispose restores every static it replaced.
    /// </summary>
    public sealed class ReservationBackendHarness : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly object _gate = new();
        private readonly List<RecordedRequest> _requests = new();
        private readonly BackendConfiguration _config;
        private readonly GameObject _authObject = new("ReservationHarnessAuth");
        private readonly GameObject _serviceObject = new("ReservationHarnessService");
        private readonly int _previousBackoff;
        private readonly Func<int, Task> _previousDelay;
        private readonly Func<int, Task> _previousRoundDelay;

        public ReservationBackendHarness(LocalProfileStore store, Func<int, RecordedRequest, BackendReply> responder)
        {
            Responder = responder;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();
            _config = ScriptableObject.CreateInstance<BackendConfiguration>();
            _config.BaseUrl = $"http://localhost:{port}";

            _previousBackoff = RemoteIdempotentRetryPolicy.BackoffMs;
            _previousDelay = RemoteIdempotentRetryPolicy.DelayAsync;
            _previousRoundDelay = RemoteInventoryService.AdmissionRoundDelayAsync;
            RemoteIdempotentRetryPolicy.BackoffMs = 0;
            RemoteIdempotentRetryPolicy.DelayAsync = ms => { InnerDelays.Add(ms); return Task.CompletedTask; };
            RemoteInventoryService.AdmissionRoundDelayAsync = ms => { RoundDelays.Add(ms); return Task.CompletedTask; };

            var auth = _authObject.AddComponent<ApplicationAuthContext>();
            // EditMode does not run MonoBehaviour.Awake automatically for this component.
            if (ApplicationAuthContext.Instance != auth)
                typeof(ApplicationAuthContext).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(auth, null);
            auth.Initialize("test-token", new CharacterData { characterId = "test-profile", name = "Test" }, default);
            Service = _serviceObject.AddComponent<RemoteInventoryService>();
            typeof(RemoteInventoryService).GetField("_backendConfig", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Service, _config);
            typeof(RemoteInventoryService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Service, store);
            _ = ServeAsync();
        }

        public RemoteInventoryService Service { get; }
        public Func<int, RecordedRequest, BackendReply> Responder { get; set; }
        public List<int> InnerDelays { get; } = new();
        public List<int> RoundDelays { get; } = new();

        public RecordedRequest[] Requests
        {
            get { lock (_gate) return _requests.ToArray(); }
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                string body;
                using (var reader = new System.IO.StreamReader(context.Request.InputStream))
                    body = await reader.ReadToEndAsync();
                var recorded = new RecordedRequest(context.Request.HttpMethod, context.Request.Url.AbsolutePath,
                    context.Request.QueryString["rollback"], context.Request.QueryString["expectedReservationId"], body);
                int index;
                lock (_gate)
                {
                    index = _requests.Count;
                    _requests.Add(recorded);
                }

                BackendReply reply = Responder(index, recorded);
                byte[] payload = Encoding.UTF8.GetBytes(reply.Body);
                context.Response.StatusCode = reply.Status;
                // A truncated body promises more bytes than it delivers; the dropped connection is a real transport failure.
                context.Response.ContentLength64 = reply.Truncated ? payload.Length + 100 : payload.Length;
                await context.Response.OutputStream.WriteAsync(payload, 0, payload.Length);
                if (reply.Truncated)
                {
                    await context.Response.OutputStream.FlushAsync();
                    context.Response.Abort();
                    continue;
                }

                context.Response.Close();
            }
        }

        public void Dispose()
        {
            RemoteIdempotentRetryPolicy.BackoffMs = _previousBackoff;
            RemoteIdempotentRetryPolicy.DelayAsync = _previousDelay;
            RemoteInventoryService.AdmissionRoundDelayAsync = _previousRoundDelay;
            _listener.Close();
            UnityEngine.Object.DestroyImmediate(_serviceObject);
            UnityEngine.Object.DestroyImmediate(_authObject);
            UnityEngine.Object.DestroyImmediate(_config);
        }
    }
}
#endif

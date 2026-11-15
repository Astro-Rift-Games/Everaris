#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fusion;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;

public sealed class BackendConfirmedLaunchTests
{
    private static readonly ProfileId Profile = new("58585858585858585858585858585858");
    private GameObject _root;
    private ApplicationAuthContext _auth;
    private ApplicationAuthContext _previousAuth;
    private SessionConnectionCoordinator _previousCoordinator;
    private ProfileId _previousProfile;
    private BackendConfiguration _config;
    private LocalProfileStore _store;
    private ApplicationStashContext _stash;
    private FusionSessionLauncher _launcher;
    private SessionConnectionCoordinator _coordinator;
    private SessionConnectionStateMachine _state;
    private PreparedEquipmentLoadout _equipment;
    private Backend _backend;

    [SetUp]
    public void SetUp()
    {
        _previousProfile = LocalProfileProvider.GetOrCreateLocalProfile();
        _previousAuth = ApplicationAuthContext.Instance;
        _previousCoordinator = SessionConnectionCoordinator.Instance;
        Static(typeof(ApplicationAuthContext), "<Instance>k__BackingField", null);
        LocalProfileProvider.SetRemoteCharacterId(Profile);
        _root = new GameObject(nameof(BackendConfirmedLaunchTests));
        _auth = _root.AddComponent<ApplicationAuthContext>();
        Static(typeof(ApplicationAuthContext), "<Instance>k__BackingField", _auth);
        _config = ScriptableObject.CreateInstance<BackendConfiguration>();
        _auth.Initialize("test-only", new CharacterData { characterId = Profile.Value }, default, _config);
        _auth.SetVerifiers(null, null);
        var catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
            "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
        var repository = new InMemoryLocalProfileRepository();
        Assert.That(repository.Initialize(Profile, catalog), Is.True);
        _store = new LocalProfileStore(repository, Profile, catalog);
        Assert.That(_store.TryImportItems(new[] { new StashItem(new LootId("training_short_sword"), 1) }), Is.EqualTo(StashOperationResult.Success));
        Assert.That(_store.TryAssignPreparedEquipment(EquipmentSlot.WeaponSetAMainHand, new LootId("training_short_sword")), Is.EqualTo(StashOperationResult.Success));
        _equipment = _store.GetPreparedEquipment();
        var loadout = _root.AddComponent<InMemoryPlayerLoadoutService>();
        loadout.Initialize(_store);
        _stash = _root.AddComponent<ApplicationStashContext>();
        _stash.Initialize(_store, null, loadout, null, null);
        _root.AddComponent<HubSessionLauncher>();
        _launcher = _root.AddComponent<FusionSessionLauncher>();
        var production = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Systems.prefab").GetComponentInChildren<FusionSessionLauncher>(true);
        Set(_launcher, "_matchControllerPrefab", Get(production, "_matchControllerPrefab"));
        // A busy launcher is a safety stop AFTER admission preparation, not a fake runner.
        // Reaching it yields Error with no typed backend failure on the old implementation.
        Set(_launcher, "_isStarting", true);
        _backend = new Backend();
        _launcher.InitializeLaunchBackend(_backend);
        _coordinator = _root.AddComponent<SessionConnectionCoordinator>();
        Set(_coordinator, "_hubLauncher", _root.GetComponent<HubSessionLauncher>());
        Set(_coordinator, "_raidLauncher", _launcher);
        Static(typeof(SessionConnectionCoordinator), "<Instance>k__BackingField", _coordinator);
        _state = (SessionConnectionStateMachine)Get(_coordinator, "_stateMachine");
        Assert.That(_state.TryTransition(SessionConnectionState.ConnectingTown), Is.True);
        Assert.That(_state.TryTransition(SessionConnectionState.Town), Is.True);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_root);
        Object.DestroyImmediate(_config);
        Static(typeof(ApplicationAuthContext), "<Instance>k__BackingField", _previousAuth);
        Static(typeof(SessionConnectionCoordinator), "<Instance>k__BackingField", _previousCoordinator);
        LocalProfileProvider.SetRemoteCharacterId(_previousProfile);
    }

    [TestCase(GameMode.Host, "save", "RESERVATION_REJECTED", SessionTransitionResult.LoadoutReservationFailed)]
    [TestCase(GameMode.Client, "save", "RESERVATION_REJECTED", SessionTransitionResult.LoadoutReservationFailed)]
    [TestCase(GameMode.Host, "ticket", "NO_ACTIVE_RESERVATION", SessionTransitionResult.LoadoutReservationFailed)]
    [TestCase(GameMode.Client, "ticket", "NO_ACTIVE_RESERVATION", SessionTransitionResult.LoadoutReservationFailed)]
    [TestCase(GameMode.Host, "ticket", "NETWORK_ERROR", SessionTransitionResult.ServiceUnavailable)]
    [TestCase(GameMode.Client, "ticket", "NETWORK_ERROR", SessionTransitionResult.ServiceUnavailable)]
    [TestCase(GameMode.Host, "save", "TIMEOUT", SessionTransitionResult.ServiceUnavailable)]
    [TestCase(GameMode.Client, "save", "TIMEOUT", SessionTransitionResult.ServiceUnavailable)]
    [TestCase(GameMode.Host, "ticket", "UNAUTHORIZED", SessionTransitionResult.LoadoutReservationFailed)]
    [TestCase(GameMode.Client, "ticket", "UNAUTHORIZED", SessionTransitionResult.LoadoutReservationFailed)]
    [TestCase(GameMode.Host, "decode", "", SessionTransitionResult.LoadoutReservationFailed)]
    [TestCase(GameMode.Client, "decode", "", SessionTransitionResult.LoadoutReservationFailed)]
    public async Task BackendFailure_AbortsBeforeRunner_WithTypedReason(GameMode mode, string stage, string error, SessionTransitionResult expected)
    {
        Set(_launcher, "_isStarting", false);
        var ticket = Reserve(mode);
        _backend.Stage = stage;
        _backend.Error = error;
        Assert.That(await _launcher.StartCoordinatedSessionAsync(ticket.Request.SessionName, mode, null,
            ticket.LaunchContext, ticket.LoadoutReservation), Is.False);
        Assert.That(_launcher.LastLaunchFailure, Is.EqualTo(expected));
        Assert.That(_launcher.Runner, Is.Null);
        Assert.That(Get(_launcher, "_runnerObject"), Is.Null);
        Assert.That(_backend.Saves, Is.EqualTo(1));
        Assert.That(_backend.Requests, Is.EqualTo(stage == "save" ? 0 : 1));
        Assert.That(FusionSessionLauncher.IsSessionAvailabilityPending(_launcher.LastStartShutdownReason), Is.False);
    }

    [TestCase(GameMode.Host, "save", "RESERVATION_REJECTED", SessionTransitionResult.LoadoutReservationFailed)]
    [TestCase(GameMode.Client, "save", "RESERVATION_REJECTED", SessionTransitionResult.LoadoutReservationFailed)]
    [TestCase(GameMode.Host, "ticket", "NO_ACTIVE_RESERVATION", SessionTransitionResult.LoadoutReservationFailed)]
    [TestCase(GameMode.Client, "ticket", "NO_ACTIVE_RESERVATION", SessionTransitionResult.LoadoutReservationFailed)]
    [TestCase(GameMode.Client, "ticket", "TIMEOUT", SessionTransitionResult.ServiceUnavailable)]
    public async Task Coordinator_MapsAbortAndRollsBackThroughStore(GameMode mode, string stage, string error, SessionTransitionResult expected)
    {
        Set(_launcher, "_isStarting", false);
        var ticket = Reserve(mode);
        _backend.Stage = stage;
        _backend.Error = error;
        Connecting();
        var result = await Enter(ticket);
        Assert.That(result, Is.EqualTo(expected));
        Assert.That(_launcher.Runner, Is.Null);
        Assert.That(_backend.Saves, Is.EqualTo(1), "Backend abort must never enter availability retries.");
        Assert.That(_store.PendingReservation, Is.Null);
        Assert.That(_store.GetPreparedEquipment(), Is.EqualTo(_equipment));
        Assert.That(_store.GetLoadout(), Is.Empty);
        Assert.That(_coordinator.ActiveTicket, Is.Null);
    }

    [TestCase(GameMode.Host, "ticket", "NO_ACTIVE_RESERVATION")]
    [TestCase(GameMode.Client, "save", "RESERVATION_REJECTED")]
    public async Task ProductionRecovery_BackendAbort_RollsBackStoreThroughFix02(GameMode mode, string stage, string error)
    {
        Set(_launcher, "_isStarting", false);
        // Fail Town recovery deterministically, after its FIX-02 rollback, without starting a runner.
        Set(_coordinator, "_townSceneName", "FIX55_UnbuiltTownScene");
        var ticket = Reserve(mode);
        _backend.Stage = stage;
        _backend.Error = error;
        Connecting();

        LogAssert.Expect(UnityEngine.LogType.Exception, new Regex("ArgumentException"));
        var result = await _coordinator.EnterRaidAsync(ticket);

        Assert.That(result, Is.EqualTo(SessionTransitionResult.RecoveryFailed));
        Assert.That(_coordinator.State, Is.EqualTo(SessionConnectionState.Failed));
        Assert.That(_launcher.Runner, Is.Null);
        Assert.That(_launcher.LastLaunchFailure, Is.EqualTo(SessionTransitionResult.LoadoutReservationFailed));
        Assert.That(_backend.Saves, Is.EqualTo(1));
        Assert.That(_backend.Requests, Is.EqualTo(stage == "save" ? 0 : 1));
        Assert.That(_store.PendingReservation, Is.Null);
        Assert.That(_store.GetPreparedEquipment(), Is.EqualTo(_equipment));
        Assert.That(_store.GetLoadout(), Is.Empty);
    }

    [Test]
    public void AuthenticatedWithoutInventoryService_PreAckRejectsAndRollsBack()
    {
        var (launch, party) = Launch(GameMode.Host);
        Assert.That(_coordinator.TryStoreRaidLaunchContext(launch, party), Is.EqualTo(RaidLaunchPreparationResult.Rejected));
        Assert.That(_coordinator.TryConsumeLastLaunchRejection(out var reason), Is.True);
        Assert.That(reason, Is.EqualTo(ExpeditionPreparationResult.ReservationFailed));
        Assert.That(_store.PendingReservation, Is.Null);
        Assert.That(_store.GetPreparedEquipment(), Is.EqualTo(_equipment));
        Assert.That(_coordinator.ActiveTicket, Is.Null);
    }

    [TestCase(GameMode.Host)]
    [TestCase(GameMode.Client)]
    public async Task Offline_PreservesLocalSnapshotAndSkipsBackend(GameMode mode)
    {
        _auth.Clear();
        var (launch, party) = Launch(mode);
        Assert.That(_coordinator.TryStoreRaidLaunchContext(launch, party), Is.EqualTo(RaidLaunchPreparationResult.Success));
        var ticket = _coordinator.ActiveTicket.Value;
        Assert.That(await _launcher.StartCoordinatedSessionAsync(ticket.Request.SessionName, mode, null,
            launch, ticket.LoadoutReservation), Is.False, "The busy safety stop follows successful local admission encoding.");
        Assert.That(_launcher.LastLaunchFailure, Is.Null);
        Assert.That(_launcher.LastStartShutdownReason, Is.EqualTo(ShutdownReason.Error));
        Assert.That(_backend.Saves, Is.Zero);
        Assert.That(_backend.Requests, Is.Zero);
        Assert.That(_store.PendingReservation.PreparedEquipment, Is.EqualTo(_equipment));
    }

    [TestCase(GameMode.Host)]
    [TestCase(GameMode.Client)]
    public async Task ConfirmedTicket_PreservesSuccessfulPreparationBeforeBusyStop(GameMode mode)
    {
        var ticket = Reserve(mode);
        _backend.Stage = "success";
        _backend.RawTicket = AuthoritativeRaidAdmissionTicketCodec.CreateTestTicket(Profile,
            ticket.LaunchContext.RaidCode, ticket.LoadoutReservation.ReservationId, _store.GetLevel(),
            _store.GetCurrentExperience(), ProgressionBalanceDefaults.InitialCharacterAttributeState,
            default, Array.Empty<LootEntry>(), _equipment);
        Assert.That(await _launcher.StartCoordinatedSessionAsync(ticket.Request.SessionName, mode, null,
            ticket.LaunchContext, ticket.LoadoutReservation), Is.False, "Only the existing busy guard stops this attempt.");
        Assert.That(_launcher.LastLaunchFailure, Is.Null);
        Assert.That(_launcher.LastStartShutdownReason, Is.EqualTo(ShutdownReason.Error));
        Assert.That(_backend.Saves, Is.EqualTo(1));
        Assert.That(_backend.Requests, Is.EqualTo(1));
        Assert.That(_store.PendingReservation.PreparedEquipment, Is.EqualTo(_equipment));
    }

    [TestCase("save")]
    [TestCase("ticket")]
    public async Task Unauthorized_RealInvalidationEffects_EndDefinedAndLeaveOrphan(string stage)
    {
        Set(_launcher, "_isStarting", false);
        var ticket = Reserve(GameMode.Client);
        _backend.Stage = stage;
        _backend.Error = BackendErrorUtility.Unauthorized;
        // Use the real invalidation; the save implementation invalidates before returning.
        // Context reset is also performed explicitly because this fixture does not own bootstrap's static context.
        _backend.InvalidateSave = () => { _auth.HandleUnauthorizedSession(); _stash.Initialize(null, null, null, null, null); };
        _auth.SessionInvalidated += () => _stash.Initialize(null, null, null, null, null);
        Connecting();
        Assert.That(await _coordinator.EnterRaidAsync(ticket), Is.EqualTo(SessionTransitionResult.LoadoutReservationFailed));
        Assert.That(_auth.IsAuthenticated, Is.False);
        Assert.That(_stash.Store, Is.Null);
        Assert.That(_coordinator.State, Is.EqualTo(SessionConnectionState.Failed));
        Assert.That(_coordinator.TryConsumeLastTransitionFailure(out var failure), Is.True);
        Assert.That(failure, Is.EqualTo(SessionTransitionResult.LoadoutReservationFailed));
        Assert.That(_launcher.Runner, Is.Null);
        Assert.That(_store.PendingReservation.ReservationId, Is.EqualTo(ticket.LoadoutReservation.ReservationId));
        Assert.That(ApplicationStashServiceBootstrapper.TryResolveOrphanedReservation(_store, admitted: false), Is.EqualTo(StashOperationResult.Success));
        Assert.That(_store.PendingReservation, Is.Null);
        Assert.That(_store.GetPreparedEquipment(), Is.EqualTo(_equipment));
    }

    private void Connecting()
    {
        Assert.That(_state.TryTransition(SessionConnectionState.PreparingRaid), Is.True);
        Assert.That(_state.TryTransition(SessionConnectionState.ConnectingRaid), Is.True);
    }

    private Task<SessionTransitionResult> Enter(RaidTransitionTicket ticket) =>
        (Task<SessionTransitionResult>)typeof(SessionConnectionCoordinator).GetMethod("EnterRaidAsync",
            BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(RaidTransitionTicket), typeof(bool) }, null)
            .Invoke(_coordinator, new object[] { ticket, false });

    private RaidTransitionTicket Reserve(GameMode mode)
    {
        Assert.That(_store.TryCreateLoadoutReservation("fix55-reservation", out var reservation), Is.EqualTo(StashOperationResult.Success));
        var (launch, _) = Launch(mode);
        var ticket = new RaidTransitionTicket(new RaidConnectionRequest(launch.RaidCode,
            mode == GameMode.Host ? RaidConnectionRole.Host : RaidConnectionRole.Client), reservation, SessionConnectionState.Town, launch);
        Set(_coordinator, "_activeTicket", (RaidTransitionTicket?)ticket);
        return ticket;
    }

    private static (RaidLaunchContext, TownPartyContinuationContext) Launch(GameMode mode)
    {
        RaidCode.TryParse("123456", out var code);
        RaidTeamId.TryCreate(1, out var team);
        var host = mode == GameMode.Host ? Profile : new ProfileId("59595959595959595959595959595959");
        var participants = mode == GameMode.Host ? new[] { new RaidLaunchParticipant(Profile, team) }
            : new[] { new RaidLaunchParticipant(Profile, team), new RaidLaunchParticipant(host, team) };
        Assert.That(RaidLaunchContext.TryCreate(code, host, participants, Profile, 1, out var launch), Is.True);
        Assert.That(TownPartyContinuationContext.TryCreate(Profile, new[] { Profile }, out var party), Is.True);
        return (launch, party);
    }

    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    private static void Static(Type type, string field, object value) => type.GetField(field, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value);

    private sealed class Backend : IRaidLaunchBackend
    {
        public string Stage = "ticket";
        public string Error = "NO_ACTIVE_RESERVATION";
        public string RawTicket;
        public int Saves;
        public int Requests;
        public Action InvalidateSave;
        public Task<(bool success, BackendError error)> SaveReservationAsync(ApplicationStashContext stash, PendingLoadoutReservation reservation)
        {
            Saves++;
            if (Stage == "save" && Error == BackendErrorUtility.Unauthorized) InvalidateSave?.Invoke();
            return Task.FromResult((Stage != "save", new BackendError { error = Stage == "save" ? Error : null }));
        }
        public Task<(bool success, AuthoritativeRaidAdmissionTicketResponse data, BackendError error)> RequestTicketAsync(
            ApplicationAuthContext auth, RaidLaunchContext launch, PendingLoadoutReservation reservation)
        {
            Requests++;
            bool success = Stage == "decode" || Stage == "success";
            return Task.FromResult((success, new AuthoritativeRaidAdmissionTicketResponse
                { ticket = Stage == "decode" ? "not-a-ticket" : RawTicket },
                new BackendError { error = success ? null : Error }));
        }
    }
}
#endif

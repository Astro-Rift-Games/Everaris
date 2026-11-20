using Fusion;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;
using LogType = UnityEngine.LogType;

/// <summary>
/// FIX-61 S4: the development-only direct raid route (MainMenu JoinRoom, DirectRaidDevelopmentStarter)
/// is unavailable outside the Editor and Development builds. A Release build is simulated through the
/// internal availability override; the compile-time exclusion itself is the <c>#if</c> in
/// <see cref="DevelopmentRaidRouteAvailability"/>.
/// </summary>
public sealed class DevelopmentRaidRouteAvailabilityTests
{
    private const string RefusalPattern = "development direct raid route is unavailable";
    private static readonly ProfileId Profile = new("61616161616161616161616161616161");
    private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    private GameObject _root;
    private ProfileId _previousProfile;
    private SessionConnectionCoordinator _coordinator;
    private SessionConnectionCoordinator _previousCoordinator;
    private LocalProfileStore _store;
    private PreparedEquipmentLoadout _prepared;

    [SetUp]
    public void SetUp()
    {
        _previousCoordinator = SessionConnectionCoordinator.Instance;
        _previousProfile = LocalProfileProvider.GetOrCreateLocalProfile();
        LocalProfileProvider.SetRemoteCharacterId(Profile);
        var catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
            "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
        var repository = new InMemoryLocalProfileRepository();
        Assert.That(repository.Initialize(Profile, catalog), Is.True);
        _store = new LocalProfileStore(repository, Profile, catalog);

        var weapon = new LootId("training_short_sword");
        Assert.That(_store.TryImportItems(new[] { new StashItem(weapon, 1) }), Is.EqualTo(StashOperationResult.Success));
        Assert.That(_store.TryAssignPreparedEquipment(EquipmentSlot.WeaponSetAMainHand, weapon),
            Is.EqualTo(StashOperationResult.Success));
        _prepared = _store.GetPreparedEquipment();

        _root = new GameObject(nameof(DevelopmentRaidRouteAvailabilityTests));
        var service = _root.AddComponent<InMemoryPlayerLoadoutService>();
        service.Initialize(_store);
        _root.AddComponent<ApplicationStashContext>().Initialize(_store, null, service, null, null);
        _coordinator = _root.AddComponent<SessionConnectionCoordinator>();
        // EditMode does not run the scene lifecycle; bind the existing application singleton seam.
        SetCoordinatorInstance(_coordinator);
        DevelopmentRaidRouteAvailability.SetAvailabilityOverrideForTests(null);
    }

    [TearDown]
    public void TearDown()
    {
        DevelopmentRaidRouteAvailability.SetAvailabilityOverrideForTests(null);
        LogAssert.ignoreFailingMessages = false;
        if (_root != null) Object.DestroyImmediate(_root);
        LocalProfileProvider.SetRemoteCharacterId(_previousProfile);
        SetCoordinatorInstance(_previousCoordinator);
    }

    // --- availability rule ------------------------------------------------

    [Test]
    public void Availability_DefaultsToTheCompiledBuildAndHonoursTheOverride()
    {
        // The Editor always compiles with UNITY_EDITOR, so the compiled default observable here is true.
        Assert.That(DevelopmentRaidRouteAvailability.IsAvailable, Is.True);

        DevelopmentRaidRouteAvailability.SetAvailabilityOverrideForTests(false);
        Assert.That(DevelopmentRaidRouteAvailability.IsAvailable, Is.False);

        DevelopmentRaidRouteAvailability.SetAvailabilityOverrideForTests(null);
        Assert.That(DevelopmentRaidRouteAvailability.IsAvailable, Is.True);
    }

    // --- coordinator -------------------------------------------------------

    [Test]
    public async Task Release_CoordinatorRefusesWithoutAnyLaunchSideEffect()
    {
        DevelopmentRaidRouteAvailability.SetAvailabilityOverrideForTests(false);
        LogAssert.Expect(LogType.Warning, new Regex(RefusalPattern));
        SessionConnectionState before = _coordinator.State;

        SessionTransitionResult result = await _coordinator.StartDirectRaidForDevelopmentAsync(
            "release-room", GameMode.Host);

        Assert.That(result, Is.EqualTo(SessionTransitionResult.ServiceUnavailable));
        Assert.That(_coordinator.State, Is.EqualTo(before));
        Assert.That(_coordinator.ActiveTicket, Is.Null);
        Assert.That(_store.PendingReservation, Is.Null, "A refused development start must not reserve the loadout.");
        Assert.That(_store.GetPreparedEquipment(), Is.EqualTo(_prepared), "Prepared equipment must stay untouched.");
        Assert.That(IsOperationActive(), Is.False);
    }

    [Test]
    public async Task Release_RefusalPrecedesEveryOtherValidation()
    {
        DevelopmentRaidRouteAvailability.SetAvailabilityOverrideForTests(false);
        LogAssert.Expect(LogType.Warning, new Regex(RefusalPattern));

        SessionTransitionResult result = await _coordinator.StartDirectRaidForDevelopmentAsync(
            string.Empty, GameMode.Server);

        Assert.That(result, Is.EqualTo(SessionTransitionResult.ServiceUnavailable));
    }

    [Test]
    public async Task Development_CoordinatorKeepsItsExistingValidation()
    {
        Assert.That(DevelopmentRaidRouteAvailability.IsAvailable, Is.True);

        Assert.That(
            await _coordinator.StartDirectRaidForDevelopmentAsync(string.Empty, GameMode.Host),
            Is.EqualTo(SessionTransitionResult.InvalidRequest));
        Assert.That(
            await _coordinator.StartDirectRaidForDevelopmentAsync("room", GameMode.Server),
            Is.EqualTo(SessionTransitionResult.InvalidRequest));
        Assert.That(_coordinator.State, Is.EqualTo(SessionConnectionState.MainMenu));
        Assert.That(_store.PendingReservation, Is.Null);
    }

    // --- DirectRaidDevelopmentStarter --------------------------------------

    [Test]
    public void Release_DirectRaidStarterContextMenuRoutesThroughTheCoordinatorGuard()
    {
        DevelopmentRaidRouteAvailability.SetAvailabilityOverrideForTests(false);
        var host = new GameObject(nameof(DirectRaidDevelopmentStarter));
        try
        {
            var ownCoordinator = host.AddComponent<SessionConnectionCoordinator>();
            host.AddComponent<DevelopmentProfileBootstrap>();
            var starter = host.AddComponent<DirectRaidDevelopmentStarter>();
            // EditMode does not run Awake; cache the sibling components exactly as the lifecycle would.
            typeof(DirectRaidDevelopmentStarter).GetMethod("Awake", NonPublicInstance).Invoke(starter, null);

            LogAssert.Expect(LogType.Warning, new Regex(RefusalPattern));
            LogAssert.Expect(LogType.Warning, new Regex("Direct raid attempt did not succeed: ServiceUnavailable"));
            typeof(DirectRaidDevelopmentStarter).GetMethod("StartDirectHostRaid", NonPublicInstance)
                .Invoke(starter, null);

            Assert.That(ownCoordinator.State, Is.EqualTo(SessionConnectionState.MainMenu));
            Assert.That(ownCoordinator.ActiveTicket, Is.Null);
            Assert.That(_store.PendingReservation, Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }

    // --- MainMenuController -------------------------------------------------

    [Test]
    public void Release_MainMenuJoinRoomDoesNothing()
    {
        DevelopmentRaidRouteAvailability.SetAvailabilityOverrideForTests(false);
        GameObject owner = CreateMenu(out MainMenuController menu, out TMP_InputField input, out TextMeshProUGUI status);
        try
        {
            InvokeJoinRoom(menu);

            Assert.That(status.text, Is.Empty, "JoinRoom must not announce a join attempt in a release build.");
            Assert.That(input.interactable, Is.True, "JoinRoom must not lock the UI in a release build.");
            Assert.That(typeof(MainMenuController).GetField("_isBusy", NonPublicInstance).GetValue(menu),
                Is.EqualTo(false));
            Assert.That(_coordinator.State, Is.EqualTo(SessionConnectionState.MainMenu));
            Assert.That(_store.PendingReservation, Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void Development_MainMenuJoinRoomStillAttemptsTheDirectRoute()
    {
        Assert.That(DevelopmentRaidRouteAvailability.IsAvailable, Is.True);
        GameObject owner = CreateMenu(out MainMenuController menu, out _, out TextMeshProUGUI status);
        try
        {
            // No raid launcher is wired, so the attempt fails after leaving the guard; the observable
            // proof is that the join flow ran (status moved off the empty initial value).
            LogAssert.ignoreFailingMessages = true;
            InvokeJoinRoom(menu);

            Assert.That(status.text, Is.Not.Empty, "A development build must still run the direct join flow.");
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    private GameObject CreateMenu(
        out MainMenuController menu,
        out TMP_InputField input,
        out TextMeshProUGUI status)
    {
        // Inactive owner avoids unrelated serialized button lifecycle requirements.
        var owner = new GameObject(nameof(MainMenuController));
        owner.SetActive(false);
        menu = owner.AddComponent<MainMenuController>();
        var inputObject = new GameObject("RoomCode", typeof(RectTransform));
        inputObject.transform.SetParent(owner.transform);
        input = inputObject.AddComponent<TMP_InputField>();
        input.text = "ABCDEF";
        var statusObject = new GameObject("Status", typeof(RectTransform));
        statusObject.transform.SetParent(owner.transform);
        status = statusObject.AddComponent<TextMeshProUGUI>();
        status.text = string.Empty;
        typeof(MainMenuController).GetField("roomCodeInput", NonPublicInstance).SetValue(menu, input);
        typeof(MainMenuController).GetField("_statusText", NonPublicInstance).SetValue(menu, status);
        typeof(MainMenuController).GetField("_connectionCoordinator", NonPublicInstance).SetValue(menu, _coordinator);
        return owner;
    }

    private static void InvokeJoinRoom(MainMenuController menu) =>
        typeof(MainMenuController).GetMethod(nameof(MainMenuController.JoinRoom)).Invoke(menu, null);

    private bool IsOperationActive() =>
        (bool)typeof(SessionConnectionCoordinator).GetField("_operationActive", NonPublicInstance)
            .GetValue(_coordinator);

    private static void SetCoordinatorInstance(SessionConnectionCoordinator coordinator) =>
        typeof(SessionConnectionCoordinator).GetField("<Instance>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, coordinator);
}

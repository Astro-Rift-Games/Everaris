using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class MainMenuControllerWiringTests
{
    private GameObject _root;
    private MainMenuController _menu;
    private ApplicationAuthContext _auth;
    private LoginPanelView _login;
    private CharacterCreationPanelView _character;
    private MainMenuDialogView _confirm;
    private MainMenuDialogView _info;
    private GameObject _authDialog;
    private Button _play;
    private TextMeshProUGUI _status;
    private TextMeshProUGUI _subtitle;
    private HealthFake _health;
    private BackendConfiguration _config;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject("Menu wiring tests");
        _root.SetActive(false);
        _menu = _root.AddComponent<MainMenuController>();
        _auth = Child("Auth context").AddComponent<ApplicationAuthContext>();
        var flow = Child("Login flow").AddComponent<LoginFlowController>();
        // No token means logout uses its real local teardown without an HTTP revocation.
        flow.ConfigureForTesting(null, null, _auth);
        Set(_menu, "_loginFlowController", flow);
        _config = ScriptableObject.CreateInstance<BackendConfiguration>();
        Set(_menu, "_backendConfig", _config);
        Set(_menu, "menuPanel", Child("Menu"));
        _play = Button("Play");
        Set(_menu, "createRoomButton", _play);
        foreach (string field in new[] { "_loginButton", "_logoutButton", "_settingsButton", "_creditsButton", "_exitButton" })
            Set(_menu, field, Button(field));
        Set(_menu, "_settingsPanel", Child("Settings").AddComponent<SettingsPanelView>());
        Set(_menu, "_creditsPanel", Child("Credits").AddComponent<CreditsPanelView>());
        _status = Label("Status");
        _subtitle = Label("Subtitle");
        Set(_menu, "_statusText", _status);
        Set(_menu, "_playSubtitle", _subtitle);
        _authDialog = Child("Auth dialog");
        Set(_menu, "_authDialogRoot", _authDialog);
        var loginObject = Child("Login");
        loginObject.transform.SetParent(_authDialog.transform);
        _login = loginObject.AddComponent<LoginPanelView>();
        Set(_menu, "_loginPanel", _login);
        var characterObject = Child("Character");
        characterObject.transform.SetParent(_authDialog.transform);
        _character = characterObject.AddComponent<CharacterCreationPanelView>();
        Set(_character, "_createButton", Button("Create"));
        Set(_character, "_stepText", Label("Character step"));
        Set(_character, "_finishLaterText", Label("Finish later"));
        Set(_character, "_finishLaterButton", Button("Finish later action"));
        Set(_menu, "_characterCreationPanel", _character);
        _confirm = Dialog("Confirm");
        _info = Dialog("Info");
        Set(_menu, "_confirmationDialog", _confirm);
        Set(_menu, "_infoDialog", _info);
        _health = new HealthFake();
        _menu.InitializeHealth(_health);
        _root.SetActive(true);
        Lifecycle(_auth, "Awake");
        Lifecycle(_menu, "OnEnable");
    }

    [TearDown]
    public void TearDown()
    {
        Lifecycle(_menu, "OnDisable");
        Lifecycle(_auth, "OnDestroy");
        Object.DestroyImmediate(_root);
        Object.DestroyImmediate(_config);
        LocalProfileProvider.ClearRemoteCharacterId();
    }

    [Test]
    public void Exit_OnlyAcceptQuits_CancelAndDoubleAcceptDoNothing()
    {
        int quits = 0;
        _menu.QuitAction = () => quits++;
        _menu.RequestExit();
        Assert.That(quits, Is.Zero);
        _confirm.Cancel();
        Assert.That(quits, Is.Zero);
        _menu.RequestExit();
        _confirm.Accept();
        _confirm.Accept();
        Assert.That(quits, Is.EqualTo(1));
    }

    [Test]
    public void Logout_CancelPreservesSession_AcceptClearsThroughFlow()
    {
        SignIn();
        _menu.RequestConfirmation(MenuConfirmation.SignOut);
        Assert.That(_auth.IsAuthenticated, Is.True);
        _confirm.Cancel();
        Assert.That(_auth.IsAuthenticated, Is.True);
        _menu.RequestConfirmation(MenuConfirmation.SignOut);
        _confirm.Accept();
        Assert.That(_auth.IsAuthenticated, Is.False);
        Assert.That(_menu.Flow.AccountText, Is.EqualTo("Sin sesión iniciada"));
    }

    [Test]
    public void SettingsAndCredits_StillOpenExistingViews()
    {
        Get<SettingsPanelView>(_menu, "_settingsPanel").Close();
        Get<CreditsPanelView>(_menu, "_creditsPanel").Close();
        Assert.That(Get<SettingsPanelView>(_menu, "_settingsPanel").IsOpen, Is.False);
        Assert.That(Get<CreditsPanelView>(_menu, "_creditsPanel").IsOpen, Is.False);
        LogAssert.Expect(LogType.Warning, "SettingsPanelView has no audio volume control; changes will be ignored.");
        Get<Button>(_menu, "_settingsButton").onClick.Invoke();
        Assert.That(Get<SettingsPanelView>(_menu, "_settingsPanel").IsOpen, Is.True);
        Get<Button>(_menu, "_creditsButton").onClick.Invoke();
        Assert.That(Get<CreditsPanelView>(_menu, "_creditsPanel").IsOpen, Is.True);
    }

    [Test]
    public void Play_AsGuestOpensLoginOverMenu()
    {
        _play.onClick.Invoke();
        Assert.That(_login.gameObject.activeInHierarchy, Is.True);
        Assert.That(_authDialog.activeSelf, Is.True);
    }

    [Test]
    public async Task Offline_DisablesActualPlayAndExplainsWhy()
    {
        _health.Result = false;
        // EditMode invokes lifecycle explicitly, matching LoginPanelViewTests.
        Lifecycle(_menu, "OnDisable");
        _root.SetActive(false);
        _root.SetActive(true);
        Lifecycle(_menu, "OnEnable");
        await Task.Yield();
        Assert.That(_play.interactable, Is.False);
        Assert.That(_subtitle.text, Is.EqualTo("Servidor no disponible"));
        _menu.CreateRoom();
        Assert.That(_login.gameObject.activeInHierarchy, Is.False);
    }

    [Test]
    public void SessionInvalidation_RefreshesChipAndNotice()
    {
        SignIn();
        LogAssert.Expect(LogType.Warning, "[ApplicationAuthContext] Session invalidated due to 401 UNAUTHORIZED. Clearing local auth and stash state.");
        _auth.HandleUnauthorizedSession();
        Assert.That(_menu.Flow.AccountText, Is.EqualTo("Sin sesión iniciada"));
        Assert.That(_status.text, Is.EqualTo("Tu sesión ha terminado. Inicia sesión de nuevo."));
    }

    [Test]
    public void Recovery_OpensInfoWithExplanation_AndReturnsToForm()
    {
        _menu.CreateRoom();
        _login.RequestPasswordRecovery();
        Assert.That(_info.IsOpen, Is.True);
        Assert.That(Get<TextMeshProUGUI>(_info, "_bodyText").text, Is.EqualTo(MainMenuFlow.RecoveryMessage));
        Assert.That(_login.State.CanSubmit, Is.False);
        _info.Cancel();
        Assert.That(_login.State.CanSubmit, Is.True);
    }

    [Test]
    public void CharacterFinishLater_LogsOutAndClosesSharedDialog()
    {
        SignIn();
        _menu.HandleLoginFlowResult(LoginFlowResult.Failure(LoginFlowStatus.NeedsCharacterCreation, null));
        Assert.That(_character.gameObject.activeInHierarchy, Is.True);
        Assert.That(Get<TextMeshProUGUI>(_character, "_stepText").text, Is.EqualTo("Paso 2 de 2 · Personaje"));
        Assert.That(Get<TextMeshProUGUI>(_character, "_finishLaterText").text, Is.EqualTo("Cerrar sesión y terminar después"));
        Get<Button>(_character, "_finishLaterButton").onClick.Invoke();
        Assert.That(_auth.IsAuthenticated, Is.False);
        Assert.That(_authDialog.activeSelf, Is.False);
    }

    [Test]
    public void SuccessfulLogin_ClosesSharedDialogBeforeAutomaticTownEntry()
    {
        AssumeNoRealConnectionCoordinator();
        _menu.CreateRoom();
        SignIn();
        // No coordinator in this fixture: the automatic Town entry fails fast instead of networking.
        LogAssert.Expect(LogType.Error, new Regex("Error entering Town"));
        _menu.HandleLoginFlowResult(LoginFlowResult.Success());
        Assert.That(_authDialog.activeSelf, Is.False);
        Assert.That(_menu.Flow.CharacterStep, Is.False);
    }

    [Test]
    public void SuccessfulLogin_AfterStaleUnreachableHealth_StillStartsTownEntry()
    {
        AssumeNoRealConnectionCoordinator();
        _menu.Flow.CheckHealthAsync(new HealthFake { Result = false }, _config).GetAwaiter().GetResult();
        SignIn();
        LogAssert.Expect(LogType.Error, new Regex("Error entering Town"));
        _menu.HandleLoginFlowResult(LoginFlowResult.Success());
        Assert.That(_menu.Flow.PlayEnabled, Is.True, "a successful backend login proves the server is reachable");
        Assert.That(_menu.Flow.CanRetryTown, Is.True, "Town entry was attempted (and failed only because the fixture has no coordinator)");
    }

    // Guards against reaching a real SessionConnectionCoordinator from an open scene: never network from tests.
    private static void AssumeNoRealConnectionCoordinator()
    {
        Assume.That(SessionConnectionCoordinator.Instance, Is.Null);
        Assume.That(Object.FindAnyObjectByType<SessionConnectionCoordinator>(FindObjectsInactive.Include), Is.Null);
    }

    private void SignIn() => _auth.Initialize(null, new CharacterData { characterId = "test", name = "Maren" }, new CharacterProfileData());
    private GameObject Child(string name)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(_root.transform);
        return child;
    }
    private Button Button(string name) => Child(name).AddComponent<Button>();
    private TextMeshProUGUI Label(string name) => Child(name).AddComponent<TextMeshProUGUI>();
    private MainMenuDialogView Dialog(string name)
    {
        var dialog = Child(name).AddComponent<MainMenuDialogView>();
        Set(dialog, "_titleText", Label(name + " title"));
        Set(dialog, "_bodyText", Label(name + " body"));
        Set(dialog, "_acceptText", Label(name + " accept"));
        Set(dialog, "_acceptButton", Button(name + " yes"));
        Set(dialog, "_cancelButton", Button(name + " no"));
        return dialog;
    }
    // Reflection only composes serialized dependencies; every assertion uses the public UI/action boundary.
    private static void Lifecycle(object owner, string method) => owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null);
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    private static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    private sealed class HealthFake : IBackendHealthClient
    {
        public bool Result = true;
        public Task<bool> CheckAsync(BackendConfiguration config) => Task.FromResult(Result);
    }
}

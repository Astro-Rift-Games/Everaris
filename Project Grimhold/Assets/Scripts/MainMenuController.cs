using Fusion;
using Grimhold.Backend;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MainMenuController : MonoBehaviour
{
    [SerializeField]
    private SessionConnectionCoordinator _connectionCoordinator;

    [SerializeField]
    private TMP_InputField roomCodeInput;

    [SerializeField]
    private GameObject menuPanel;

    [SerializeField]
    private Button createRoomButton;

    [SerializeField]
    private Button joinRoomButton;

    [SerializeField]
    private GameObject lobbyPanel;

    [SerializeField]
    private LobbyMenuController lobbyMenu;

    [SerializeField]
    private TextMeshProUGUI _statusText;

    [Header("Login")]
    [SerializeField]
    private LoginPanelView _loginPanel;

    [SerializeField]
    private CharacterCreationPanelView _characterCreationPanel;

    [SerializeField]
    private LoginFlowController _loginFlowController;

    [Header("Session actions")]
    [SerializeField] private Button _loginButton;
    [SerializeField] private Button _logoutButton;
    [SerializeField] private Button _settingsButton;
    [SerializeField] private Button _creditsButton;
    [SerializeField] private Button _exitButton;
    [SerializeField] private SettingsPanelView _settingsPanel;
    [SerializeField] private CreditsPanelView _creditsPanel;

    [Header("Redesigned menu (optional on legacy scene)")]
    [SerializeField] private BackendConfiguration _backendConfig;
    [SerializeField] private TextMeshProUGUI _accountText;
    [SerializeField] private TextMeshProUGUI _accountActionText;
    [SerializeField] private Button _accountActionButton;
    [SerializeField] private TextMeshProUGUI _serverText;
    [SerializeField] private TextMeshProUGUI _playSubtitle;
    [SerializeField] private Button _serverRetryButton;
    [SerializeField] private Button _townRetryButton;
    [SerializeField] private MainMenuDialogView _confirmationDialog;
    [SerializeField] private MainMenuDialogView _infoDialog;
    [SerializeField] private GameObject _authDialogRoot;

    private readonly MainMenuFlow _flow = new();
    private IBackendHealthClient _healthClient = new BackendHealthClient();
    private bool _modalOpen;
    private int _enableGeneration;
    private bool _isBusy;
    public MainMenuFlow Flow => _flow;
    public void InitializeHealth(IBackendHealthClient client) => _healthClient = client ?? throw new ArgumentNullException(nameof(client));
    private ApplicationAuthContext _subscribedAuthContext;

    public Action QuitAction { get; set; } = ApplicationExit.Quit;

    private LoginFlowController LoginFlow
    {
        get
        {
            if (_loginFlowController == null)
            {
                _loginFlowController = LoginFlowController.Instance ?? FindAnyObjectByType<LoginFlowController>();
            }
            return _loginFlowController;
        }
    }

    private SessionConnectionCoordinator ConnectionCoordinator
    {
        get
        {
            if (_connectionCoordinator == null)
            {
                _connectionCoordinator = SessionConnectionCoordinator.Instance ?? FindAnyObjectByType<SessionConnectionCoordinator>();
            }
            return _connectionCoordinator;
        }
    }

    private void OnEnable()
    {
        _enableGeneration++;
        if (createRoomButton != null) createRoomButton.onClick.AddListener(CreateRoom);
        if (joinRoomButton != null) joinRoomButton.onClick.AddListener(JoinRoom);
        if (createRoomButton == null || menuPanel == null)
            Debug.LogError($"[{nameof(MainMenuController)}] Assign Play button and menu panel references.", this);
        if (roomCodeInput != null) roomCodeInput.gameObject.SetActive(false);
        if (joinRoomButton != null) joinRoomButton.gameObject.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (menuPanel != null) menuPanel.SetActive(true);
        _flow.Changed += RenderMenu;
        _accountActionButton?.onClick.AddListener(OnAccountAction);
        _serverRetryButton?.onClick.AddListener(CheckServer);
        _townRetryButton?.onClick.AddListener(RetryTown);
        if (_confirmationDialog != null) _confirmationDialog.gameObject.SetActive(false);
        if (_infoDialog != null) _infoDialog.gameObject.SetActive(false);

        _loginButton?.onClick.AddListener(ShowLoginPanel);
        _logoutButton?.onClick.AddListener(OnLogoutButtonClicked);
        _settingsButton?.onClick.AddListener(OnSettingsButtonClicked);
        _creditsButton?.onClick.AddListener(OnCreditsButtonClicked);
        _exitButton?.onClick.AddListener(OnExitButtonClicked);

        if (((_loginButton == null || _logoutButton == null) && _accountActionButton == null) ||
            _settingsButton == null || _creditsButton == null || _exitButton == null ||
            _settingsPanel == null || _creditsPanel == null)
        {
            Debug.LogError($"[{nameof(MainMenuController)}] Assign all session action buttons and SettingsPanelView/CreditsPanelView references.", this);
        }

        _subscribedAuthContext = ApplicationAuthContext.Instance;
        if (_subscribedAuthContext != null)
        {
            _subscribedAuthContext.SessionInvalidated += OnSessionInvalidated;
        }

        SetStatus(string.Empty);
        RefreshSessionActions();

        if (_characterCreationPanel != null)
        {
            _characterCreationPanel.gameObject.SetActive(false);
            _characterCreationPanel.AddCreateListener(OnCreateCharacterButtonClicked);
            _characterCreationPanel.AddFinishLaterListener(FinishCharacterLater);
        }

        if (_loginPanel != null)
        {
            _loginPanel.gameObject.SetActive(false);
            _loginPanel.SetStatus(string.Empty);
            _loginPanel.AddLoginListener(OnLoginButtonClicked);
            _loginPanel.AddRegisterListener(OnRegisterButtonClicked);
            _loginPanel.AddBackListener(OnLoginBackButtonClicked);
            _loginPanel.Initialize(new PlayerPrefsRememberedUsernameStore(), () => Time.realtimeSinceStartupAsDouble);
            _loginPanel.AddForgotPasswordListener(ShowPasswordRecovery);
        }
        else
        {
            Debug.LogError($"[{nameof(MainMenuController)}] No LoginPanelView assigned.");
        }
        if (_authDialogRoot != null) _authDialogRoot.SetActive(false);
        CheckServer();
    }

    private void OnDisable()
    {
        _enableGeneration++;
        _modalOpen = false;
        _flow.ResolveConfirmation(false);
        _flow.Changed -= RenderMenu;
        if (createRoomButton != null) createRoomButton.onClick.RemoveListener(CreateRoom);
        if (joinRoomButton != null) joinRoomButton.onClick.RemoveListener(JoinRoom);
        _accountActionButton?.onClick.RemoveListener(OnAccountAction);
        _serverRetryButton?.onClick.RemoveListener(CheckServer);
        _townRetryButton?.onClick.RemoveListener(RetryTown);
        _loginButton?.onClick.RemoveListener(ShowLoginPanel);
        _logoutButton?.onClick.RemoveListener(OnLogoutButtonClicked);
        _settingsButton?.onClick.RemoveListener(OnSettingsButtonClicked);
        _creditsButton?.onClick.RemoveListener(OnCreditsButtonClicked);
        _exitButton?.onClick.RemoveListener(OnExitButtonClicked);
        if (_subscribedAuthContext != null)
        {
            _subscribedAuthContext.SessionInvalidated -= OnSessionInvalidated;
        }
        _subscribedAuthContext = null;

        if (_loginPanel != null)
        {
            _loginPanel.RemoveLoginListener(OnLoginButtonClicked);
            _loginPanel.RemoveRegisterListener(OnRegisterButtonClicked);
            _loginPanel.RemoveBackListener(OnLoginBackButtonClicked);
            _loginPanel.RemoveForgotPasswordListener(ShowPasswordRecovery);
        }

        if (_characterCreationPanel != null)
        {
            _characterCreationPanel.RemoveCreateListener(OnCreateCharacterButtonClicked);
            _characterCreationPanel.RemoveFinishLaterListener(FinishCharacterLater);
        }
    }

    private async void OnLoginButtonClicked()
    {
        if (_isBusy || _modalOpen) return;
        var loginFlow = LoginFlow;
        if (loginFlow == null)
        {
            Debug.LogError($"[{nameof(MainMenuController)}] LoginFlowController not assigned.");
            return;
        }

        _loginPanel.SetInteractable(false);
        _loginPanel.SetStatus("Iniciando sesión…");
        SetUIInteractable(false);

        string username = _loginPanel.Username;
        try
        {
            LoginFlowResult result;
            if (loginFlow.HasHydrationFailed && loginFlow.PendingUsername == username)
            {
                result = await loginFlow.RetryHydrationAsync();
            }
            else
            {
                result = await loginFlow.ExecuteLoginAsync(username, _loginPanel.Password);
            }
            if (this != null) HandleLoginFlowResult(result);
        }
        catch (Exception ex)
        {
            if (this == null) return;
            Debug.LogException(ex, this);
            _loginPanel.SetStatus("No se pudo iniciar sesión. Inténtalo de nuevo.");
            _loginPanel.SetInteractable(true);
            SetUIInteractable(true);
        }
    }

    private async void OnRegisterButtonClicked()
    {
        if (_isBusy || _modalOpen) return;
        var loginFlow = LoginFlow;
        if (loginFlow == null)
        {
            Debug.LogError($"[{nameof(MainMenuController)}] LoginFlowController not assigned.");
            return;
        }

        _loginPanel.SetInteractable(false);
        _loginPanel.SetStatus("Registrando cuenta…");
        SetUIInteractable(false);

        try
        {
            LoginFlowResult result = await loginFlow.ExecuteRegisterAsync(
                _loginPanel.Username,
                _loginPanel.Password);
            if (this != null) HandleLoginFlowResult(result);
        }
        catch (Exception ex)
        {
            if (this == null) return;
            Debug.LogException(ex, this);
            _loginPanel.SetStatus("No se pudo registrar la cuenta. Inténtalo de nuevo.");
            _loginPanel.SetInteractable(true);
            SetUIInteractable(true);
        }
    }

    public void HandleLoginFlowResult(LoginFlowResult result)
    {
        var route = _flow.ApplyLoginResult(result);
        _loginPanel?.ApplyResult(result);
        SetUIInteractable(true);
        if (route == MenuPlayRoute.Town)
        {
            _loginPanel?.SetStatus(string.Empty);
            if (_loginPanel != null) UiPanelVisibility.SetVisible(_loginPanel.gameObject, false);
            if (_authDialogRoot != null) _authDialogRoot.SetActive(false);
            RefreshSessionActions();
            CreateRoom();
        }
        else if (result.Status == LoginFlowStatus.NeedsCharacterCreation)
        {
            if (_authDialogRoot != null) _authDialogRoot.SetActive(true);
            if (_loginPanel != null) UiPanelVisibility.SetVisible(_loginPanel.gameObject, false);

            if (_characterCreationPanel != null)
            {
                _characterCreationPanel.Open();
                _characterCreationPanel.SetStatus("Elige el nombre de tu personaje.");
                _characterCreationPanel.SetInteractable(true);
            }
            else
            {
                Debug.LogError($"[{nameof(MainMenuController)}] Needs character creation but no panel assigned!");
            }
        }
        else
        {
            _loginPanel?.SetInteractable(true);
        }
    }

    private async void OnCreateCharacterButtonClicked()
    {
        if (_isBusy || _modalOpen) return;
        var loginFlow = LoginFlow;
        if (loginFlow == null) return;

        _characterCreationPanel.SetInteractable(false);
        _characterCreationPanel.SetStatus("Creando personaje…");
        SetUIInteractable(false);

        LoginFlowResult result;
        try
        {
            result = await loginFlow.CreateCharacterAsync(_characterCreationPanel.CharacterName);
        }
        catch (Exception ex)
        {
            if (this == null) return;
            Debug.LogException(ex, this);
            _characterCreationPanel.SetStatus("No se pudo crear el personaje. Inténtalo de nuevo.");
            _characterCreationPanel.SetInteractable(true);
            SetUIInteractable(true);
            return;
        }
        if (this == null) return;
        SetUIInteractable(true);

        if (result.IsSuccess)
        {
            _characterCreationPanel.SetStatus(string.Empty);
            UiPanelVisibility.SetVisible(_characterCreationPanel.gameObject, false);
            HandleLoginFlowResult(result);
        }
        else
        {
            _characterCreationPanel.SetStatus("No se pudo crear el personaje. Inténtalo de nuevo.");
            _characterCreationPanel.SetInteractable(true);
        }
    }

    private void RefreshSessionActions()
    {
        var actions = new MainMenuSessionActions(
            ApplicationAuthContext.Instance?.IsAuthenticated == true, _isBusy);
        _flow.SetAccount(ApplicationAuthContext.Instance?.IsAuthenticated == true,
            ApplicationAuthContext.Instance?.CharacterName);
        if (createRoomButton != null) createRoomButton.gameObject.SetActive(true);
        if (_loginButton != null) _loginButton.gameObject.SetActive(actions.LoginVisible);
        if (_logoutButton != null) _logoutButton.gameObject.SetActive(actions.LogoutVisible);
        if (_loginButton != null) _loginButton.interactable = actions.SecondaryInteractable;
        if (_logoutButton != null) _logoutButton.interactable = actions.SecondaryInteractable;
        if (_settingsButton != null) _settingsButton.interactable = actions.SecondaryInteractable;
        if (_creditsButton != null) _creditsButton.interactable = actions.SecondaryInteractable;
        if (_exitButton != null) _exitButton.interactable = actions.SecondaryInteractable;
        RenderMenu();
    }

    private void SetStatus(string message)
    {
        if (_statusText == null) return;
        _statusText.text = message;
        _statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    private void OnSettingsButtonClicked()
    {
        if (!_isBusy && !_modalOpen) _settingsPanel?.Open();
    }

    private void OnCreditsButtonClicked()
    {
        if (!_isBusy && !_modalOpen) _creditsPanel?.Open();
    }

    public void RequestExit() => RequestConfirmation(MenuConfirmation.Exit);
    private void OnExitButtonClicked() => RequestExit();
    private void OnLogoutButtonClicked() => RequestConfirmation(MenuConfirmation.SignOut);

    public void RequestConfirmation(MenuConfirmation action)
    {
        if (_isBusy || _modalOpen) return;
        if (_confirmationDialog == null)
        {
            Debug.LogError($"[{nameof(MainMenuController)}] Assign MainMenuDialogView confirmation reference.", this);
            return; // Never silently bypass confirmation on the legacy scene.
        }
        _flow.RequestConfirmation(action);
        _modalOpen = true;
        RenderMenu();
        bool exit = action == MenuConfirmation.Exit;
        _confirmationDialog.Open(exit ? "¿Salir de Grimhold?" : "¿Cerrar sesión?",
            exit ? "El juego se cerrará." : "Necesitarás tu contraseña para volver a iniciar sesión.",
            exit ? "Salir" : "Cerrar sesión", accepted =>
            {
                _modalOpen = false;
                var confirmed = _flow.ResolveConfirmation(accepted);
                RenderMenu();
                if (confirmed == MenuConfirmation.Exit) QuitAction?.Invoke();
                else if (confirmed == MenuConfirmation.SignOut) ExecuteLogout();
            });
    }

    private async void ExecuteLogout()
    {
        if (_isBusy) return;
        SetUIInteractable(false);
        SetStatus("Cerrando sesión…");
        try
        {
            var loginFlow = LoginFlow;
            if (loginFlow == null)
            {
                throw new InvalidOperationException("LoginFlowController is unavailable.");
            }
            await loginFlow.ExecuteLogoutAsync();
            if (this == null) return;
            if (_loginPanel != null) _loginPanel.gameObject.SetActive(false);
            if (_characterCreationPanel != null) _characterCreationPanel.gameObject.SetActive(false);
            if (_authDialogRoot != null) _authDialogRoot.SetActive(false);
            SetStatus("Sesión cerrada. Inicia sesión para continuar.");
            RefreshSessionActions();
        }
        catch (Exception ex)
        {
            if (this == null) return;
            Debug.LogException(ex, this);
            SetStatus("No se pudo cerrar sesión. Inténtalo de nuevo.");
        }
        finally
        {
            if (this != null) SetUIInteractable(true);
        }
    }

    private void RefreshConnectionButtons()
    {
        RefreshSessionActions();
        if (joinRoomButton != null) joinRoomButton.interactable = !_isBusy;
    }

    private void SetUIInteractable(bool interactable)
    {
        _isBusy = !interactable;
        if (roomCodeInput != null) roomCodeInput.interactable = interactable;
        _flow.SetBusy(!interactable);
        RefreshConnectionButtons();
    }

    public async void CreateRoom()
    {
        if (_isBusy || _modalOpen) return;
        RefreshSessionActions();
        var route = _flow.RequestPlay();
        if (route == MenuPlayRoute.None) return;
        if (route == MenuPlayRoute.SignIn) { ShowLoginPanel(); return; }

        SetUIInteractable(false);
        SetStatus("Entrando al Town…");

        try
        {
            var coordinator = ConnectionCoordinator;
            if (coordinator == null)
            {
                throw new InvalidOperationException("Session connection coordinator is unavailable.");
            }

            SessionTransitionResult result =
                await coordinator.ConnectToTownAsync();
            if (this == null) return;

            if (result != SessionTransitionResult.Succeeded)
            {
                ShowTownConnectionFailure(result);
                UiPanelVisibility.SetVisible(menuPanel, true);
                SetUIInteractable(true);
            }
            else
            {
                SetUIInteractable(true);
                SetStatus(string.Empty);
            }
        }
        catch (Exception ex)
        {
            if (this == null) return;
            Debug.LogError($"Error entering Town: {ex.Message}");
            _flow.TownFailed("No se pudo entrar al Town. Inténtalo de nuevo.");
            SetStatus(_flow.Status);
            UiPanelVisibility.SetVisible(menuPanel, true);
            SetUIInteractable(true);
        }
    }

    private void ShowTownConnectionFailure(SessionTransitionResult result)
    {
        if (SessionTransitionFailureText.TryGetMessage(result, out string message))
        {
            _flow.TownFailed(message);
            SetStatus(message);
        }
    }

    private void ShowLoginPanel()
    {
        if (_isBusy || _modalOpen) return;
        SetStatus(string.Empty);
        if (_loginPanel == null)
        {
            SetStatus("El inicio de sesión no está disponible.");
            Debug.LogError($"[{nameof(MainMenuController)}] Cannot continue without a LoginPanelView.");
            return;
        }

        if (_authDialogRoot != null) _authDialogRoot.SetActive(true);
        if (_characterCreationPanel != null) _characterCreationPanel.gameObject.SetActive(false);
        _loginPanel.SetInteractable(true);
        UiPanelVisibility.SetVisible(_loginPanel.gameObject, true);
        _loginPanel.Open();
    }

    private void OnLoginBackButtonClicked()
    {
        if (_isBusy || _modalOpen) return;
        _loginPanel.ClearFields();
        _loginPanel.SetStatus(string.Empty);
        UiPanelVisibility.SetVisible(_loginPanel.gameObject, false);
        if (_authDialogRoot != null) _authDialogRoot.SetActive(false);
        UiPanelVisibility.SetVisible(menuPanel, true);
        RefreshSessionActions();
    }

    private void OnAccountAction()
    {
        if (ApplicationAuthContext.Instance?.IsAuthenticated == true) OnLogoutButtonClicked();
        else ShowLoginPanel();
    }

    public void RetryTown()
    {
        if (!_isBusy && !_modalOpen && _flow.RetryTown() != MenuPlayRoute.None) CreateRoom();
    }

    private void OnSessionInvalidated()
    {
        _flow.SessionInvalidated();
        SetStatus(_flow.Status);
        RefreshSessionActions();
    }

    public void ShowPasswordRecovery()
    {
        if (_isBusy || _modalOpen) return;
        if (_infoDialog == null)
        {
            Debug.LogError($"[{nameof(MainMenuController)}] Assign MainMenuDialogView info reference.", this);
            return;
        }
        _modalOpen = true;
        _loginPanel?.SetInteractable(false);
        RenderMenu();
        _infoDialog.Open("¿Olvidaste tu contraseña?", MainMenuFlow.RecoveryMessage, null, _ =>
        {
            _modalOpen = false;
            _loginPanel?.SetInteractable(true);
            RenderMenu();
        });
    }

    public void FinishCharacterLater()
    {
        if (_flow.RequestFinishLater() && !_modalOpen) ExecuteLogout();
    }

    private async void CheckServer()
    {
        if (_isBusy || _modalOpen) return;
        int generation = _enableGeneration;
        var config = _backendConfig != null ? _backendConfig : ApplicationAuthContext.Instance?.BackendConfig;
        if (config == null) config = LoginFlow?.Config;
        if (config == null)
            Debug.LogError($"[{nameof(MainMenuController)}] Assign BackendConfiguration for GET /health.", this);
        await _flow.CheckHealthAsync(_healthClient, config);
        if (this == null || generation != _enableGeneration) return;
        RenderMenu();
    }

    private void RenderMenu()
    {
        if (this == null || !isActiveAndEnabled) return;
        bool enabled = !_isBusy && !_modalOpen;
        if (createRoomButton != null) createRoomButton.interactable = enabled && _flow.PlayEnabled;
        if (_accountText != null) _accountText.text = _flow.AccountText;
        if (_accountActionText != null) _accountActionText.text = _flow.AccountActionText;
        if (_serverText != null) _serverText.text = _flow.ServerText;
        if (_playSubtitle != null) _playSubtitle.text = _flow.PlaySubtitle;
        if (_accountActionButton != null) _accountActionButton.interactable = enabled;
        if (_serverRetryButton != null) { _serverRetryButton.gameObject.SetActive(_flow.CanRetryHealth); _serverRetryButton.interactable = enabled; }
        if (_townRetryButton != null) { _townRetryButton.gameObject.SetActive(_flow.CanRetryTown); _townRetryButton.interactable = enabled && _flow.PlayEnabled; }
        if (_settingsButton != null) _settingsButton.interactable = enabled;
        if (_creditsButton != null) _creditsButton.interactable = enabled;
        if (_exitButton != null) _exitButton.interactable = enabled;
        if (_loginButton != null) _loginButton.interactable = enabled;
        if (_logoutButton != null) _logoutButton.interactable = enabled;
    }

    public async void JoinRoom()
    {
        if (_isBusy || _modalOpen || roomCodeInput == null) return;
        if (string.IsNullOrEmpty(roomCodeInput.text))
        {
            SetStatus("Introduce un código de sala.");
            return;
        }

        if (roomCodeInput.text.Length < 6)
        {
            SetStatus("Código de sala no válido.");
            return;
        }

        SetUIInteractable(false);
        SetStatus("Uniéndose…");

        SessionTransitionResult result = SessionTransitionResult.ConnectionFailed;
        try
        {
            var coordinator = ConnectionCoordinator;
            if (coordinator == null)
            {
                throw new InvalidOperationException("Session connection coordinator is unavailable.");
            }

            result = await coordinator.StartDirectRaidForDevelopmentAsync(
                roomCodeInput.text,
                GameMode.Client);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error joining room: {ex.Message}");
            SetStatus("No se pudo entrar a la sala. Inténtalo de nuevo.");
        }

        if (this == null) return;
        if (result == SessionTransitionResult.Succeeded)
        {
            SetUIInteractable(true);
            SetStatus(string.Empty);
        }
        else
        {
            SetUIInteractable(true);
            if (_statusText != null && _statusText.text == "Uniéndose…")
            {
                SetStatus("No se pudo entrar a la sala: está cerrada, llena o no existe.");
            }
        }
    }
}

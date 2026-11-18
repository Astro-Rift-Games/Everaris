using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Auth form presentation. Existing listener signatures remain the controller boundary;
/// optional dialog references are wired by the MainMenu composition, not found at runtime.
/// </summary>
public sealed class LoginPanelView : MonoBehaviour
{
    [SerializeField] private TMP_InputField _usernameInput;
    [SerializeField] private TMP_InputField _passwordInput;
    [SerializeField] private Button _loginButton;
    [SerializeField] private Button _registerButton;
    [SerializeField] private Button _backButton;
    [SerializeField] private TextMeshProUGUI _statusText;

    [Header("Auth dialog (optional on legacy prefabs)")]
    [SerializeField] private TMP_InputField _confirmPasswordInput;
    [SerializeField] private Button _signInTabButton;
    [SerializeField] private Button _createAccountTabButton;
    [SerializeField] private Button _submitButton;
    [SerializeField] private Button _showPasswordButton;
    [SerializeField] private Button _forgotPasswordButton;
    [SerializeField] private Toggle _rememberUsernameToggle;
    [SerializeField] private TextMeshProUGUI _rememberUsernameLabel;
    [SerializeField] private TextMeshProUGUI _usernameErrorText;
    [SerializeField] private TextMeshProUGUI _passwordErrorText;
    [SerializeField] private TextMeshProUGUI _confirmPasswordErrorText;
    [SerializeField] private TextMeshProUGUI _capsLockWarningText;
    [SerializeField] private TextMeshProUGUI _showPasswordText;
    [SerializeField] private TextMeshProUGUI _submitText;
    [SerializeField] private TextMeshProUGUI _titleText;
    [SerializeField] private TextMeshProUGUI _leadText;
    [SerializeField] private GameObject _accountStep;
    [SerializeField] private GameObject _confirmPasswordGroup;

    private readonly UnityEvent _loginRequested = new();
    private readonly UnityEvent _registerRequested = new();
    private readonly UnityEvent _backRequested = new();
    private readonly UnityEvent _forgotRequested = new();
    private LoginFormState _state;
    private bool _interactable = true;
    private bool _passwordVisible;
    private bool _capsLock;
    private string _status = "";

    public string Username => _usernameInput != null ? _usernameInput.text.Trim() : "";
    public string Password => _passwordInput != null ? _passwordInput.text : "";
    public LoginFormMode Mode => State.Mode;
    public LoginFormState State => _state ??= new LoginFormState(new PlayerPrefsRememberedUsernameStore(), () => Time.realtimeSinceStartupAsDouble);

    /// <summary>Composition/testing seam. Supply only a non-secret username store and monotonic clock.</summary>
    public void Initialize(IRememberedUsernameStore store, Func<double> now)
        => _state = new LoginFormState(store, now);

    private void OnEnable()
    {
        _loginButton?.onClick.AddListener(LoginClicked);
        _registerButton?.onClick.AddListener(RegisterClicked);
        _backButton?.onClick.AddListener(BackClicked);
        _submitButton?.onClick.AddListener(SubmitClicked);
        _signInTabButton?.onClick.AddListener(SelectSignIn);
        _createAccountTabButton?.onClick.AddListener(SelectCreateAccount);
        _showPasswordButton?.onClick.AddListener(ShowPasswordClicked);
        _forgotPasswordButton?.onClick.AddListener(ForgotClicked);
        _rememberUsernameToggle?.onValueChanged.AddListener(RememberChanged);
        _usernameInput?.onEndEdit.AddListener(UsernameEdited);
        Open();
    }

    private void OnDisable()
    {
        _loginButton?.onClick.RemoveListener(LoginClicked);
        _registerButton?.onClick.RemoveListener(RegisterClicked);
        _backButton?.onClick.RemoveListener(BackClicked);
        _submitButton?.onClick.RemoveListener(SubmitClicked);
        _signInTabButton?.onClick.RemoveListener(SelectSignIn);
        _createAccountTabButton?.onClick.RemoveListener(SelectCreateAccount);
        _showPasswordButton?.onClick.RemoveListener(ShowPasswordClicked);
        _forgotPasswordButton?.onClick.RemoveListener(ForgotClicked);
        _rememberUsernameToggle?.onValueChanged.RemoveListener(RememberChanged);
        _usernameInput?.onEndEdit.RemoveListener(UsernameEdited);
        ClearSecrets();
    }

    public void Open()
    {
        string username = State.Open();
        if (_usernameInput != null) _usernameInput.SetTextWithoutNotify(username);
        ClearSecrets();
        _passwordVisible = false;
        SetPasswordContentType();
        _capsLock = false;
        _status = "";
        if (_rememberUsernameToggle != null) _rememberUsernameToggle.SetIsOnWithoutNotify(State.RememberUsername);
        Refresh();
        var focus = username.Length > 0 ? _passwordInput : _usernameInput;
        if (focus != null && isActiveAndEnabled) { focus.Select(); focus.ActivateInputField(); }
    }

    public void SetMode(LoginFormMode mode)
    {
        if (!_interactable) return;
        State.SetMode(mode);
        _status = "";
        if (_confirmPasswordInput != null) _confirmPasswordInput.SetTextWithoutNotify("");
        Refresh();
    }

    public void Submit()
    {
        if (!State.CanSubmit) return;
        string confirm = _confirmPasswordInput != null ? _confirmPasswordInput.text : Password;
        if (!State.Validate(Username, Password, confirm)) { _status = ""; Refresh(); return; }
        if (State.RememberUsername) State.SetRememberUsername(true, Username);
        _status = "";
        Refresh();
        if (Mode == LoginFormMode.SignIn) _loginRequested.Invoke();
        else _registerRequested.Invoke();
    }

    public void ApplyResult(LoginFlowResult result)
    {
        State.ApplyResult(result);
        _status = "";
        Refresh();
    }

    public void SetInteractable(bool interactable)
    {
        _interactable = interactable;
        State.SetBusy(!interactable);
        Refresh();
    }

    public void ClearFields()
    {
        if (_usernameInput != null) _usernameInput.SetTextWithoutNotify("");
        ClearSecrets();
    }

    private void ClearSecrets()
    {
        if (_passwordInput != null) _passwordInput.SetTextWithoutNotify("");
        if (_confirmPasswordInput != null) _confirmPasswordInput.SetTextWithoutNotify("");
    }

    public void SetStatus(string message)
    {
        _status = message ?? "";
        Refresh();
    }

    public void TogglePasswordVisibility()
    {
        if (!_interactable) return;
        _passwordVisible = !_passwordVisible;
        SetPasswordContentType();
        Refresh();
    }

    private void SetPasswordContentType()
    {
        var type = _passwordVisible ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
        if (_passwordInput != null) { _passwordInput.contentType = type; _passwordInput.ForceLabelUpdate(); }
        if (_confirmPasswordInput != null) { _confirmPasswordInput.contentType = type; _confirmPasswordInput.ForceLabelUpdate(); }
    }

    public void SetCapsLock(bool enabled) { _capsLock = enabled; Refresh(); }
    public void RequestBack() { if (_interactable) _backRequested.Invoke(); }
    public void RequestPasswordRecovery() { if (_interactable && Mode == LoginFormMode.SignIn) _forgotRequested.Invoke(); }

    /// <summary>Single keyboard dispatch path: Escape takes precedence over Enter.</summary>
    public void HandleKeyboard(bool enterPressed, bool escapePressed)
    {
        if (escapePressed) RequestBack();
        else if (enterPressed) Submit();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null)
            HandleKeyboard(keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame,
                keyboard.escapeKey.wasPressedThisFrame);
        Refresh();
    }

    private void OnGUI()
    {
        // Input System KeyControl reports a key press, not the OS lock state.
        // IMGUI key events carry the actual CapsLock modifier, including a lock set before opening.
        var current = Event.current;
        if (current != null && current.isKey) SetCapsLock(current.capsLock);
    }

    // The keyboard path above owns Enter. Suppress EventSystem's simultaneous button submit.
    private static bool EnterPressed => Keyboard.current != null &&
        (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);
    private void SubmitClicked() { if (!EnterPressed) Submit(); }
    private void LoginClicked() { if (!EnterPressed) { SetMode(LoginFormMode.SignIn); Submit(); } }
    private void RegisterClicked()
    {
        if (EnterPressed) return;
        if (Mode != LoginFormMode.CreateAccount)
        {
            SetMode(LoginFormMode.CreateAccount);
            // Legacy prefab has no tabs or confirmation input: retain its one-click register action.
            if (_confirmPasswordInput != null) return;
        }
        Submit();
    }
    private void BackClicked() { if (!EnterPressed) RequestBack(); }
    private void ForgotClicked() { if (!EnterPressed) RequestPasswordRecovery(); }
    private void ShowPasswordClicked() { if (!EnterPressed) TogglePasswordVisibility(); }
    private void SelectSignIn() { if (!EnterPressed) SetMode(LoginFormMode.SignIn); }
    private void SelectCreateAccount() { if (!EnterPressed) SetMode(LoginFormMode.CreateAccount); }
    private void RememberChanged(bool remember) { if (_interactable) State.SetRememberUsername(remember, Username); }
    private void UsernameEdited(string username) { if (_interactable && State.RememberUsername) State.SetRememberUsername(true, username); }

    /// <summary>Refresh countdown/field presentation without changing the externally owned busy state.</summary>
    public void Refresh()
    {
        bool register = Mode == LoginFormMode.CreateAccount;
        SetInput(_usernameInput, _interactable); SetInput(_passwordInput, _interactable); SetInput(_confirmPasswordInput, _interactable);
        SetButton(_loginButton, State.CanSubmit); SetButton(_registerButton, State.CanSubmit); SetButton(_submitButton, State.CanSubmit);
        SetButton(_backButton, _interactable); SetButton(_showPasswordButton, _interactable); SetButton(_forgotPasswordButton, _interactable);
        SetButton(_signInTabButton, _interactable && register); SetButton(_createAccountTabButton, _interactable && !register);
        if (_rememberUsernameToggle != null) _rememberUsernameToggle.interactable = _interactable;
        if (_confirmPasswordGroup != null) _confirmPasswordGroup.SetActive(register);
        else if (_confirmPasswordInput != null) _confirmPasswordInput.gameObject.SetActive(register);
        if (_accountStep != null) _accountStep.SetActive(register);
        if (_forgotPasswordButton != null) _forgotPasswordButton.gameObject.SetActive(!register);
        SetLabel(_usernameErrorText, State.UsernameError);
        SetLabel(_passwordErrorText, State.PasswordError);
        SetLabel(_confirmPasswordErrorText, State.ConfirmPasswordError);
        SetLabel(_capsLockWarningText, _capsLock ? "Bloq Mayús está activado." : "");
        SetLabel(_rememberUsernameLabel, "Recordar nombre de usuario");
        SetLabel(_showPasswordText, _passwordVisible ? "Ocultar" : "Mostrar");
        SetLabel(_submitText, State.RetryAfterSeconds > 0 ? $"Espera {State.RetryAfterSeconds} s" : register ? "Registrarse" : "Iniciar sesión");
        SetLabel(_titleText, register ? "Crea tu cuenta" : "Inicia sesión para jugar");
        SetLabel(_leadText, register ? "Elige un nombre de usuario y una contraseña. Después crearás tu personaje." : "Tu personaje y tu alijo se guardan en tu cuenta.");
        SetLabel(_statusText, State.FormError.Length > 0 ? State.FormError : _status);
    }

    private static void SetInput(TMP_InputField input, bool enabled) { if (input != null) input.interactable = enabled; }
    private static void SetButton(Button button, bool enabled) { if (button != null) button.interactable = enabled; }
    private static void SetLabel(TextMeshProUGUI label, string text)
    {
        if (label == null) return;
        if (label.text != text) label.text = text;
        if (label.gameObject.activeSelf != (text.Length > 0)) label.gameObject.SetActive(text.Length > 0);
    }

    public void AddLoginListener(UnityAction action) => _loginRequested.AddListener(action);
    public void RemoveLoginListener(UnityAction action) => _loginRequested.RemoveListener(action);
    public void AddRegisterListener(UnityAction action) => _registerRequested.AddListener(action);
    public void RemoveRegisterListener(UnityAction action) => _registerRequested.RemoveListener(action);
    public void AddBackListener(UnityAction action) => _backRequested.AddListener(action);
    public void RemoveBackListener(UnityAction action) => _backRequested.RemoveListener(action);
    public void AddForgotPasswordListener(UnityAction action) => _forgotRequested.AddListener(action);
    public void RemoveForgotPasswordListener(UnityAction action) => _forgotRequested.RemoveListener(action);
}

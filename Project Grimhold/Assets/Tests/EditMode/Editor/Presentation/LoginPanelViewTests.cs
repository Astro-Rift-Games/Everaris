using System;
using System.Reflection;
using Grimhold.Backend;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LoginPanelViewTests
{
    private GameObject _owner;
    private LoginPanelView _view;
    private TMP_InputField _username, _password, _confirm;
    private Button _login, _register, _back, _forgot;
    private sealed class Store : IRememberedUsernameStore
    {
        public string Value = "Alice";
        public int Writes;
        public string Load() => Value;
        public void Save(string username) { Value = username; Writes++; }
        public void Clear() { Value = ""; Writes++; }
    }
    private Store _store;

    [SetUp]
    public void SetUp()
    {
        _owner = new GameObject("Auth view tests"); _owner.SetActive(false);
        _view = _owner.AddComponent<LoginPanelView>();
        _username = Input("_usernameInput"); _password = Input("_passwordInput");
        _confirm = Input("_confirmPasswordInput");
        _login = Button("_loginButton"); _register = Button("_registerButton");
        _back = Button("_backButton"); _forgot = Button("_forgotPasswordButton");
        _store = new Store();
    }

    [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(_owner);

    [Test]
    public void TabsAndEnterSubmitOnlySelectedMode_ValidateBeforeDispatch()
    {
        int login = 0, register = 0;
        _view.AddLoginListener(() => login++); _view.AddRegisterListener(() => register++);
        Call("Initialize", _store, (Func<double>)(() => 0));
        Call("Open");
        _password.text = "p";
        Call("HandleKeyboard", true, false);
        Assert.That(login, Is.EqualTo(1)); Assert.That(register, Is.Zero);
        Assert.That(_store.Writes, Is.EqualTo(1));
        Call("SetMode", LoginFormMode.CreateAccount);
        _username.text = "ab"; _password.text = "123456"; _confirm.text = "123456";
        Call("Submit"); Assert.That(register, Is.Zero);
        Assert.That(_store.Writes, Is.EqualTo(1));
        _username.text = "Alice"; _confirm.text = "wrong";
        Call("Submit"); Assert.That(register, Is.Zero);
        Assert.That(_store.Writes, Is.EqualTo(1));
        _confirm.text = "123456";
        Call("Submit"); Assert.That(register, Is.EqualTo(1));
        _view.SetInteractable(false);
        Call("Submit"); Assert.That(register, Is.EqualTo(1));
        Assert.That(_store.Writes, Is.EqualTo(2));
    }

    [Test]
    public void ShowHideBothSecrets_CapsWarningAndForgotAndBackRespectBusy()
    {
        Call("Initialize", _store, (Func<double>)(() => 0));
        Call("Open");
        Assert.That(_password.contentType, Is.EqualTo(TMP_InputField.ContentType.Password));
        Call("TogglePasswordVisibility");
        Assert.That(_password.contentType, Is.EqualTo(TMP_InputField.ContentType.Standard));
        Assert.That(_confirm.contentType, Is.EqualTo(TMP_InputField.ContentType.Standard));
        Call("TogglePasswordVisibility");
        Assert.That(_confirm.contentType, Is.EqualTo(TMP_InputField.ContentType.Password));
        var warningObject = new GameObject("Caps warning", typeof(RectTransform));
        warningObject.transform.SetParent(_owner.transform);
        var warning = warningObject.AddComponent<TextMeshProUGUI>();
        Field("_capsLockWarningText", warning);
        Call("SetCapsLock", true);
        Assert.That(warning.text, Is.EqualTo("Bloq Mayús está activado."));
        Assert.That(warning.gameObject.activeSelf, Is.True);
        Call("SetCapsLock", false);
        Assert.That(warning.gameObject.activeSelf, Is.False);
        int back = 0, forgot = 0;
        UnityEngine.Events.UnityAction backAction = () => back++;
        UnityEngine.Events.UnityAction forgotAction = () => forgot++;
        _view.AddBackListener(backAction);
        Call("AddForgotPasswordListener", forgotAction);
        Call("HandleKeyboard", false, true); Call("RequestPasswordRecovery");
        Assert.That(back, Is.EqualTo(1)); Assert.That(forgot, Is.EqualTo(1));
        _view.SetInteractable(false);
        Call("RequestBack"); Call("RequestPasswordRecovery");
        Assert.That(back, Is.EqualTo(1)); Assert.That(forgot, Is.EqualTo(1));
        _view.SetInteractable(true);
        _view.RemoveBackListener(backAction); Call("RemoveForgotPasswordListener", forgotAction);
        Call("RequestBack"); Call("RequestPasswordRecovery");
        Assert.That(back, Is.EqualTo(1)); Assert.That(forgot, Is.EqualTo(1));
    }

    [Test]
    public void OpenPrefillsOnlyUsername_ClearFieldsClearsSecrets_RateLimitBlocksButtons()
    {
        double now = 0;
        Call("Initialize", _store, (Func<double>)(() => now));
        _password.text = "never store"; _confirm.text = "never store";
        Call("Open");
        Assert.That(_view.Username, Is.EqualTo("Alice")); Assert.That(_view.Password, Is.Empty);
        Assert.That(_confirm.text, Is.Empty); Assert.That(_store.Writes, Is.Zero);
        Call("ApplyResult", LoginFlowResult.Failure(LoginFlowStatus.AuthFailed, "", new BackendError { error = "TOO_MANY_REQUESTS", retryAfter = 2 }));
        Assert.That(_login.interactable, Is.False);
        int submissions = 0;
        _view.AddLoginListener(() => submissions++);
        _password.text = "123456";
        Call("Submit");
        Assert.That(submissions, Is.Zero);
        Assert.That(_store.Writes, Is.Zero);
        _view.SetInteractable(true); Assert.That(_login.interactable, Is.False);
        now = 2; Call("Refresh"); Assert.That(_login.interactable, Is.True);
        _username.text = " Bob "; _password.text = "secret";
        Assert.That(_view.Username, Is.EqualTo("Bob")); Assert.That(_view.Password, Is.EqualTo("secret"));
        _view.ClearFields(); Assert.That(_view.Username, Is.Empty); Assert.That(_view.Password, Is.Empty);
    }

    [Test]
    public void CheckboxStoresOnlyUsername_AndUncheckingForgetsImmediately()
    {
        var toggleObject = new GameObject("Remember", typeof(RectTransform));
        toggleObject.transform.SetParent(_owner.transform);
        var toggle = toggleObject.AddComponent<Toggle>();
        Field("_rememberUsernameToggle", toggle);
        Call("Initialize", _store, (Func<double>)(() => 0));
        Enable();
        Assert.That(toggle.isOn, Is.True);
        _username.text = " Bob "; _password.text = "never persist me";
        toggle.isOn = false;
        Assert.That(_store.Value, Is.Empty);
        toggle.isOn = true;
        Assert.That(_store.Value, Is.EqualTo("Bob"));
        _username.onEndEdit.Invoke(" Cara ");
        Assert.That(_store.Value, Is.EqualTo("Cara"));
        Assert.That(_store.Writes, Is.EqualTo(3));
        Disable();
        Assert.That(_password.text, Is.Empty);
        Assert.That(_confirm.text, Is.Empty);
        Assert.That(_store.Value, Is.EqualTo("Cara"));
    }

    [Test]
    public void LegacyButtonsAndListenerRemovalRemainFunctional_AfterEnableCycle()
    {
        Field("_confirmPasswordInput", null);
        Call("Initialize", _store, (Func<double>)(() => 0));
        int login = 0, register = 0;
        UnityEngine.Events.UnityAction loginAction = () => login++;
        UnityEngine.Events.UnityAction registerAction = () => register++;
        _view.AddLoginListener(loginAction); _view.AddRegisterListener(registerAction);
        Enable();
        _password.text = "123456";
        _login.onClick.Invoke();
        Assert.That(login, Is.EqualTo(1));
        _register.onClick.Invoke();
        Assert.That(register, Is.EqualTo(1));
        Disable(); Enable();
        _password.text = "123456";
        _login.onClick.Invoke();
        Assert.That(login, Is.EqualTo(2));
        _view.RemoveLoginListener(loginAction); _view.RemoveRegisterListener(registerAction);
        _login.onClick.Invoke(); _register.onClick.Invoke();
        Assert.That(login, Is.EqualTo(2)); Assert.That(register, Is.EqualTo(1));
    }

    // Ordinary MonoBehaviour lifecycle messages do not run in EditMode. Invoke only the
    // lifecycle boundary explicitly, as CreditsPanelViewTests does; assertions use public UI APIs.
    private void Enable()
    {
        _owner.SetActive(true);
        typeof(LoginPanelView).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_view, null);
    }
    private void Disable()
    {
        typeof(LoginPanelView).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_view, null);
        _owner.SetActive(false);
    }

    private void Call(string name, params object[] args)
    {
        var method = typeof(LoginPanelView).GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
        Assert.That(method, Is.Not.Null, "Missing public API: " + name);
        method.Invoke(_view, args);
    }
    private void Field(string name, object value)
    {
        var field = typeof(LoginPanelView).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null, "Missing serialized reference: " + name);
        field.SetValue(_view, value);
    }
    private TMP_InputField Input(string field)
    {
        var go = new GameObject(field, typeof(RectTransform)); go.transform.SetParent(_owner.transform);
        var input = go.AddComponent<TMP_InputField>(); Field(field, input); return input;
    }
    private Button Button(string field)
    {
        var go = new GameObject(field, typeof(RectTransform)); go.transform.SetParent(_owner.transform);
        var button = go.AddComponent<Button>(); Field(field, button); return button;
    }
}

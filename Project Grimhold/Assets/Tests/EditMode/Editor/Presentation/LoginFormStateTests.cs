using System;
using System.Collections.Generic;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEngine;

public sealed class LoginFormStateTests
{
    private sealed class UsernameStore : IRememberedUsernameStore
    {
        public string Username { get; private set; } = "";
        public readonly List<string> Writes = new();
        public string Load() => Username;
        public void Save(string username) { Username = username; Writes.Add(username); }
        public void Clear() { Username = ""; Writes.Add(""); }
    }

    [TestCase(2, false)] [TestCase(3, true)] [TestCase(20, true)] [TestCase(21, false)]
    public void RegisterUsernameBoundaries(int length, bool valid)
    {
        var state = NewState();
        state.SetMode(LoginFormMode.CreateAccount);
        Assert.That(state.Validate(new string('a', length), "123456", "123456"), Is.EqualTo(valid));
        Assert.That(string.IsNullOrEmpty(state.UsernameError), Is.EqualTo(valid));
        Assert.That(state.PasswordError, Is.Empty);
        Assert.That(state.ConfirmPasswordError, Is.Empty);
    }

    [TestCase("abc_def")] [TestCase("ábc")] [TestCase("abc\n")]
    public void RegisterRejectsInvalidCharacters(string username)
    {
        var state = NewState();
        state.SetMode(LoginFormMode.CreateAccount);
        // A trailing newline is trimmed by the backend and is therefore valid.
        bool valid = username == "abc\n";
        Assert.That(state.Validate(username, "123456", "123456"), Is.EqualTo(valid));
        Assert.That(string.IsNullOrEmpty(state.UsernameError), Is.EqualTo(valid));
    }

    [TestCase(5, false)] [TestCase(6, true)]
    public void RegisterPasswordBoundaries(int length, bool valid)
    {
        var state = NewState(); state.SetMode(LoginFormMode.CreateAccount);
        string password = new string('p', length);
        Assert.That(state.Validate("  Ab 9  ", password, password), Is.EqualTo(valid));
        Assert.That(state.UsernameError, Is.Empty);
        Assert.That(string.IsNullOrEmpty(state.PasswordError), Is.EqualTo(valid));
        Assert.That(state.ConfirmPasswordError, Is.Empty);
    }

    [Test]
    public void ConfirmMustMatch_AndLoginOnlyRequiresNonEmptyFields()
    {
        var state = NewState(); state.SetMode(LoginFormMode.CreateAccount);
        Assert.That(state.Validate("abc", "123456", "123457"), Is.False);
        Assert.That(state.ConfirmPasswordError, Is.EqualTo("Las contraseñas no coinciden."));
        state.SetMode(LoginFormMode.SignIn);
        Assert.That(state.ConfirmPasswordError, Is.Empty);
        Assert.That(state.Validate("a_", "1", ""), Is.True);
        Assert.That(state.Validate("  ", "", ""), Is.False);
        Assert.That(state.UsernameError, Is.EqualTo("Introduce tu nombre de usuario."));
        Assert.That(state.PasswordError, Is.EqualTo("Introduce tu contraseña."));
    }

    [TestCase("USERNAME_TAKEN", "Este nombre de usuario ya está en uso.", "")]
    [TestCase("INVALID_CREDENTIALS", "", "Usuario o contraseña incorrectos.")]
    [TestCase("NETWORK_ERROR", "", "No se puede conectar con el servidor. Comprueba tu conexión.")]
    [TestCase("TIMEOUT", "", "No se puede conectar con el servidor. Comprueba tu conexión.")]
    [TestCase("INTERNAL_SERVER_ERROR", "", "No se pudo completar la solicitud. Inténtalo de nuevo.")]
    public void BackendErrorsHaveDistinctSpanishMessages(string code, string usernameError, string formError)
    {
        var state = NewState();
        state.ApplyResult(LoginFlowResult.Failure(LoginFlowStatus.AuthFailed, "raw diagnostic", new BackendError { error = code }));
        Assert.That(state.UsernameError, Is.EqualTo(usernameError));
        Assert.That(state.FormError, Is.EqualTo(formError));
        Assert.That(state.PasswordError, Is.Empty);
        Assert.That(state.ConfirmPasswordError, Is.Empty);
    }

    [TestCase("username")] [TestCase("password")] [TestCase("confirm")]
    public void ValidationDetailsMapToNamedFieldWithoutDisplayingRawMessages(string field)
    {
        var error = JsonUtility.FromJson<BackendError>("{\"error\":\"VALIDATION_FAILED\",\"details\":[{\"field\":\"" + field + "\",\"msg\":\"raw English\"}]}");
        var state = NewState();
        state.ApplyResult(LoginFlowResult.Failure(LoginFlowStatus.RegistrationFailed, "diagnostic", error));
        Assert.That(string.IsNullOrEmpty(state.UsernameError), Is.EqualTo(field != "username"));
        Assert.That(string.IsNullOrEmpty(state.PasswordError), Is.EqualTo(field != "password"));
        Assert.That(string.IsNullOrEmpty(state.ConfirmPasswordError), Is.EqualTo(field != "confirm"));
        Assert.That(state.FormError, Is.Empty);
    }

    [Test]
    public void RateLimitRoundsUp_DisablesUntilDeadline_AndBusyStillBlocks()
    {
        double now = 100;
        var state = new LoginFormState(new UsernameStore(), () => now);
        state.ApplyResult(LoginFlowResult.Failure(LoginFlowStatus.AuthFailed, "", new BackendError { error = "TOO_MANY_REQUESTS", retryAfter = 2.2f }));
        Assert.That(state.RetryAfterSeconds, Is.EqualTo(3));
        Assert.That(state.CanSubmit, Is.False);
        Assert.That(state.FormError, Is.EqualTo("Demasiados intentos. Vuelve a intentarlo en 3 s."));
        state.SetMode(LoginFormMode.CreateAccount);
        now = 102;
        Assert.That(state.RetryAfterSeconds, Is.EqualTo(1));
        Assert.That(state.CanSubmit, Is.False);
        state.SetBusy(true); now = 103;
        Assert.That(state.RetryAfterSeconds, Is.Zero);
        Assert.That(state.CanSubmit, Is.False);
        Assert.That(state.FormError, Is.Empty);
        state.SetBusy(false);
        Assert.That(state.CanSubmit, Is.True);
    }

    [Test]
    public void RememberPrefillsOnOpen_ForgettingClears_AndNeverStoresPassword()
    {
        var store = new UsernameStore();
        var state = new LoginFormState(store, () => 0);
        state.SetRememberUsername(true, "  Alice  ");
        Assert.That(store.Load(), Is.EqualTo("Alice"));
        Assert.That(state.Open(), Is.EqualTo("Alice"));
        Assert.That(state.RememberUsername, Is.True);
        state.Validate("Alice", "password-never-persist", "password-never-persist");
        CollectionAssert.AreEqual(new[] { "Alice" }, store.Writes);
        state.SetRememberUsername(false, "Alice");
        Assert.That(store.Load(), Is.Empty);
        Assert.That(new LoginFormState(store, () => 0).Open(), Is.Empty);
        Assert.That(state.RememberUsername, Is.False);
    }

    [Test]
    public void MissingDetailsAndLegacyResultRemainCompatible()
    {
        var error = JsonUtility.FromJson<BackendError>("{\"error\":\"INVALID_CREDENTIALS\",\"message\":\"original\",\"retryAfter\":12}");
        Assert.That(error.details, Is.Null);
        Assert.That(error.error, Is.EqualTo("INVALID_CREDENTIALS"));
        Assert.That(error.message, Is.EqualTo("original"));
        Assert.That(error.retryAfter, Is.EqualTo(12));
        var legacy = LoginFlowResult.Failure(LoginFlowStatus.AuthFailed, "original");
        Assert.That(legacy.Status, Is.EqualTo(LoginFlowStatus.AuthFailed));
        Assert.That(legacy.ErrorMessage, Is.EqualTo("original"));
        Assert.That(legacy.IsSuccess, Is.False);
        Assert.That(LoginFlowResult.Success().IsSuccess, Is.True);
    }

    private static LoginFormState NewState() => new(new UsernameStore(), () => 0);
}

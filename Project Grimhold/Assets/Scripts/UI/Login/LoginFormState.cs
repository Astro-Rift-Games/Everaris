using System;
using System.Text.RegularExpressions;
using Grimhold.Backend;

/// <summary>Deterministic auth form state; passwords are transient validation inputs only.</summary>
public sealed class LoginFormState
{
    private const string UsernameRules = "Usa entre 3 y 20 caracteres: letras, números y espacios.";
    private const string PasswordRules = "La contraseña debe tener al menos 6 caracteres.";
    private const string Mismatch = "Las contraseñas no coinciden.";
    private const string GenericFailure = "No se pudo completar la solicitud. Inténtalo de nuevo.";
    private static readonly Regex UsernamePattern = new(@"\A[a-zA-Z0-9 ]+\z");
    private readonly IRememberedUsernameStore _store;
    private readonly Func<double> _now;
    private double _retryDeadline;
    private bool _busy;
    private string _formError = "";

    public LoginFormState(IRememberedUsernameStore store, Func<double> now)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _now = now ?? throw new ArgumentNullException(nameof(now));
    }

    public LoginFormMode Mode { get; private set; }
    public bool RememberUsername { get; private set; }
    public string UsernameError { get; private set; } = "";
    public string PasswordError { get; private set; } = "";
    public string ConfirmPasswordError { get; private set; } = "";
    public int RetryAfterSeconds => (int)Math.Min(int.MaxValue, Math.Ceiling(Math.Max(0, _retryDeadline - _now())));
    public bool CanSubmit => !_busy && RetryAfterSeconds == 0;
    public string FormError => RetryAfterSeconds > 0
        ? $"Demasiados intentos. Vuelve a intentarlo en {RetryAfterSeconds} s." : _formError;

    public string Open()
    {
        string username = _store.Load() ?? "";
        RememberUsername = username.Length > 0;
        SetMode(LoginFormMode.SignIn);
        return username;
    }

    public void SetMode(LoginFormMode mode)
    {
        Mode = mode;
        ClearErrors(); // Does not reset the rate-limit deadline or the busy lock.
    }

    public void SetBusy(bool busy) => _busy = busy;

    public void SetRememberUsername(bool remember, string username)
    {
        RememberUsername = remember;
        if (remember) _store.Save((username ?? "").Trim());
        else _store.Clear();
    }

    public bool Validate(string username, string password, string confirm)
    {
        ClearErrors();
        username = (username ?? "").Trim();
        password ??= "";
        if (Mode == LoginFormMode.CreateAccount)
        {
            if (username.Length < 3 || username.Length > 20 || !UsernamePattern.IsMatch(username))
                UsernameError = UsernameRules;
            if (password.Length < 6) PasswordError = PasswordRules;
            if (confirm != password || password.Length == 0) ConfirmPasswordError = Mismatch;
        }
        else
        {
            if (username.Length == 0) UsernameError = "Introduce tu nombre de usuario.";
            if (password.Length == 0) PasswordError = "Introduce tu contraseña.";
        }
        return UsernameError.Length == 0 && PasswordError.Length == 0 && ConfirmPasswordError.Length == 0;
    }

    public void ApplyResult(LoginFlowResult result)
    {
        ClearErrors();
        if (result.IsSuccess || result.Status == LoginFlowStatus.NeedsCharacterCreation) return;
        BackendError error = result.BackendError;
        if (error.error == BackendErrorUtility.TooManyRequests)
        {
            float seconds = error.retryAfter;
            if (!float.IsNaN(seconds) && !float.IsInfinity(seconds) && seconds > 0)
                _retryDeadline = Math.Max(_retryDeadline, _now() + seconds);
            else _formError = "Demasiados intentos. Inténtalo de nuevo más tarde.";
        }
        else if (BackendErrorUtility.IsTransportFailure(error.error) || result.Status == LoginFlowStatus.NetworkError)
            _formError = "No se puede conectar con el servidor. Comprueba tu conexión.";
        else if (error.error == "USERNAME_TAKEN") UsernameError = "Este nombre de usuario ya está en uso.";
        else if (error.error == "INVALID_CREDENTIALS") _formError = "Usuario o contraseña incorrectos.";
        else if (error.error == "VALIDATION_FAILED")
        {
            bool mapped = false;
            if (error.details != null)
                foreach (var detail in error.details)
                {
                    // Never expose backend diagnostic text (or echo submitted credentials).
                    switch (detail.field)
                    {
                        case "username": UsernameError = Mode == LoginFormMode.SignIn ? "Introduce tu nombre de usuario." : UsernameRules; mapped = true; break;
                        case "password": PasswordError = Mode == LoginFormMode.SignIn ? "Introduce tu contraseña." : PasswordRules; mapped = true; break;
                        case "confirm": case "confirmPassword": ConfirmPasswordError = Mismatch; mapped = true; break;
                    }
                }
            if (!mapped) _formError = "Revisa el nombre de usuario y la contraseña.";
        }
        else _formError = GenericFailure;
    }

    private void ClearErrors()
    {
        UsernameError = PasswordError = ConfirmPasswordError = _formError = "";
    }
}

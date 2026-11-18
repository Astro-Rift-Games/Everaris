using System;
using System.Threading.Tasks;
using Grimhold.Backend;

public enum MenuPlayRoute { None, SignIn, Town }
public enum MenuConfirmation { None, Exit, SignOut }

/// <summary>Menu presentation policy. No credentials, persistence or network session ownership.</summary>
public sealed class MainMenuFlow
{
    private bool _authenticated;
    private string _name;
    private bool _busy;
    private bool _checking;
    private bool _unreachable;
    private bool _healthKnown;
    // Bumped when a backend-answered login proves reachability; an older /health result must not undo it.
    private int _reachabilityProof;
    public event Action Changed;
    public static string RecoveryMessage => "La recuperación de contraseña aún no está disponible. Las cuentas no tienen correo electrónico para verificar su titularidad.";
    public string AccountText => _authenticated ? $"Sesión iniciada: {_name}" : "Sin sesión iniciada";
    public string AccountActionText => _authenticated ? "Cerrar sesión" : "Iniciar sesión";
    public string ServerText => _checking || !_healthKnown ? "Comprobando servidor…" : _unreachable ? "Servidor no disponible" : "Servidor en línea";
    public string PlaySubtitle => _unreachable ? "Servidor no disponible" : _authenticated ? "Entrar al Town" : "Inicia sesión para jugar";
    public string Status { get; private set; } = "";
    public string CharacterStepText => "Paso 2 de 2 · Personaje";
    public bool PlayEnabled => !_busy && !_unreachable;
    public bool CanRetryHealth => !_busy && !_checking && _unreachable;
    public bool CanRetryTown { get; private set; }
    public bool CharacterStep { get; private set; }
    public MenuConfirmation PendingConfirmation { get; private set; }
    public void SetAccount(bool authenticated, string name)
    {
        _authenticated = authenticated; _name = name;
        if (!authenticated) CanRetryTown = false;
        Changed?.Invoke();
    }
    public void SetBusy(bool busy) { _busy = busy; Changed?.Invoke(); }
    public MenuPlayRoute RequestPlay() => !PlayEnabled ? MenuPlayRoute.None : _authenticated ? MenuPlayRoute.Town : MenuPlayRoute.SignIn;
    public void RequestConfirmation(MenuConfirmation action)
    {
        if (_busy) return;
        PendingConfirmation = action;
        Changed?.Invoke();
    }
    public MenuConfirmation ResolveConfirmation(bool accepted)
    {
        var action = accepted && !_busy ? PendingConfirmation : MenuConfirmation.None;
        PendingConfirmation = MenuConfirmation.None;
        Changed?.Invoke();
        return action;
    }
    public void TownFailed(string message) { Status = message; CanRetryTown = true; Changed?.Invoke(); }
    public void TownStarting() { CanRetryTown = false; Changed?.Invoke(); }
    public MenuPlayRoute RetryTown()
    {
        if (!CanRetryTown || !PlayEnabled) return MenuPlayRoute.None;
        CanRetryTown = false;
        Changed?.Invoke();
        return RequestPlay();
    }
    public void SessionInvalidated()
    {
        _authenticated = false;
        _name = null;
        CanRetryTown = false;
        PendingConfirmation = MenuConfirmation.None;
        Status = "Tu sesión ha terminado. Inicia sesión de nuevo.";
        Changed?.Invoke();
    }
    public async Task CheckHealthAsync(IBackendHealthClient health, BackendConfiguration config)
    {
        if (_checking) return;
        _checking = true;
        Changed?.Invoke();
        var proof = _reachabilityProof;
        bool reachable;
        try { reachable = await health.CheckAsync(config); }
        catch (Exception) { reachable = false; }
        if (reachable || proof == _reachabilityProof) _unreachable = !reachable;
        _healthKnown = true;
        _checking = false;
        Changed?.Invoke();
    }
    public bool RequestFinishLater() => CharacterStep && !_busy;
    public MenuPlayRoute ApplyLoginResult(LoginFlowResult result)
    {
        CharacterStep = result.Status == LoginFlowStatus.NeedsCharacterCreation;
        // A login the backend answered proves it is reachable; a stale /health failure must not block Town entry.
        if (result.IsSuccess || CharacterStep) { _unreachable = false; _healthKnown = true; _reachabilityProof++; }
        Changed?.Invoke();
        return result.IsSuccess ? MenuPlayRoute.Town : MenuPlayRoute.None;
    }
}

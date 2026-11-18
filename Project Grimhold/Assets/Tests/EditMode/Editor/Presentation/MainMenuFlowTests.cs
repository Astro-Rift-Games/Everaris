using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;

public sealed class MainMenuFlowTests
{
    [TestCase(LoginFlowStatus.Success)]
    [TestCase(LoginFlowStatus.NeedsCharacterCreation)]
    public void BackendLoginResponse_ClearsStaleUnreachableHealth(LoginFlowStatus status)
    {
        var menu = new MainMenuFlow();
        menu.CheckHealthAsync(new UnreachableHealth(), null).GetAwaiter().GetResult();
        Assert.That(menu.PlayEnabled, Is.False, "precondition: cached health says unreachable");
        var result = status == LoginFlowStatus.Success
            ? LoginFlowResult.Success()
            : LoginFlowResult.Failure(LoginFlowStatus.NeedsCharacterCreation, "");
        menu.ApplyLoginResult(result);
        menu.SetAccount(status == LoginFlowStatus.Success, "Maren");
        Assert.That(menu.PlayEnabled, Is.True);
        Assert.That(menu.ServerText, Is.EqualTo("Servidor en línea"));
        Assert.That(menu.PlaySubtitle, Is.Not.EqualTo("Servidor no disponible"));
        if (status == LoginFlowStatus.Success) Assert.That(menu.RequestPlay(), Is.EqualTo(MenuPlayRoute.Town));
    }

    [Test]
    public void TransportFailedLogin_KeepsUnreachableHealth()
    {
        var menu = new MainMenuFlow();
        menu.CheckHealthAsync(new UnreachableHealth(), null).GetAwaiter().GetResult();
        menu.ApplyLoginResult(LoginFlowResult.Failure(LoginFlowStatus.AuthFailed, ""));
        Assert.That(menu.PlayEnabled, Is.False);
    }

    private sealed class UnreachableHealth : IBackendHealthClient
    {
        public Task<bool> CheckAsync(BackendConfiguration config) => Task.FromResult(false);
    }

    [TestCase(false, MenuPlayRoute.SignIn)]
    [TestCase(true, MenuPlayRoute.Town)]
    public void Play_RoutesBySession(bool authenticated, MenuPlayRoute expected)
    {
        var menu = new MainMenuFlow();
        menu.SetAccount(authenticated, "Maren");
        Assert.That(menu.RequestPlay(), Is.EqualTo(expected));
        Assert.That(menu.AccountText, Is.EqualTo(authenticated ? "Sesión iniciada: Maren" : "Sin sesión iniciada"));
    }

    [TestCase(MenuConfirmation.Exit)]
    [TestCase(MenuConfirmation.SignOut)]
    public void Confirmation_RequiresAcceptanceAndIsConsumedOnce(MenuConfirmation action)
    {
        var menu = new MainMenuFlow();
        menu.RequestConfirmation(action);
        Assert.That(menu.PendingConfirmation, Is.EqualTo(action));
        Assert.That(menu.ResolveConfirmation(false), Is.EqualTo(MenuConfirmation.None));
        Assert.That(menu.PendingConfirmation, Is.EqualTo(MenuConfirmation.None));
        menu.RequestConfirmation(action);
        Assert.That(menu.ResolveConfirmation(true), Is.EqualTo(action));
        Assert.That(menu.ResolveConfirmation(true), Is.EqualTo(MenuConfirmation.None));
    }

    [Test]
    public void Busy_BlocksPlayConfirmationAndCharacterSignOut()
    {
        var menu = new MainMenuFlow();
        menu.SetBusy(true);
        menu.RequestConfirmation(MenuConfirmation.Exit);
        Assert.That(menu.RequestPlay(), Is.EqualTo(MenuPlayRoute.None));
        Assert.That(menu.PendingConfirmation, Is.EqualTo(MenuConfirmation.None));
        Assert.That(menu.RequestFinishLater(), Is.False);
    }

    [Test]
    public void TownFailure_OffersRetryWhichRoutesToTown()
    {
        var menu = new MainMenuFlow();
        menu.SetAccount(true, "Maren");
        menu.TownFailed("No se pudo entrar al Town.");
        Assert.That(menu.Status, Is.EqualTo("No se pudo entrar al Town."));
        Assert.That(menu.CanRetryTown, Is.True);
        Assert.That(menu.RetryTown(), Is.EqualTo(MenuPlayRoute.Town));
        Assert.That(menu.CanRetryTown, Is.False);
    }

    [Test]
    public void SessionInvalidated_ClearsAccountAndShowsNotice()
    {
        var menu = new MainMenuFlow();
        menu.SetAccount(true, "Maren");
        menu.SessionInvalidated();
        Assert.That(menu.AccountText, Is.EqualTo("Sin sesión iniciada"));
        Assert.That(menu.AccountActionText, Is.EqualTo("Iniciar sesión"));
        Assert.That(menu.Status, Is.EqualTo("Tu sesión ha terminado. Inicia sesión de nuevo."));
        Assert.That(menu.RequestPlay(), Is.EqualTo(MenuPlayRoute.SignIn));
    }

    [Test]
    public async Task Health_CheckingOfflineRetryOnline_UpdatesPlayAndSubtitle()
    {
        var health = new HealthFake();
        var menu = new MainMenuFlow();
        var check = menu.CheckHealthAsync(health, null);
        Assert.That(menu.ServerText, Is.EqualTo("Comprobando servidor…"));
        Assert.That(menu.CanRetryHealth, Is.False);
        health.Response.SetResult(false);
        await check;
        Assert.That(menu.ServerText, Is.EqualTo("Servidor no disponible"));
        Assert.That(menu.PlayEnabled, Is.False);
        Assert.That(menu.PlaySubtitle, Is.EqualTo("Servidor no disponible"));
        Assert.That(menu.RequestPlay(), Is.EqualTo(MenuPlayRoute.None));
        Assert.That(menu.CanRetryHealth, Is.True);
        health.Response = new TaskCompletionSource<bool>();
        check = menu.CheckHealthAsync(health, null);
        health.Response.SetResult(true);
        await check;
        Assert.That(menu.ServerText, Is.EqualTo("Servidor en línea"));
        Assert.That(menu.PlayEnabled, Is.True);
        Assert.That(menu.PlaySubtitle, Is.EqualTo("Inicia sesión para jugar"));
        Assert.That(health.Calls, Is.EqualTo(2));
    }

    [Test]
    public void ForgotPassword_ExplainsNoEmailRecovery()
    {
        Assert.That(MainMenuFlow.RecoveryMessage, Is.EqualTo("La recuperación de contraseña aún no está disponible. Las cuentas no tienen correo electrónico para verificar su titularidad."));
    }

    [Test]
    public void CharacterStep_AllowsFinishLater_AndDoesNotEnterTownUntilSuccess()
    {
        var menu = new MainMenuFlow();
        Assert.That(menu.ApplyLoginResult(LoginFlowResult.Failure(LoginFlowStatus.NeedsCharacterCreation, null)), Is.EqualTo(MenuPlayRoute.None));
        Assert.That(menu.CharacterStep, Is.True);
        Assert.That(menu.CharacterStepText, Is.EqualTo("Paso 2 de 2 · Personaje"));
        Assert.That(menu.RequestFinishLater(), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SuccessfulLoginOrCharacterCreation_AutomaticallyEntersTown(bool characterStep)
    {
        var menu = new MainMenuFlow();
        if (characterStep) menu.ApplyLoginResult(LoginFlowResult.Failure(LoginFlowStatus.NeedsCharacterCreation, null));
        Assert.That(menu.ApplyLoginResult(LoginFlowResult.Success()), Is.EqualTo(MenuPlayRoute.Town));
        Assert.That(menu.CharacterStep, Is.False);
    }

    private sealed class HealthFake : IBackendHealthClient
    {
        public TaskCompletionSource<bool> Response = new();
        public int Calls;
        public Task<bool> CheckAsync(BackendConfiguration config) { Calls++; return Response.Task; }
    }
}

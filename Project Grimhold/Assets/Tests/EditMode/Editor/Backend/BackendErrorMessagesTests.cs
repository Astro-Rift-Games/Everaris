#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using Grimhold.Backend;
using NUnit.Framework;

public sealed class BackendErrorMessagesTests
{
    [TestCase("TOO_MANY_REQUESTS", "Demasiadas solicitudes. Espera un momento antes de volver a intentarlo.")]
    [TestCase("CONCURRENT_MUTATION", "El servidor está ocupado. Vuelve a intentarlo en un momento.")]
    [TestCase("EXTRACTION_RESULT_PENDING", "El resultado de la incursión aún se está procesando.")]
    [TestCase("NO_AUTHORITATIVE_RESULT", "El resultado de la incursión aún se está procesando.")]
    [TestCase("RESULT_CONFLICT", "El estado ha cambiado. Actualiza tus datos antes de continuar.")]
    [TestCase("RESERVATION_PENDING", "Ya tienes una reserva de incursión pendiente.")]
    [TestCase("PREPARED_WEAPON_REQUIRED", "Equipa un arma o pide un arma de entrenamiento al Instructor.")]
    [TestCase("LOADOUT_NOT_EMPTY", "Tu inventario debe estar vacío para recibir el botín.")]
    [TestCase("RESULT_ALREADY_CONSUMED", "El resultado de esta incursión ya fue guardado.")]
    [TestCase("INVALID_HOST_SIGNATURE", "No se pudo completar la operación. Vuelve a iniciar sesión o contacta con soporte.")]
    [TestCase("FUTURE_CODE", "No se pudo completar la operación. Vuelve a iniciar sesión o contacta con soporte.")]
    [TestCase("TIMEOUT", "No se pudo conectar con el servidor. Comprueba tu conexión.")]
    public void Error_HasSpanishGuidanceWithoutRawDiagnostics(string code, string expected)
    {
        Assert.That(BackendErrorMessages.ForError(new BackendError { error = code, message = "raw diagnostic" }), Is.EqualTo(expected));
    }

    [TestCase("NO_AUTHORITATIVE_RESULT")]
    [TestCase("TOO_MANY_REQUESTS")]
    [TestCase("CONCURRENT_MUTATION")]
    [TestCase("EXTRACTION_RESULT_PENDING")]
    [TestCase("TIMEOUT")]
    public void DefeatExhaustion_DoesNotPromiseLoginReplay(string code)
    {
        var holder = new UnityEngine.GameObject("DefeatFeedbackTests");
        holder.SetActive(false);
        try
        {
            var feedback = holder.AddComponent<NetworkRaidParticipant>().BackendDefeatFeedback;
            feedback.Begin();
            feedback.Retrying(new BackendError { error = code });
            Assert.That(BackendErrorMessages.ForCommit(feedback), Does.StartWith("Guardado pendiente; reintentando…"));
            feedback.Complete(false, new BackendError { error = code, message = "raw diagnostic" });
            Assert.That(feedback.Status, Is.EqualTo(BackendCommitStatus.Failed));
            Assert.That(BackendErrorMessages.ForCommit(feedback), Does.StartWith("No se pudo guardar."));
            Assert.That(BackendErrorMessages.ForCommit(feedback), Does.Not.Contain("se reintentará"));
            Assert.That(BackendErrorMessages.ForCommit(feedback), Does.Not.Contain("raw diagnostic"));
            Assert.That(BackendErrorMessages.ForCommit(feedback), Does.Not.Contain(code));
        }
        finally { UnityEngine.Object.DestroyImmediate(holder); }
    }

    [Test]
    public void RateLimit_IncludesRoundedUpWaitHint()
    {
        Assert.That(BackendErrorMessages.ForError(new BackendError { error = "TOO_MANY_REQUESTS", retryAfter = 2.5f }),
            Is.EqualTo("Demasiadas solicitudes. Espera 3 segundos antes de volver a intentarlo."));
    }

    [TestCase("NO_AUTHORITATIVE_RESULT", BackendCommitStatus.DeferredUntilLogin, "No se pudo guardar; se reintentará al iniciar sesión.")]
    [TestCase("TIMEOUT", BackendCommitStatus.DeferredUntilLogin, "No se pudo guardar; se reintentará al iniciar sesión.")]
    [TestCase("RESULT_CONFLICT", BackendCommitStatus.Failed, "No se pudo guardar. El estado ha cambiado. Actualiza tus datos antes de continuar.")]
    [TestCase("RESERVATION_PENDING", BackendCommitStatus.Failed, "No se pudo guardar. Ya tienes una reserva de incursión pendiente.")]
    public void Commit_ExposesRetryingThenCorrectFinalDisposition(string code, BackendCommitStatus expected, string text)
    {
        var feedback = new BackendCommitFeedback();
        Assert.That(BackendErrorMessages.ForCommit(feedback), Is.Empty);
        feedback.Begin();
        Assert.That(feedback.Status, Is.EqualTo(BackendCommitStatus.Saving));
        Assert.That(BackendErrorMessages.ForCommit(feedback), Is.EqualTo("Guardando el resultado de la incursión…"));
        var error = new BackendError { error = code };
        feedback.Retrying(error);
        Assert.That(feedback.Status, Is.EqualTo(BackendCommitStatus.Retrying));
        Assert.That(BackendErrorMessages.ForCommit(feedback), Does.StartWith("Guardado pendiente; reintentando…"));
        feedback.Complete(false, error);
        Assert.That(feedback.Status, Is.EqualTo(expected));
        Assert.That(BackendErrorMessages.ForCommit(feedback), Is.EqualTo(text));
        feedback.Begin();
        feedback.Complete(true, default);
        Assert.That(feedback.Status, Is.EqualTo(BackendCommitStatus.Saved));
        Assert.That(BackendErrorMessages.ForCommit(feedback), Is.EqualTo("Resultado guardado."));
        Assert.That(feedback.Error.error, Is.Null.Or.Empty);
        feedback.DeferHydration();
        Assert.That(BackendErrorMessages.ForCommit(feedback), Is.EqualTo("No se pudo guardar; se reintentará al iniciar sesión."));
    }
}
#endif

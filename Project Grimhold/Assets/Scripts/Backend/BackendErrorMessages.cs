namespace Grimhold.Backend
{
    /// <summary>Spanish player-facing text; backend diagnostics never enter the UI.</summary>
    public static class BackendErrorMessages
    {
        public static string ForError(BackendError error)
        {
            if (BackendErrorUtility.IsTransportFailure(error.error))
                return "No se pudo conectar con el servidor. Comprueba tu conexión.";
            switch (error.error)
            {
                case "TOO_MANY_REQUESTS":
                    return error.retryAfter > 0 && !float.IsInfinity(error.retryAfter)
                        ? error.retryAfter >= 60
                            ? $"Demasiadas solicitudes. Espera {System.Math.Ceiling(error.retryAfter / 60d):0} {(System.Math.Ceiling(error.retryAfter / 60d) == 1 ? "minuto" : "minutos")} antes de volver a intentarlo."
                            : $"Demasiadas solicitudes. Espera {System.Math.Ceiling(error.retryAfter):0} segundos antes de volver a intentarlo."
                        : "Demasiadas solicitudes. Espera un momento antes de volver a intentarlo.";
                case "EXTRACTION_RESULT_PENDING":
                case "NO_AUTHORITATIVE_RESULT":
                    return "El resultado de la incursión aún se está procesando.";
                case "RESULT_ALREADY_CONSUMED":
                    return "El resultado de esta incursión ya fue guardado.";
                case "RESERVATION_PENDING":
                    return "Ya tienes una reserva de incursión pendiente.";
                case "PREPARED_WEAPON_REQUIRED":
                    return "Equipa un arma o pide un arma de entrenamiento al Instructor.";
                case "LOADOUT_NOT_EMPTY":
                    return "Tu inventario debe estar vacío para recibir el botín.";
            }
            return BackendErrorClassifier.Classify(error.error) switch
            {
                BackendErrorClass.Retryable => "El servidor está ocupado. Vuelve a intentarlo en un momento.",
                BackendErrorClass.Conflict => "El estado ha cambiado. Actualiza tus datos antes de continuar.",
                _ => "No se pudo completar la operación. Vuelve a iniciar sesión o contacta con soporte."
            };
        }

        public static string ForCommit(BackendCommitFeedback feedback) => feedback?.Message ?? string.Empty;

        internal static string FormatCommit(BackendCommitFeedback feedback) => feedback.Status switch
        {
            BackendCommitStatus.Saving => "Guardando el resultado de la incursión…",
            BackendCommitStatus.Retrying => "Guardado pendiente; reintentando… " + ForError(feedback.Error),
            BackendCommitStatus.DeferredUntilLogin => "No se pudo guardar; se reintentará al iniciar sesión.",
            BackendCommitStatus.Failed when feedback.Error.error == "LOCAL_DEFEAT_PENDING" =>
                "No se pudo registrar esta derrota localmente porque hay otra derrota pendiente de guardar.",
            BackendCommitStatus.Failed when feedback.Error.error == "NO_AUTHORITATIVE_RESULT" =>
                "No se pudo guardar la experiencia de la derrota; el servidor ya no puede acreditar este resultado.",
            BackendCommitStatus.Failed => "No se pudo guardar. " + ForError(feedback.Error),
            BackendCommitStatus.Saved => "Resultado guardado.",
            _ => string.Empty
        };
    }
}

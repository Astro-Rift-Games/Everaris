/// <summary>
/// Player-facing Spanish text for every failed <see cref="SessionTransitionResult"/>, shared by the
/// Town preparation panel and the raid menu. Messages never expose internal identifiers, and a
/// multiplayer service failure is worded as a service problem (GD 01 §13).
/// </summary>
public static class SessionTransitionFailureText
{
    public const string GenericMessage =
        "No se pudo completar la transición. Volvé a intentar en unos segundos.";

    public static bool TryGetMessage(SessionTransitionResult result, out string message)
    {
        message = result == SessionTransitionResult.Succeeded ? null : Describe(result);
        return message != null;
    }

    private static string Describe(SessionTransitionResult result) => result switch
    {
        SessionTransitionResult.Busy =>
            "Ya hay una transición en curso. Esperá a que termine y volvé a intentar.",
        SessionTransitionResult.InvalidRequest =>
            "Esta acción no está disponible en este momento.",
        SessionTransitionResult.InvalidState =>
            "No se puede realizar esta acción en el estado actual. Volvé a intentar en unos segundos.",
        SessionTransitionResult.ShutdownFailed =>
            "No se pudo cerrar la sesión actual. Volvé a intentar.",
        SessionTransitionResult.ConnectionFailed =>
            "No se pudo completar la conexión. Volvé a intentar.",
        SessionTransitionResult.ServiceUnavailable =>
            "El servicio multijugador no está disponible en este momento. Probá de nuevo más tarde.",
        SessionTransitionResult.RecoveryFailed =>
            "No se pudo volver al Pueblo. Volvé al menú principal e ingresá de nuevo.",
        SessionTransitionResult.LoadoutReservationFailed =>
            "No se pudo reservar el equipamiento de la expedición. Volvé a intentar.",
        SessionTransitionResult.LoadoutRollbackFailed =>
            "No se pudo liberar el equipamiento reservado. Se reintentará automáticamente.",
        SessionTransitionResult.LoadoutConfirmationFailed =>
            "No se pudo confirmar el equipamiento de la expedición. Volvé a intentar.",
        _ => GenericMessage
    };
}

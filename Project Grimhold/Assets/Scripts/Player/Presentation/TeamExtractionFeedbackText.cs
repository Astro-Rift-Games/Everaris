/// <summary>
/// Pure Spanish HUD messages for team extraction blocking and countdown cancellation reasons (FIX-15).
/// </summary>
public static class TeamExtractionFeedbackText
{
    public static string FormatBlockedMessage(
        TeamExtractionCancelReason reason,
        string memberName,
        int presentCount,
        int requiredCount)
    {
        bool hasName = !string.IsNullOrWhiteSpace(memberName);
        switch (reason)
        {
            case TeamExtractionCancelReason.RequiredMemberOutsideArea:
                string waitingTarget = hasName ? memberName.Trim() : "tu compañero";
                return requiredCount > 1
                    ? $"Esperando a {waitingTarget} en el área ({presentCount}/{requiredCount})"
                    : $"Esperando a {waitingTarget} en el área";
            case TeamExtractionCancelReason.RequiredMemberDisconnected:
                string disconnectedTarget = hasName ? memberName.Trim() : "un compañero";
                return $"Extracción bloqueada: {disconnectedTarget} está desconectado";
            default:
                return string.Empty;
        }
    }

    public static string FormatCancelledMessage(
        TeamExtractionCancelReason reason,
        string memberName)
    {
        bool hasName = !string.IsNullOrWhiteSpace(memberName);
        switch (reason)
        {
            case TeamExtractionCancelReason.RequiredMemberOutsideArea:
                return hasName
                    ? $"Extracción cancelada: {memberName.Trim()} salió del área"
                    : "Extracción cancelada: un integrante salió del área";
            case TeamExtractionCancelReason.RequiredMemberDisconnected:
                return hasName
                    ? $"Extracción cancelada: {memberName.Trim()} se desconectó"
                    : "Extracción cancelada: un integrante se desconectó";
            case TeamExtractionCancelReason.CompositionChanged:
                return "Extracción cancelada: cambió la composición del equipo";
            case TeamExtractionCancelReason.RitualNotCompleted:
                return "Extracción cancelada: el ritual ya no está activo";
            default:
                return "Extracción: cancelada";
        }
    }
}

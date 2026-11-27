/// <summary>
/// Pure Spanish player-facing messages for Town Raid preparation dissolution and launch cancellations.
/// </summary>
public static class TownRaidCancellationText
{
    public static bool TryGetMessage(TownRaidPreparationCancelReason reason, out string message)
    {
        message = reason switch
        {
            TownRaidPreparationCancelReason.HostLeft =>
                "El Host abandonó la preparación. La sala fue disuelta.",
            TownRaidPreparationCancelReason.HostDisconnected =>
                "El Host se desconectó. La preparación fue disuelta.",
            TownRaidPreparationCancelReason.AcknowledgeTimeout =>
                "Se canceló el lanzamiento: expiró el tiempo de confirmación de los integrantes.",
            TownRaidPreparationCancelReason.ReleaseTimeout =>
                "Se canceló el lanzamiento: se agotó el tiempo de salida hacia la expedición.",
            TownRaidPreparationCancelReason.MemberDisconnected =>
                "Se canceló el lanzamiento: un integrante se desconectó.",
            TownRaidPreparationCancelReason.MemberRejectedLaunch =>
                "Se canceló el lanzamiento: un integrante no pudo confirmar su preparación.",
            TownRaidPreparationCancelReason.ReleaseFailed =>
                "Se canceló el lanzamiento: no se pudo completar la coordinación de salida.",
            _ => null
        };
        return message != null;
    }
}

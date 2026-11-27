/// <summary>
/// Pure Spanish player-facing feedback for Town Party invitation and leave outcomes (FIX-16).
/// </summary>
public static class TownPartyFeedbackText
{
    public static string GetInvitationResultMessage(TownPartyInvitationResult result) => result switch
    {
        TownPartyInvitationResult.Accepted => "Invitación aceptada",
        TownPartyInvitationResult.Rejected => "Invitación rechazada",
        TownPartyInvitationResult.Expired => "La invitación expiró",
        TownPartyInvitationResult.AlreadyGrouped => "El jugador ya está en un grupo",
        TownPartyInvitationResult.PartyFull => "El grupo está completo",
        TownPartyInvitationResult.InviterAlreadyHasPending => "Ya tenés una invitación pendiente",
        TownPartyInvitationResult.RecipientHasPending => "Ese jugador ya tiene una invitación pendiente",
        TownPartyInvitationResult.CancelledByPartyChange => "La invitación se canceló porque el grupo cambió",
        TownPartyInvitationResult.Busy => "El grupo está ocupado",
        TownPartyInvitationResult.Cooldown => "Esperá antes de volver a invitar",
        _ => "Invitación no disponible"
    };

    public static bool TryGetLeaveFailureMessage(TownPartyLeaveResult result, out string message)
    {
        message = result switch
        {
            TownPartyLeaveResult.NotInDuo => "No estás en un grupo para abandonar",
            TownPartyLeaveResult.Unavailable => "No se pudo abandonar el grupo en este momento",
            _ => null
        };
        return message != null;
    }
}

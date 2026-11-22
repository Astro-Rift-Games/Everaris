/// <summary>Spanish player text of the Town attribute respec (TASK-304).</summary>
public static class TownRespecMessages
{
    public const string BuildLockedMessage = "Cancela Ready para reasignar atributos";
    public const string NoAssignedPointsMessage = "No hay puntos asignados para reasignar.";
    public const string CostUnavailableMessage = "No se pudo obtener el costo de la reasignación.";
    public const string ReservationPendingMessage = "No se puede reasignar mientras hay una reserva de Raid pendiente.";
    public const string BusyMessage = "Espera a que termine la operación en curso.";
    public const string UnavailableMessage = "El servicio de reasignación no está disponible.";
    public const string PendingMessage = "Reasignando atributos…";
    public const string AppliedMessage = "Atributos reasignados";
    public const string UnconfirmedMessage = "No se pudo confirmar la reasignación; el perfil se sincronizó";
    public const string UnsyncedMessage = "No se pudo confirmar la reasignación ni sincronizar el perfil. Vuelve a intentarlo";
    public const string DivergedMessage = "La reasignación se confirmó; el perfil se sincronizó con el servidor";
    public const string CostPlaceholder = "Costo: —";

    public static string CostText(int? cost) => cost.HasValue ? $"Costo: {cost.Value} de Oro" : CostPlaceholder;

    public static string InsufficientGoldMessage(int cost) =>
        $"No tenés Oro suficiente para reasignar (cuesta {cost} de Oro).";

    /// <summary>The explicit confirmation of Doc 04 §11: the reset, the points returned and the unequip.</summary>
    public static string ConfirmationText(int cost) =>
        $"Reasignar atributos cuesta {cost} de Oro. Todos tus atributos volverán a 0 y los puntos asignados " +
        "regresarán al pool disponible. Se desequiparán las armas y habilidades que dejen de cumplir sus requisitos.";

    public static string BlockMessage(TownRespecBlockReason reason, int? cost) => reason switch
    {
        TownRespecBlockReason.Unavailable => UnavailableMessage,
        TownRespecBlockReason.Busy => BusyMessage,
        TownRespecBlockReason.BuildLocked => BuildLockedMessage,
        TownRespecBlockReason.ReservationPending => ReservationPendingMessage,
        TownRespecBlockReason.CostUnavailable => CostUnavailableMessage,
        TownRespecBlockReason.NoAssignedPoints => NoAssignedPointsMessage,
        TownRespecBlockReason.InsufficientGold => cost.HasValue ? InsufficientGoldMessage(cost.Value) : CostUnavailableMessage,
        _ => null
    };

    /// <summary>Maps every backend respec code; a code that may follow a committed respec reads as unconfirmed.</summary>
    public static string FailureMessage(string code) => code switch
    {
        "NO_ASSIGNED_POINTS" => NoAssignedPointsMessage,
        "INSUFFICIENT_FUNDS" => "No tenés Oro suficiente para reasignar.",
        "RESERVATION_PENDING" => ReservationPendingMessage,
        "REVISION_CONFLICT" => "El perfil cambió. Vuelve a intentarlo.",
        "INVENTORY_FULL" => "No hay lugar en el Inventario para desequipar las armas que dejan de cumplir sus requisitos.",
        "INVENTORY_MIGRATION_REQUIRED" => "El inventario debe sincronizarse antes de reasignar.",
        null or "" or "REMOTE_RESULT_UNKNOWN" or "TRANSACTION_ID_REUSED" or "TRANSACTION_RETENTION_EXCEEDED" or
            "TIMEOUT" or "NETWORK_ERROR" or "REQUEST_CANCELLED" or "INVALID_INVENTORY" or "INTERNAL_SERVER_ERROR" or
            "SERVICE_UNAVAILABLE" or "RECONCILIATION_FAILED" => UnconfirmedMessage,
        _ => "El servidor rechazó la reasignación."
    };

    /// <summary>
    /// The status decides first (a sync is claimed only when the reread succeeded); the code refines a rejection or an
    /// unconfirmed attempt, where a transport failure or an unknown outcome reads as unconfirmed.
    /// </summary>
    public static string OutcomeMessage(in AttributeRespecOutcome outcome) => outcome.Status switch
    {
        AttributeRespecStatus.Applied or AttributeRespecStatus.AlreadyApplied => AppliedMessage,
        AttributeRespecStatus.Diverged => DivergedMessage,
        AttributeRespecStatus.RecoveryFailed => UnsyncedMessage,
        AttributeRespecStatus.Unavailable => UnavailableMessage,
        _ => FailureMessage(outcome.ErrorCode)
    };
}

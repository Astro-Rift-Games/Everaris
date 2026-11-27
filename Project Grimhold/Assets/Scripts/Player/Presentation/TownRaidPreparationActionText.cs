using System.Collections.Generic;

/// <summary>
/// Pure Spanish player-facing feedback for rejected Town Raid preparation actions and disabled Start hints.
/// </summary>
public static class TownRaidPreparationActionText
{
    public static bool TryGetMessage(TownRaidPreparationActionResult result, out string message)
    {
        message = result switch
        {
            TownRaidPreparationActionResult.DirectoryUnavailable =>
                "El servicio de preparación no está disponible todavía. Esperá un momento y volvé a intentar.",
            TownRaidPreparationActionResult.AlreadyInPreparation =>
                "Ya estás en una preparación de expedición.",
            TownRaidPreparationActionResult.AwaitingPartyCompletion =>
                TownRaidPreparationView.AwaitingPartyCompletionMessage,
            TownRaidPreparationActionResult.PartyUnavailable =>
                "Tu grupo todavía no se resolvió en Town. Esperá unos segundos y volvé a intentar.",
            TownRaidPreparationActionResult.InvalidCode =>
                "El código debe tener exactamente 6 números.",
            TownRaidPreparationActionResult.CodeNotFound =>
                "No existe ninguna preparación con ese código.",
            TownRaidPreparationActionResult.PreparationFull =>
                "La preparación está llena.",
            TownRaidPreparationActionResult.PreparationLaunching =>
                "La preparación ya está en lanzamiento y no admite cambios.",
            TownRaidPreparationActionResult.NotInPreparation =>
                "No formás parte de una preparación activa.",
            TownRaidPreparationActionResult.NotHost =>
                "Solo el Host puede iniciar la expedición.",
            TownRaidPreparationActionResult.NotAllMembersReady =>
                "Todos los integrantes deben marcar Listo antes de iniciar.",
            TownRaidPreparationActionResult.Failed =>
                "No se pudo completar la acción. Volvé a intentar.",
            _ => null
        };
        return message != null;
    }

    /// <summary>
    /// Explains why Start is disabled for the Host while Waiting, naming the unready members.
    /// Returns empty when Start is available, the viewer is not Host, or the preparation is Launching.
    /// </summary>
    public static string FormatUnreadyMembersHint(in TownRaidPreparationPresentation presentation)
    {
        if (!presentation.IsHost ||
            presentation.Snapshot.State != TownRaidPreparationState.Waiting ||
            presentation.CanStart ||
            presentation.Members == null ||
            presentation.Members.Count == 0)
        {
            return string.Empty;
        }

        var unreadyNames = new List<string>(presentation.Members.Count);
        for (int index = 0; index < presentation.Members.Count; index++)
        {
            TownRaidPreparationPresentation.Member member = presentation.Members[index];
            if (!member.IsReady)
            {
                string name = string.IsNullOrWhiteSpace(member.DisplayName)
                    ? "Jugador"
                    : member.DisplayName;
                unreadyNames.Add(name);
            }
        }

        if (unreadyNames.Count == 0)
        {
            return string.Empty;
        }

        return unreadyNames.Count == 1
            ? $"Falta marcar Listo: {unreadyNames[0]}."
            : $"Faltan marcar Listo: {string.Join(", ", unreadyNames)}.";
    }

    /// <summary>
    /// Formats live confirmation progress while the preparation is in <see cref="TownRaidPreparationState.Launching"/>.
    /// </summary>
    public static string FormatLaunchStatus(in TownRaidPreparationPresentation presentation)
    {
        if (!presentation.IsLaunching || presentation.Members == null || presentation.Members.Count == 0)
        {
            return string.Empty;
        }

        int total = presentation.Members.Count;
        int acknowledged = presentation.AcknowledgedCount;
        if (acknowledged >= total)
        {
            return $"Lanzando expedición... Todos confirmados ({acknowledged}/{total}). Conectando a la incursión...";
        }

        var confirmed = new List<string>(total);
        var pending = new List<string>(total);
        for (int index = 0; index < total; index++)
        {
            TownRaidPreparationPresentation.Member member = presentation.Members[index];
            string name = string.IsNullOrWhiteSpace(member.DisplayName) ? "Jugador" : member.DisplayName;
            if (member.LaunchAcknowledged) confirmed.Add(name);
            else pending.Add(name);
        }

        var builder = new System.Text.StringBuilder();
        builder.Append("Lanzando expedición... Confirmados: ").Append(acknowledged).Append('/').Append(total).Append('.');
        if (confirmed.Count > 0)
        {
            builder.AppendLine().Append("Confirmados: ").Append(string.Join(", ", confirmed)).Append('.');
        }
        if (pending.Count > 0)
        {
            builder.AppendLine().Append("Esperando confirmación: ").Append(string.Join(", ", pending)).Append('.');
        }

        return builder.ToString();
    }
}

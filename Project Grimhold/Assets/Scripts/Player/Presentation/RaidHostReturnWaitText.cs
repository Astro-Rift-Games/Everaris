/// <summary>Spanish presentation for a Host waiting for connected peers before closure.</summary>
public static class RaidHostReturnWaitText
{
    public static bool TryGetMessage(bool isPending, bool hasConnectedRemoteParticipants, int remainingParticipants, out string message)
    {
        message = null;
        if (!isPending || !hasConnectedRemoteParticipants)
        {
            return false;
        }

        const string reason = "Esperando a que los demás participantes salgan de la expedición";
        // Retained routing can still block shutdown before an ActivePlayers count is available.
        message = remainingParticipants > 0
            ? $"{reason} ({remainingParticipants} restantes)."
            : $"{reason}.";
        return true;
    }
}

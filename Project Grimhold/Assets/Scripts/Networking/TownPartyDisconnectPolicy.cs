/// <summary>
/// Pure authority policy for handling a player disconnecting from Town with respect to their Party (FIX-46).
/// - Product decision 1C: a player disconnecting in Town leaves their Party (leaving a Duo partner in a
///   valid Solo Party and cancelling pending invitations), EXCEPT when leaving as part of an active
///   expedition of that Party, in which case the Party preserves them.
/// </summary>
public static class TownPartyDisconnectPolicy
{
    public enum DisconnectDisposition
    {
        IgnoreUnknown,
        PreserveInExpedition,
        LeavePartyAndCancelInvitations
    }

    public static DisconnectDisposition Evaluate(ProfileId disconnectedProfile, bool isInLaunchingExpedition)
    {
        if (!disconnectedProfile.IsValid)
        {
            return DisconnectDisposition.IgnoreUnknown;
        }

        return isInLaunchingExpedition
            ? DisconnectDisposition.PreserveInExpedition
            : DisconnectDisposition.LeavePartyAndCancelInvitations;
    }

    /// <summary>
    /// Computes the remaining member when a player disconnects from a Duo Party.
    /// Returns false if the Party was Solo (the authority should free the slot instead).
    /// </summary>
    public static bool TryResolveRemainingSoloMember(
        in TownPartySnapshot party,
        ProfileId disconnectedProfile,
        out ProfileId remainingMember)
    {
        remainingMember = default;
        if (!TownPartyRules.IsValid(party) || !party.Contains(disconnectedProfile) || party.Members.Count != TownPartyRules.MaxMembers)
        {
            return false;
        }

        remainingMember = party.Members[0] == disconnectedProfile ? party.Members[1] : party.Members[0];
        return remainingMember.IsValid;
    }
}

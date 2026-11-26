using System.Collections.Generic;

/// <summary>
/// Pure, allocation-free rule for Accelerated Resolution (GD 13 section 9, DownedAndReviveArchitecture
/// section 10): a team with no valid recovery route resolves its Downed members in the accelerated
/// window. Holds no state; the Host coordinator feeds it one snapshot per team member each tick.
/// </summary>
internal static class DownedTeamRecoveryRouteRules
{
    /// <summary>
    /// True when this member is a recovery route for the team. Only a <c>Raiding</c> member with a
    /// resolvable avatar counts. An Active connected member can revive (a disconnected one cannot,
    /// section 15). A Downed member counts when it can start a Self-revive while connected, or while any
    /// valid recovery session runs on it (assisted, Self or handoff): a running session can still
    /// complete, and a Self-revive continues even after a disconnect (GD 13 section 11).
    /// </summary>
    internal static bool ProvidesRoute(in DownedTeamMemberRouteSnapshot member)
    {
        if (!member.IsKnown || !member.IsRaiding || !member.HasAvatar)
        {
            return false;
        }

        if (member.IsDowned)
        {
            return member.HasRecoverySession || (member.CanBeginSelf && member.IsConnected);
        }

        return member.IsAlive && member.IsConnected;
    }

    /// <summary>True when this member must be accelerated if the team has no route: Downed and <c>Raiding</c>.</summary>
    internal static bool IsAcceleratable(in DownedTeamMemberRouteSnapshot member)
    {
        return member.IsKnown && member.IsRaiding && member.HasAvatar && member.IsDowned;
    }

    /// <summary>
    /// True when the team has no recovery route and at least one Downed <c>Raiding</c> member, so its
    /// Downed members enter Accelerated Resolution. A team without Downed members never accelerates.
    /// Acceleration latches, so only known facts may lead to it: while any member's state is unknown the
    /// answer is false and the caller re-evaluates on a later tick. A <c>Raiding</c> member without a
    /// resolvable avatar counts as unknown (a Raiding participant always owns an avatar); a member that
    /// is not Raiding is finished whether or not its avatar resolves.
    /// </summary>
    internal static bool ShouldAccelerate(IReadOnlyList<DownedTeamMemberRouteSnapshot> members)
    {
        bool hasAcceleratable = false;
        bool hasRoute = false;
        for (int i = 0; i < members.Count; i++)
        {
            DownedTeamMemberRouteSnapshot member = members[i];
            if (!IsStateKnown(member))
            {
                return false;
            }

            hasAcceleratable |= IsAcceleratable(member);
            hasRoute |= ProvidesRoute(member);
        }

        return hasAcceleratable && !hasRoute;
    }

    private static bool IsStateKnown(in DownedTeamMemberRouteSnapshot member)
    {
        return member.IsKnown && (!member.IsRaiding || member.HasAvatar);
    }
}

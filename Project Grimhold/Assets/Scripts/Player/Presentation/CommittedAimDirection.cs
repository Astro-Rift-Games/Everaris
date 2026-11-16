using UnityEngine;

/// <summary>Presentation-only aim selection on the accepted attack's confirmed clock.</summary>
internal static class CommittedAimDirection
{
    internal static Vector2 Resolve(Vector2 liveAim, in AttackPerformedEvent attack, bool clockReadable, float seconds)
    {
        return clockReadable && attack.AttackType == AttackType.Ranged && attack.HasReleaseTimeline &&
            seconds >= 0f && seconds < attack.ScheduledWindupSeconds
            ? attack.Direction : liveAim;
    }
}

using UnityEngine;

/// <summary>
/// Tracks the single Town NPC the local player currently stands next to, so it turns towards them and returns to
/// its authored facing once they leave. Local presentation only.
/// </summary>
public sealed class TownNpcApproachFacing
{
    private TownNpcDirectionalView _tracked;

    /// <summary>
    /// Faces <paramref name="npc"/> towards the observer; a different or missing NPC releases the previous one.
    /// </summary>
    public void Track(TownNpcDirectionalView npc, Vector3 observerPosition)
    {
        if (!ReferenceEquals(_tracked, npc))
        {
            Release();
            _tracked = npc;
        }

        if (_tracked != null)
        {
            _tracked.FaceTarget(observerPosition);
        }
    }

    /// <summary>Returns the tracked NPC, if any, to its authored facing.</summary>
    public void Release()
    {
        // The Unity null check also covers an NPC destroyed while it was tracked.
        if (_tracked != null)
        {
            _tracked.RestoreInitialFacing();
        }

        _tracked = null;
    }
}

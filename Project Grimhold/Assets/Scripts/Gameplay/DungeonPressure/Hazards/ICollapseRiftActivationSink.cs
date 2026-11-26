using UnityEngine;

/// <summary>
/// Authoritative seam for abilities or local triggers to activate dormant collapse rifts within a radius (TASK-337 seam).
/// Registered on <see cref="EntityRegistry"/> by <see cref="CollapseHazardDirector"/>.
/// </summary>
public interface ICollapseRiftActivationSink
{
    int TryActivateRiftsInRadius(Vector2 center, float radius);
}

using UnityEngine;

/// <summary>
/// Stable authoritative hazard identity and radial push direction helpers (Doc 19 §8, Doc 20 §5).
/// </summary>
public static class CollapseHazardIdUtility
{
    private const uint NegativeHazardDomainBit = 0x80000000u;

    /// <summary>
    /// Derives a stable, deterministic, non-zero negative <see cref="EntityId"/> for a floor rift index
    /// under a given director network object id so it never collides with live player/enemy entity IDs.
    /// </summary>
    public static EntityId ForFloorRift(uint directorNetworkIdRaw, int riftIndex)
    {
        uint safeIndex = (uint)(riftIndex & 0x0FFF);
        uint directorBits = (directorNetworkIdRaw & 0x7FFFu) << 12;
        uint packed = NegativeHazardDomainBit | 0x40000000u | directorBits | safeIndex;
        return new EntityId(unchecked((int)packed));
    }

    /// <summary>
    /// Unit vector pointing radially away from <paramref name="hazardCenter"/> toward <paramref name="targetPosition"/>.
    /// Falls back deterministically to <see cref="Vector2.right"/> when target and center coincide.
    /// </summary>
    public static Vector2 ResolveRadialKnockbackDirection(Vector2 hazardCenter, Vector2 targetPosition)
    {
        Vector2 delta = targetPosition - hazardCenter;
        return delta.sqrMagnitude > 1e-6f ? delta.normalized : Vector2.right;
    }
}

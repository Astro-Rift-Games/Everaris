using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight snapshot of a neighbouring enemy used by <see cref="EnemyCrowdRules"/>.
/// </summary>
public readonly struct EnemyCrowdPeerSnapshot
{
    public EntityId Id { get; }
    public Vector2 Position { get; }

    public EnemyCrowdPeerSnapshot(EntityId id, Vector2 position)
    {
        Id = id;
        Position = position;
    }
}

/// <summary>
/// Pure deterministic crowd-separation and target-surround slot rules (TASK-318).
/// Prevents enemies from stacking on the same point while keeping narrow 1-tile corridors
/// flowing single-file without jamming.
/// </summary>
public static class EnemyCrowdRules
{
    private const float MinDirectionSqr = 0.0001f;

    /// <summary>
    /// Computes a local soft separation steering vector away from nearby alive peers within
    /// <paramref name="separationRadius"/>. When <paramref name="inNarrowCorridor"/> is true,
    /// lateral push against corridor walls is attenuated so enemies queue single-file cleanly.
    /// </summary>
    public static Vector2 ComputeSeparationVector(
        Vector2 selfPos,
        EntityId selfId,
        Vector2 intendedDirection,
        IReadOnlyList<EnemyCrowdPeerSnapshot> peers,
        float separationRadius,
        float separationStrength,
        bool inNarrowCorridor)
    {
        if (peers == null || peers.Count == 0 || separationRadius <= 0.01f || separationStrength <= 0f)
        {
            return Vector2.zero;
        }

        float radiusSqr = separationRadius * separationRadius;
        Vector2 accumulated = Vector2.zero;

        for (int i = 0; i < peers.Count; i++)
        {
            EnemyCrowdPeerSnapshot peer = peers[i];
            if (peer.Id == selfId)
            {
                continue;
            }

            Vector2 away = selfPos - peer.Position;
            float distSqr = away.sqrMagnitude;
            if (distSqr >= radiusSqr)
            {
                continue;
            }

            Vector2 dir;
            float weight;
            if (distSqr < MinDirectionSqr)
            {
                // Deterministic tie-breaker for exact overlap using EntityId
                float angle = (selfId.Value % 8) * (Mathf.PI * 0.25f);
                dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                weight = 1f;
            }
            else
            {
                float dist = Mathf.Sqrt(distSqr);
                dir = away / dist;
                weight = 1f - (dist / separationRadius);
            }

            accumulated += dir * weight;
        }

        if (accumulated.sqrMagnitude < MinDirectionSqr)
        {
            return Vector2.zero;
        }

        if (inNarrowCorridor && intendedDirection.sqrMagnitude >= MinDirectionSqr)
        {
            Vector2 forward = intendedDirection.normalized;
            float forwardComp = Vector2.Dot(accumulated, forward);
            Vector2 lateral = accumulated - forward * forwardComp;
            // Keep gentle spacing along corridor axis without pushing sideways into walls or reversing hard.
            float clampedForward = Mathf.Clamp(forwardComp, -0.55f, 0.55f);
            accumulated = forward * clampedForward + lateral * 0.15f;
        }

        Vector2 scaled = accumulated * separationStrength;
        return scaled.sqrMagnitude > 1f ? scaled.normalized : scaled;
    }

    /// <summary>
    /// Blends an intended navigation direction with local separation without reversing forward flow in corridors.
    /// </summary>
    public static Vector2 BlendDirectionWithSeparation(Vector2 intendedDirection, Vector2 separationVector)
    {
        if (separationVector.sqrMagnitude < MinDirectionSqr)
        {
            return intendedDirection;
        }

        if (intendedDirection.sqrMagnitude < MinDirectionSqr)
        {
            return separationVector.normalized;
        }

        Vector2 blended = intendedDirection.normalized + separationVector;
        if (blended.sqrMagnitude < MinDirectionSqr)
        {
            return intendedDirection.normalized;
        }

        return blended.normalized;
    }

    /// <summary>
    /// Deterministically assigns a walkable surround slot around <paramref name="targetPos"/> when
    /// multiple melee enemies pursue the same target. Returns <see langword="false"/> for a solo
    /// pursuer (preserving direct pursuit) or when no ring slots are walkable (single-file corridor).
    /// </summary>
    public static bool TryAssignSurroundSlot(
        EntityId selfId,
        Vector2 targetPos,
        IReadOnlyList<EntityId> pursuingPeerIds,
        float slotRadius,
        int maxSlots,
        Func<Vector2, bool> isPositionWalkable,
        Func<Vector2, Vector2, bool> hasLineOfSight,
        out Vector2 assignedSlotPosition)
    {
        assignedSlotPosition = targetPos;

        if (pursuingPeerIds == null || pursuingPeerIds.Count <= 1 || slotRadius <= 0.05f || maxSlots < 2)
        {
            return false;
        }

        // Deterministic O(k) rank by EntityId without sorting or heap allocation.
        int rank = 0;
        for (int i = 0; i < pursuingPeerIds.Count; i++)
        {
            if (pursuingPeerIds[i].Value < selfId.Value)
            {
                rank++;
            }
        }

        int slotCount = Mathf.Clamp(maxSlots, 2, 12);
        float angleStep = (Mathf.PI * 2f) / slotCount;
        int preferredStartSlot = rank % slotCount;

        // First try the enemy's own deterministic angular slot; if blocked by a wall, probe outward
        // in alternating offsets so each attacker still spreads to distinct walkable positions.
        for (int offset = 0; offset < slotCount; offset++)
        {
            int signedOffset = offset == 0 ? 0 : ((offset % 2 == 1) ? (offset + 1) / 2 : -(offset / 2));
            int slotIndex = ((preferredStartSlot + signedOffset) % slotCount + slotCount) % slotCount;
            float angle = slotIndex * angleStep;

            Vector2 candidate = targetPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * slotRadius;

            bool walkable = isPositionWalkable == null || isPositionWalkable(candidate);
            if (!walkable)
            {
                continue;
            }

            bool visible = hasLineOfSight == null || hasLineOfSight(candidate, targetPos);
            if (!visible)
            {
                continue;
            }

            assignedSlotPosition = candidate;
            return true;
        }

        return false;
    }
}

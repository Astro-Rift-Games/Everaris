using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene/grid-scoped coordinator for active Host enemies (TASK-318).
/// Holds reusable scratch buffers (zero per-tick heap allocations) and has no global static state;
/// lifecycle is owned by <see cref="PathfindingGrid"/>.
/// </summary>
public sealed class EnemyCrowdCoordinator
{
    private readonly List<EnemyMovementAIController> _agents = new List<EnemyMovementAIController>(32);
    private readonly List<EnemyCrowdPeerSnapshot> _peerBuffer = new List<EnemyCrowdPeerSnapshot>(32);
    private readonly List<EntityId> _pursuerIdBuffer = new List<EntityId>(16);

    public void Register(EnemyMovementAIController controller)
    {
        if (controller == null || _agents.Contains(controller))
        {
            return;
        }

        _agents.Add(controller);
    }

    public void Unregister(EnemyMovementAIController controller)
    {
        if (controller == null)
        {
            return;
        }

        _agents.Remove(controller);
    }

    public void Clear()
    {
        _agents.Clear();
        _peerBuffer.Clear();
        _pursuerIdBuffer.Clear();
    }

    /// <summary>
    /// Computes the local separation vector for <paramref name="self"/> against all registered alive enemies.
    /// </summary>
    public Vector2 ComputeSeparation(
        EnemyMovementAIController self,
        Vector2 intendedDirection,
        float separationRadius,
        float separationStrength,
        PathfindingGrid grid)
    {
        if (self == null || _agents.Count <= 1)
        {
            return Vector2.zero;
        }

        _peerBuffer.Clear();
        for (int i = 0; i < _agents.Count; i++)
        {
            EnemyMovementAIController agent = _agents[i];
            if (agent == null || !agent.IsAliveForCrowd)
            {
                continue;
            }

            _peerBuffer.Add(new EnemyCrowdPeerSnapshot(agent.SelfEntityId, agent.transform.position));
        }

        bool inNarrowCorridor = IsInNarrowCorridor(self.transform.position, intendedDirection, grid);

        return EnemyCrowdRules.ComputeSeparationVector(
            self.transform.position,
            self.SelfEntityId,
            intendedDirection,
            _peerBuffer,
            separationRadius,
            separationStrength,
            inNarrowCorridor);
    }

    /// <summary>
    /// Resolves a deterministic walkable surround slot around <paramref name="targetPos"/> when
    /// multiple melee enemies pursue <paramref name="targetId"/>.
    /// </summary>
    public bool TryResolveSurroundSlot(
        EnemyMovementAIController self,
        EntityId targetId,
        Vector2 targetPos,
        float slotRadius,
        int maxSlots,
        PathfindingGrid grid,
        LayerMask obstacleLayer,
        out Vector2 slotPosition)
    {
        slotPosition = targetPos;
        if (self == null || targetId.Value == 0 || !self.UsesSurroundSlots)
        {
            return false;
        }

        _pursuerIdBuffer.Clear();
        for (int i = 0; i < _agents.Count; i++)
        {
            EnemyMovementAIController agent = _agents[i];
            if (agent == null || !agent.IsAliveForCrowd || !agent.UsesSurroundSlots || !agent.IsOnPursuit)
            {
                continue;
            }

            if (agent.TryGetCurrentTarget(out EntityId peerTargetId, out _) && peerTargetId == targetId)
            {
                _pursuerIdBuffer.Add(agent.SelfEntityId);
            }
        }

        if (_pursuerIdBuffer.Count <= 1)
        {
            return false;
        }

        bool IsWalkable(Vector2 pos) => grid == null || !grid.IsBuilt || grid.IsWalkableWorldPosition(pos);
        bool HasLos(Vector2 from, Vector2 to) => EnemyVisionRules.HasLineOfSight(from, to, obstacleLayer);

        return EnemyCrowdRules.TryAssignSurroundSlot(
            self.SelfEntityId,
            targetPos,
            _pursuerIdBuffer,
            slotRadius,
            maxSlots,
            IsWalkable,
            HasLos,
            out slotPosition);
    }

    private static bool IsInNarrowCorridor(Vector2 position, Vector2 intendedDirection, PathfindingGrid grid)
    {
        if (grid == null || !grid.IsBuilt || intendedDirection.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        Vector2 forward = intendedDirection.normalized;
        Vector2 lateral = new Vector2(-forward.y, forward.x);
        float probeDist = grid.Config != null ? Mathf.Max(0.6f, grid.Config.NodeSize * 1.4f) : 0.7f;

        bool leftBlocked = !grid.IsWalkableWorldPosition(position + lateral * probeDist);
        bool rightBlocked = !grid.IsWalkableWorldPosition(position - lateral * probeDist);
        return leftBlocked && rightBlocked;
    }
}

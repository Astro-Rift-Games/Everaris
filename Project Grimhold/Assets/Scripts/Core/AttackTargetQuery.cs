using UnityEngine;

/// <summary>
/// Contiene los parámetros necesarios para realizar una consulta de objetivos.
/// </summary>
public readonly struct AttackTargetQuery
{
    public EntityId AttackerId { get; }
    public Vector2 Origin { get; }
    public Vector2 Direction { get; }
    public float Range { get; }
    public float Radius { get; }
    public int MaximumTargets { get; }
    public int TargetLayerMask { get; }

    /// <summary>Start of the line of sight to each target; meaningful only with <see cref="ObstacleLayerMask"/>.</summary>
    public Vector2 OcclusionOrigin { get; }

    /// <summary>Layers that hide a target behind them (walls, closed doors). Zero disables occlusion.</summary>
    public int ObstacleLayerMask { get; }

    public AttackTargetQuery(
        EntityId attackerId,
        Vector2 origin,
        Vector2 direction,
        float range,
        float radius,
        int maximumTargets,
        int targetLayerMask,
        Vector2 occlusionOrigin = default,
        int obstacleLayerMask = 0)
    {
        OcclusionOrigin = occlusionOrigin;
        ObstacleLayerMask = obstacleLayerMask;
        AttackerId = attackerId;
        Origin = origin;
        Direction = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.zero;
        Range = range;
        Radius = radius;
        MaximumTargets = maximumTargets;
        TargetLayerMask = targetLayerMask;
    }
}

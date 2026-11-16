using UnityEngine;

/// <summary>One-shot visual clone; waits for every child system, not just the root's duration.</summary>
[DisallowMultipleComponent]
public sealed class AttackReleaseBurstLifetime : MonoBehaviour
{
    private ParticleSystem _particles;

    internal static void Play(ParticleSystem prefab, Transform root, AttackVfxDefinition.ResolvedPose pose, Color? tint, int sortingLayer)
    {
        // Reuse the shared effect factory; no gameplay object or network state is created.
        GameObject instance = ParticleEffectPlayer.InstantiateAndPlay(prefab, root.TransformPoint(pose.Position), root.rotation * pose.Rotation);
        if (instance == null) return;
        instance.name = "AttackReleaseBurst";
        instance.transform.SetParent(root, true);
        instance.transform.localPosition = pose.Position;
        instance.transform.localRotation = pose.Rotation;
        ParticleSystem particles = instance.GetComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = system.main;
            main.loop = false;
            main.stopAction = ParticleSystemStopAction.None;
            if (tint.HasValue) main.startColor = tint.Value;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sortingLayerID = sortingLayer;
            renderer.sortingOrder = pose.SortingOrder;
        }
        var lifetime = instance.AddComponent<AttackReleaseBurstLifetime>();
        lifetime._particles = particles;
        ParticleEffectPlayer.PlayInPlace(particles, root.TransformPoint(pose.Position));
    }

    private void LateUpdate()
    {
        if (_particles == null || !_particles.IsAlive(true)) Destroy(gameObject);
    }
}

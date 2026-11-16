using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>Dedicated visual tails finish locally; no flight gameplay is prolonged.</summary>
[DisallowMultipleComponent]
public sealed class ProjectileTrailDetacher : NetworkBehaviour
{
    [SerializeField] private TrailRenderer[] _trails;
    [SerializeField] private ParticleSystem[] _particles;
    private readonly List<GameObject> _registered = new();

    /// <summary>Registers a dedicated profile visual root once. Never register gameplay objects.</summary>
    public void Register(GameObject visualRoot)
    {
        if (visualRoot == null || visualRoot == gameObject || !visualRoot.transform.IsChildOf(transform) ||
            _registered.Contains(visualRoot)) return;
        _registered.Add(visualRoot);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        bool detach = runner != null && runner.IsRunning && !runner.IsShutdown;
        if (detach)
        {
            if (_trails != null)
                foreach (TrailRenderer trail in _trails)
                {
                    if (trail == null || trail.gameObject == gameObject) continue;
                    trail.transform.SetParent(null, true);
                    Destroy(trail.gameObject, StopTrail(trail));
                }
            if (_particles != null)
                foreach (ParticleSystem particles in _particles)
                {
                    if (particles == null || particles.gameObject == gameObject) continue;
                    particles.transform.SetParent(null, true);
                    Destroy(particles.gameObject, StopParticles(particles));
                }
        }
        foreach (GameObject root in _registered)
        {
            if (root == null) continue;
            if (detach)
            {
                float remaining = 0f;
                foreach (TrailRenderer trail in root.GetComponentsInChildren<TrailRenderer>(true))
                    remaining = Mathf.Max(remaining, StopTrail(trail));
                foreach (ParticleSystem particles in root.GetComponentsInChildren<ParticleSystem>(true))
                    remaining = Mathf.Max(remaining, StopParticles(particles));
                root.transform.SetParent(null, true);
                Destroy(root, remaining);
            }
            else
            {
                // A pooled projectile must not retain a profile child after runner teardown.
                root.SetActive(false);
                Destroy(root);
            }
        }
        _registered.Clear();
    }

    private static float StopTrail(TrailRenderer trail)
    {
        trail.emitting = false;
        trail.autodestruct = false;
        return Mathf.Max(0f, trail.time);
    }
    private static float StopParticles(ParticleSystem particles)
    {
        particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        var main = particles.main;
        main.stopAction = ParticleSystemStopAction.None;
        var live = new ParticleSystem.Particle[particles.particleCount];
        int count = particles.GetParticles(live);
        float remaining = 0f;
        for (int i = 0; i < count; i++) remaining = Mathf.Max(remaining, live[i].remainingLifetime);
        var trails = particles.trails;
        if (trails.enabled) remaining += trails.lifetime.constantMax;
        return remaining;
    }
}

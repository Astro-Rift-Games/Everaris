using UnityEngine;

/// <summary>Tints projectile-local instances; only profile effects override authored lifecycle.</summary>
public static class ProjectileParticlePlayer
{
    public static void Play(ParticleSystem prefab, Vector2 position, Quaternion rotation, Color? tint, bool oneShot = false)
    {
        GameObject instance = ParticleEffectPlayer.InstantiateAndPlay(prefab, position, rotation);
        if (instance == null) return;
        ParticleSystem root = instance.GetComponent<ParticleSystem>();
        // Clear any play-on-awake emission before presenting the configured one-shot this frame.
        root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (ParticleSystem particles in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = particles.main;
            if (oneShot)
            {
                main.loop = false;
                main.stopAction = particles == root ? ParticleSystemStopAction.Destroy : ParticleSystemStopAction.None;
            }
            if (tint.HasValue) main.startColor = tint.Value;
        }
        ParticleEffectPlayer.PlayInPlace(root, position);
    }
}

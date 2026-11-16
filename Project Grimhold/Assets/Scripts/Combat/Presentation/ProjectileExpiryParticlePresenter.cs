using UnityEngine;

/// <summary>Optional local fizzle for expired flight, never for impact or runner teardown.</summary>
[DisallowMultipleComponent]
public sealed class ProjectileExpiryParticlePresenter : MonoBehaviour
{
    [SerializeField] private NetworkProjectile _projectile;
    [Tooltip("Optional one-shot fizzle. Looping is disabled and Stop Action is set to Destroy on the instance.")]
    [SerializeField] private ParticleSystem _fizzleParticles;

    private ProjectileVisualProfilePresenter _visual;

    private void Awake()
    {
        if (_projectile == null) _projectile = GetComponent<NetworkProjectile>();
        _visual = GetComponent<ProjectileVisualProfilePresenter>();
    }

    private void OnEnable()
    {
        if (_projectile != null) _projectile.Expired += OnExpired;
    }

    private void OnDisable()
    {
        if (_projectile != null) _projectile.Expired -= OnExpired;
    }

    private void OnExpired(Vector2 position, Vector2 direction)
    {
        if (_fizzleParticles == null || (_visual != null && _visual.HasFizzleOverride)) return;
        Quaternion rotation = direction.sqrMagnitude > 0.0001f
            ? Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg)
            : transform.rotation;
        GameObject instance = ParticleEffectPlayer.InstantiateAndPlay(_fizzleParticles, position, rotation);
        ParticleSystem particles = instance.GetComponent<ParticleSystem>();
        var main = particles.main;
        main.loop = false;
        main.stopAction = ParticleSystemStopAction.Destroy;
    }
}

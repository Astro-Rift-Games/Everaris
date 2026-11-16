using UnityEngine;

/// <summary>One hand's optional charge/burst lifecycle, owned by the attack VFX presenter.</summary>
internal sealed class WindupChargePresentation
{
    private WeaponDefinition _weapon;
    private AttackPerformedEvent _attack;
    private bool _active;
    private bool _burstConsumed;
    private bool _chargeEnded;
    private Color? _tint;
    private ParticleSystem.MinMaxGradient[] _authoredColors;
    private ParticleSystem _instance;
    private ParticleSystem _prefab;
    private ParticleSystem[] _systems;
    private ParticleSystemRenderer[] _renderers;
    private Vector3 _authoredScale;

    internal bool Active => _active;
    internal AttackPerformedEvent Attack => _attack;
    internal WeaponDefinition Weapon => _weapon;

    internal void Begin(in AttackPerformedEvent attack, WeaponDefinition weapon, Color? tint, bool resumed)
    {
        // Repeated snapshots of the same hand must never restart a charge or duplicate a burst.
        if (_weapon == weapon && _attack.Sequence == attack.Sequence && _attack.SimulationTick == attack.SimulationTick &&
            _attack.SourceSlot == attack.SourceSlot && !(resumed && !_active)) return;
        Cancel();
        _attack = attack;
        _weapon = weapon;
        _tint = tint;
        _burstConsumed = resumed;
        _chargeEnded = false;
        _active = attack.HasReleaseTimeline && weapon != null &&
            (weapon.Presentation.WindupChargeVfx != null || weapon.Presentation.ReleaseBurstParticles != null);
    }

    internal bool CheckClock(IPlayerPresentationState state, out float seconds)
    {
        seconds = 0f;
        if (!_active) return false;
        if (!state.IsAlive || state.IsDowned || state.IsConsuming ||
            !state.TryGetAttackPresentationSeconds(_attack, out seconds))
        {
            Cancel();
            return false;
        }
        if (seconds >= _attack.ScheduledWindupSeconds)
        {
            _chargeEnded = true;
            StopCharge();
        }
        return true;
    }

    internal void Present(Transform root, AttackVfxDefinition.ResolvedPose pose, float seconds, bool fullyDrawn, int sortingLayer)
    {
        if (!_active) return;
        var presentation = _weapon.Presentation;
        var charge = presentation.WindupChargeVfx;
        if (!_chargeEnded && charge != null && charge.TryValidate(out _) &&
            presentation.AttackVfx.Visual is CastFlashVfxVisualDefinition flash &&
            WindupChargeWindow.TryResolve(seconds, _attack.ScheduledWindupSeconds, out float progress))
        {
            EnsureCharge(root, charge.Prefab, sortingLayer, pose.SortingOrder);
            _instance.transform.localPosition = pose.Position + (Vector3)flash.Center;
            _instance.transform.localRotation = pose.Rotation;
            float scale = Mathf.Lerp(charge.MinimumScale, charge.MaximumScale, progress) *
                (fullyDrawn ? charge.FullDrawMultiplier : 1f);
            _instance.transform.localScale = _authoredScale * scale;
            if (!_instance.isPlaying) _instance.Play(true);
        }
        else StopCharge();

        var vfx = presentation.AttackVfx;
        float vfxSeconds = AttackTiming.ReleaseVfxSeconds(seconds, _attack.ScheduledWindupSeconds, vfx.ReleaseLeadSeconds);
        if (!_burstConsumed && vfxSeconds >= -.0001f)
        {
            _burstConsumed = true;
            // A fresh notification already beyond the visual's window is also not replayed.
            if (vfxSeconds < vfx.Clip.length && presentation.ReleaseBurstParticles != null)
                AttackReleaseBurstLifetime.Play(presentation.ReleaseBurstParticles, root, pose, _tint, sortingLayer);
        }
        if (_chargeEnded && _burstConsumed) _active = false;
    }

    private void EnsureCharge(Transform root, ParticleSystem prefab, int sortingLayer, int sortingOrder)
    {
        if (_instance == null || _prefab != prefab)
        {
            DisposeInstance();
            _prefab = prefab;
            _instance = Object.Instantiate(prefab, root, false);
            _instance.name = _attack.IsOffHand ? "OffHandWindupCharge" : "MainHandWindupCharge";
            _instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _authoredScale = _instance.transform.localScale;
            _systems = _instance.GetComponentsInChildren<ParticleSystem>(true);
            _renderers = _instance.GetComponentsInChildren<ParticleSystemRenderer>(true);
            _authoredColors = new ParticleSystem.MinMaxGradient[_systems.Length];
            for (int i = 0; i < _systems.Length; i++) _authoredColors[i] = _systems[i].main.startColor;
            foreach (ParticleSystem particles in _systems)
            {
                var main = particles.main;
                main.loop = true;
                main.stopAction = ParticleSystemStopAction.None;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
        }
        foreach (ParticleSystemRenderer renderer in _renderers)
        {
            renderer.sortingLayerID = sortingLayer;
            renderer.sortingOrder = sortingOrder;
        }
        for (int i = 0; i < _systems.Length; i++)
        {
            var main = _systems[i].main;
            main.startColor = _tint.HasValue ? new ParticleSystem.MinMaxGradient(_tint.Value) : _authoredColors[i];
        }
    }

    private void StopCharge()
    {
        if (_instance != null) _instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    internal void Cancel()
    {
        _active = false;
        _chargeEnded = true;
        _burstConsumed = true;
        StopCharge();
    }

    internal void Dispose()
    {
        Cancel();
        DisposeInstance();
        _weapon = null;
        _attack = default;
    }

    private void DisposeInstance()
    {
        if (_instance != null)
        {
            if (Application.isPlaying) Object.Destroy(_instance.gameObject);
            else Object.DestroyImmediate(_instance.gameObject);
        }
        _instance = null;
        _prefab = null;
        _systems = null;
        _renderers = null;
        _authoredColors = null;
    }
}

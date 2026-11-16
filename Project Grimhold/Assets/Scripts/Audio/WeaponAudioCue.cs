/// <summary>Local one-shot cue state for one confirmed attack timeline. Never advances gameplay.</summary>
public sealed class WeaponAudioCue
{
    private bool _hasAttack;
    private bool _releasePlayed;
    private int _reloadEndTick;
    private bool _chargeConsumed;
    public bool Charging { get; private set; }

    /// <summary>Consume Charge only in the confirmed windup; late observation never restarts it.</summary>
    public bool TryCharge(float presentationSeconds)
    {
        if (!Active || _chargeConsumed || _releasePlayed || !Attack.HasReleaseTimeline ||
            !AttackTiming.IsFinite(presentationSeconds) || presentationSeconds < 0f) return false;
        _chargeConsumed = true;
        Charging = presentationSeconds < Attack.ScheduledWindupSeconds;
        return Charging;
    }

    // Key presence, not weapon kind, selects optional aliases. Attack wins even if its entry is empty.
    public static string ImpactKey(bool characterVictim, bool hasAttack) =>
        characterVictim ? (hasAttack ? "Attack" : "Hit") : "Block";
    public static string ReleaseKey(AttackType type, bool hasCast) =>
        type == AttackType.Melee ? "Swing" : (hasCast ? "Cast" : "Shoot");

    public bool Active { get; private set; }
    public AttackPerformedEvent Attack { get; private set; }

    public bool Accept(in AttackPerformedEvent attack, bool resumed = false)
    {
        if (_hasAttack && attack.Sequence == Attack.Sequence && attack.SimulationTick == Attack.SimulationTick &&
            attack.AttackerId.Equals(Attack.AttackerId) && attack.SourceSlot == Attack.SourceSlot) return false;
        Attack = attack;
        _hasAttack = true;
        Active = true;
        _releasePlayed = resumed;
        _chargeConsumed = resumed;
        Charging = false;
        _reloadEndTick = 0;
        return true;
    }

    public bool TryRelease(float presentationSeconds)
    {
        if (!Active || _releasePlayed || !AttackTiming.IsFinite(presentationSeconds) ||
            (Attack.HasReleaseTimeline && presentationSeconds < Attack.ScheduledWindupSeconds)) return false;
        _releasePlayed = true;
        Charging = false;
        _chargeConsumed = true;
        return true;
    }

    public bool TryReload(in WeaponReloadState reload)
    {
        if (!Active || Attack.AttackType != AttackType.Ranged || !reload.Reloading ||
            reload.ReloadEndTick == _reloadEndTick) return false;
        _reloadEndTick = reload.ReloadEndTick;
        return true;
    }

    public void CancelRelease()
    {
        _releasePlayed = true;
        _chargeConsumed = true;
        Charging = false;
    }

    // Keep the consumed identity across disable/re-enable; never replay an interrupted local cue.
    public void Suspend()
    {
        Active = false;
        CancelRelease();
    }
}

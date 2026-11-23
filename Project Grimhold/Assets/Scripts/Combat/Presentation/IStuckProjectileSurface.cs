/// <summary>
/// Presentation-only veto a body can expose for stuck projectile visuals. A body that reports false takes no new
/// stuck copy and drops the ones it carries. Never read by simulation.
/// </summary>
public interface IStuckProjectileSurface
{
    bool HoldsStuckProjectiles { get; }
}

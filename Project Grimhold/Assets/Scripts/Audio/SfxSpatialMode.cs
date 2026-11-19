/// <summary>
/// How a one-shot SFX is spatialized by <see cref="AudioManager.PlaySfx(in CustomClip, UnityEngine.Vector3, SfxSpatialMode)"/>.
/// Rule: UI, HUD, dialogue and global warnings are <see cref="Flat2D"/>; events happening in the world are <see cref="World3D"/>.
/// </summary>
public enum SfxSpatialMode
{
    /// <summary>The overload's historical behavior: the CustomClip's SpatialBlend, or 2D for a bare AudioClip.</summary>
    Default = 0,
    /// <summary>Non-positional (SpatialBlend 0); the position argument is irrelevant.</summary>
    Flat2D = 1,
    /// <summary>Positional (SpatialBlend 1) with the central distance settings of <see cref="AudioManager"/>.</summary>
    World3D = 2
}

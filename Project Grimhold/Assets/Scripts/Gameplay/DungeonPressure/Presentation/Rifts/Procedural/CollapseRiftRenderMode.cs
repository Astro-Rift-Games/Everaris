/// <summary>
/// Rendering mode for collapse rifts in <see cref="CollapseRiftConfig"/> (TASK-341).
/// <see cref="SpriteSheet"/> preserves legacy frame-swapping spritesheets as fallback.
/// <see cref="Procedural"/> generates a unique per-rift silhouette packed into a single per-raid atlas.
/// </summary>
public enum CollapseRiftRenderMode : byte
{
    SpriteSheet = 0,
    Procedural = 1
}

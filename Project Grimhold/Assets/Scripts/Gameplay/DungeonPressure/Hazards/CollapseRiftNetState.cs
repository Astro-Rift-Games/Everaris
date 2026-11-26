using Fusion;

/// <summary>
/// Compact replicated state for one authoritative floor collapse rift.
/// Stored in <see cref="CollapseHazardDirector.FloorRifts"/> so up to 256 rifts replicate
/// as a single delta-compressed Fusion NetworkArray rather than spawning per-rift NetworkObjects.
/// </summary>
public struct CollapseRiftNetState : INetworkStruct
{
    private const byte FlagActivatable = 1 << 0;
    private const byte FlagSecondaryBatch = 1 << 1;
    private const byte FlagEffectApplied = 1 << 2;

    public byte StageRaw;
    public byte Flags;
    public int StateStartTick;
    public int PhaseEndTick;

    public CollapseRiftAuthoritativeStage Stage
    {
        get => (CollapseRiftAuthoritativeStage)StageRaw;
        set => StageRaw = (byte)value;
    }

    public bool IsActivatable
    {
        get => (Flags & FlagActivatable) != 0;
        set => Flags = value ? (byte)(Flags | FlagActivatable) : (byte)(Flags & ~FlagActivatable);
    }

    public bool IsSecondaryBatch
    {
        get => (Flags & FlagSecondaryBatch) != 0;
        set => Flags = value ? (byte)(Flags | FlagSecondaryBatch) : (byte)(Flags & ~FlagSecondaryBatch);
    }

    public bool EffectApplied
    {
        get => (Flags & FlagEffectApplied) != 0;
        set => Flags = value ? (byte)(Flags | FlagEffectApplied) : (byte)(Flags & ~FlagEffectApplied);
    }

    public CollapseRiftBatch Batch => IsSecondaryBatch
        ? CollapseRiftBatch.Stage3Secondary
        : CollapseRiftBatch.Stage2Initial;

    public bool IsOccupied =>
        Stage == CollapseRiftAuthoritativeStage.Telegraph ||
        Stage == CollapseRiftAuthoritativeStage.Erupting ||
        Stage == CollapseRiftAuthoritativeStage.Cooldown;

    public bool IsVisible =>
        Stage != CollapseRiftAuthoritativeStage.Hidden &&
        Stage != CollapseRiftAuthoritativeStage.Disabled;
}

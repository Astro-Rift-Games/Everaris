using System.Collections.Generic;

/// <summary>Projection of one Town NPC upgrade: current state, next result, costs and availability (Doc 23 §11).</summary>
public sealed class NpcUpgradePreview
{
    public NpcUpgradeOperation Operation { get; internal set; }
    public string LootId { get; internal set; }
    public WeaponScalingGrade CurrentGrade { get; internal set; }
    public WeaponScalingGrade ProjectedGrade { get; internal set; }
    public int CurrentSocketCount { get; internal set; }
    public int ProjectedSocketCount { get; internal set; }
    public int CurrentArmorLevel { get; internal set; }
    public int ProjectedArmorLevel { get; internal set; }
    public int CurrentPhysicalDefense { get; internal set; }
    public int ProjectedPhysicalDefense { get; internal set; }
    public int CurrentMagicalDefense { get; internal set; }
    public int ProjectedMagicalDefense { get; internal set; }
    public int CurrentObjectValue { get; internal set; }
    public int ProjectedObjectValue { get; internal set; }
    public int GoldRequired { get; internal set; }
    public int GoldAvailable { get; internal set; }
    public IReadOnlyList<NpcUpgradeMaterialLine> Materials { get; internal set; } = System.Array.Empty<NpcUpgradeMaterialLine>();
    public NpcUpgradeBlockReason BlockReason { get; internal set; }
    public bool CanConfirm => BlockReason == NpcUpgradeBlockReason.None;
}

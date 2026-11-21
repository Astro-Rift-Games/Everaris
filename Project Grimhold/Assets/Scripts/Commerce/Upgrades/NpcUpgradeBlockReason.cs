/// <summary>Why a previewed Town NPC upgrade cannot be confirmed (Doc 23 §§9.4, 11).</summary>
public enum NpcUpgradeBlockReason
{
    None,
    NotEligible,
    WrongMerchant,
    LimitReached,
    GradeLocked,
    SocketExists,
    InsufficientGold,
    MissingMaterials
}

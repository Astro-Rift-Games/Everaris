using System;

namespace Grimhold.Backend
{
    /// <summary>Authoritative shop snapshot after an upgrade plus the applied outcome.</summary>
    [Serializable]
    public struct UpgradeItemResult
    {
        public bool alreadySecured;
        public long currency;
        public InventoryItemData[] stash;
        public InventoryItemData[] loadout;
        public PreparedEquipmentData preparedEquipment;
        public int revision;
        public UpgradeOutcomeData upgrade;
    }
}

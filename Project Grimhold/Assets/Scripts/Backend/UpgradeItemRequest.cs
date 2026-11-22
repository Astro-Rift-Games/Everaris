using System;

namespace Grimhold.Backend
{
    /// <summary>
    /// Town NPC upgrade intent for POST /character/me/inventory/upgrades (TASK-302). Encoded by
    /// InventoryRequestJson: <c>slot</c> is the loadout index or the prepared-equipment slot name per <c>location</c>.
    /// A retry must resend the same transactionId and intent; the backend replays it with alreadySecured.
    /// </summary>
    [Serializable]
    public struct UpgradeItemRequest
    {
        public const string LoadoutLocation = "loadout";
        public const string EquipmentLocation = "equipment";

        public string transactionId;
        public int expectedRevision;
        public string merchant;
        public string operation;
        public string instanceId;
        public string location;
        public int loadoutSlot;
        public EquipmentSlot equipmentSlot;
    }
}

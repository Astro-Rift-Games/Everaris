using System;
using System.Collections.Generic;

namespace Grimhold.Backend
{
    /// <summary>Recorded outcome of one applied upgrade (also returned on replay).</summary>
    [Serializable]
    public sealed class UpgradeOutcomeData
    {
        public string operation;
        public string location;
        public string lootId;
        public EquipmentInstanceData instance;
        public long gold;
        public Dictionary<string, int> materials;
        public int objectValue;
    }
}

using System;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Grimhold.Backend
{
    /// <summary>Town entries carry slots; extraction wire payloads deliberately remain aggregate.</summary>
    public static class InventoryRequestJson
    {
        [Serializable]
        private struct AggregateItem
        {
            public string lootId;
            public int amount;
        }

        [Serializable]
        private sealed class Extraction
        {
            public string raidId;
            public int resultSequence;
            public AggregateItem[] items;
        }

        [Serializable]
        private sealed class UnifiedExtraction
        {
            public string raidId;
            public int resultSequence;
            public AggregateItem[] items;
            public PreparedEquipmentData preparedEquipment;
            public ExtractionProgressionData progression;
        }

        public static string Encode<T>(T body)
        {
            if (body is UpgradeItemRequest upgrade)
                return new JObject
                {
                    ["transactionId"] = upgrade.transactionId, ["expectedRevision"] = upgrade.expectedRevision,
                    ["merchant"] = upgrade.merchant, ["operation"] = upgrade.operation, ["instanceId"] = upgrade.instanceId,
                    ["location"] = upgrade.location,
                    ["slot"] = upgrade.location == UpgradeItemRequest.EquipmentLocation
                        ? PreparedEquipmentJsonConverter.Name(upgrade.equipmentSlot) : (JToken)upgrade.loadoutSlot
                }.ToString(Formatting.None);
            if (body is InstallGemRequest install)
                return new JObject
                {
                    ["transactionId"] = install.transactionId, ["weaponList"] = ListName(install.weaponList),
                    ["weaponSlot"] = install.weaponSlot, ["instanceId"] = install.instanceId,
                    ["gemList"] = ListName(install.gemList), ["gemSlot"] = install.gemSlot,
                    ["gemLootId"] = install.gemLootId, ["expectedRevision"] = install.expectedRevision
                }.ToString(Formatting.None);
            if (body is UpdatePreparedEquipmentRequest equipment && equipment.instanceEquipment.HasValue)
            {
                var json = new JObject();
                foreach (var slot in EquipmentSlotRules.AllSlots)
                {
                    var data = equipment.instanceEquipment.Value;
                    string id = data.Get(slot);
                    JToken entry = JValue.CreateNull();
                    if (!string.IsNullOrEmpty(id))
                    {
                        EquipmentInstanceData instance = null;
                        data.instances?.TryGetValue(slot, out instance);
                        if (instance == null || !EquipmentInstance.IsValidId(instance.instanceId))
                            throw new ArgumentException("Prepared equipment requires a backend instance ID.");
                        entry = new JObject { ["lootId"] = id, ["instanceId"] = instance.instanceId };
                    }
                    json[PreparedEquipmentJsonConverter.Name(slot)] = entry;
                }
                json["expectedRevision"] = equipment.expectedRevision;
                return json.ToString(Formatting.None);
            }
            if (body is CommitExtractionRequest legacy)
                return JsonUtility.ToJson(new Extraction
                { raidId = legacy.raidId, resultSequence = legacy.resultSequence, items = Aggregate(legacy.items) });
            if (body is CommitExtractionUnifiedRequest unified)
                return JsonUtility.ToJson(new UnifiedExtraction
                {
                    raidId = unified.raidId, resultSequence = unified.resultSequence, items = Aggregate(unified.items),
                    preparedEquipment = unified.preparedEquipment, progression = unified.progression
                });
            if (body is PublishExtractionResultRequest published)
                return new JObject
                {
                    ["raidId"] = published.raidId, ["resultId"] = published.resultId,
                    ["resultSequence"] = published.resultSequence, ["targetCharacterId"] = published.targetCharacterId,
                    ["items"] = ResultItems(published.items),
                    ["preparedEquipment"] = ResultEquipment(published.preparedEquipment),
                    ["experienceGranted"] = published.experienceGranted, ["issuedAt"] = published.issuedAt,
                    ["hostSignature"] = published.hostSignature
                }.ToString(Formatting.None);
            if (body is ShopTradeRequest trade)
            {
                var obj = new JObject
                {
                    ["transactionId"] = trade.transactionId,
                    ["expectedRevision"] = trade.expectedRevision
                };
                if (trade.purchases != null)
                {
                    var pArr = new JArray();
                    foreach (var p in trade.purchases)
                        pArr.Add(new JObject { ["lootId"] = p.lootId, ["amount"] = p.amount });
                    obj["purchases"] = pArr;
                }
                if (trade.sales != null)
                {
                    var sArr = new JArray();
                    foreach (var s in trade.sales)
                        sArr.Add(new JObject { ["lootId"] = s.lootId, ["amount"] = s.amount, ["sourceSlot"] = s.sourceSlot });
                    obj["sales"] = sArr;
                }
                if (trade.buybacks != null && trade.buybacks.Length > 0)
                {
                    var bArr = new JArray();
                    foreach (var b in trade.buybacks)
                        bArr.Add(new JObject { ["entryId"] = b.entryId, ["amount"] = b.amount });
                    obj["buybacks"] = bArr;
                }
                if (!string.IsNullOrEmpty(trade.weekId)) obj["weekId"] = trade.weekId;
                if (!string.IsNullOrEmpty(trade.merchantId)) obj["merchantId"] = trade.merchantId;
                return obj.ToString(Formatting.None);
            }
            return JsonUtility.ToJson(body);
        }

        private static string ListName(InventoryList list) => list switch
        {
            InventoryList.Stash => "stash", InventoryList.Loadout => "loadout",
            _ => throw new ArgumentOutOfRangeException(nameof(list))
        };

        /// <summary>Quantity-only bodies: units of one template merge into a single amount.</summary>
        private static AggregateItem[] Aggregate(InventoryItemData[] items)
        {
            if (items == null) return null;
            var result = new System.Collections.Generic.List<AggregateItem>(items.Length);
            foreach (InventoryItemData item in items)
            {
                int index = result.FindIndex(existing => existing.lootId == item.lootId);
                if (index < 0) result.Add(new AggregateItem { lootId = item.lootId, amount = item.amount });
                else result[index] = new AggregateItem { lootId = item.lootId, amount = result[index].amount + item.amount };
            }
            return result.ToArray();
        }

        /// <summary>ID-free result items: one entry per equipment unit, modifiers only when present.</summary>
        private static JArray ResultItems(InventoryItemData[] items)
        {
            var array = new JArray();
            if (items == null) return array;
            foreach (InventoryItemData item in items)
            {
                var entry = new JObject { ["lootId"] = item.lootId, ["amount"] = item.amount };
                if (item.instance?.modifiers != null) entry["modifiers"] = ModifiersJson(item.instance.modifiers);
                array.Add(entry);
            }
            return array;
        }

        /// <summary>ID-free prepared equipment: each slot is null or {lootId, modifiers?}.</summary>
        private static JObject ResultEquipment(PreparedEquipmentData data)
        {
            var json = new JObject();
            foreach (var slot in EquipmentSlotRules.AllSlots)
            {
                string id = data.Get(slot);
                JToken entry = JValue.CreateNull();
                if (!string.IsNullOrEmpty(id))
                {
                    var unit = new JObject { ["lootId"] = id };
                    EquipmentInstanceData instance = null;
                    data.instances?.TryGetValue(slot, out instance);
                    if (instance?.modifiers != null) unit["modifiers"] = ModifiersJson(instance.modifiers);
                    entry = unit;
                }
                json[PreparedEquipmentJsonConverter.Name(slot)] = entry;
            }
            return json;
        }

        private static JObject ModifiersJson(WeaponInstanceModifiersData modifiers)
        {
            // Armor pieces carry only their level (ItemInstanceArchitecture §4.4).
            if (modifiers.IsArmorShape) return new JObject { ["upgradeLevel"] = modifiers.upgradeLevel.Value };
            var json = new JObject();
            if (modifiers.primary != null) json["primary"] = ScalingJson(modifiers.primary);
            if (modifiers.secondary != null) json["secondary"] = ScalingJson(modifiers.secondary);
            json["gemSocketCount"] = modifiers.gemSocketCount;
            var gems = new JArray();
            foreach (string gem in modifiers.gems ?? Array.Empty<string>())
                gems.Add(string.IsNullOrEmpty(gem) ? JValue.CreateNull() : (JToken)gem);
            json["gems"] = gems;
            return json;
        }

        private static JObject ScalingJson(WeaponScalingModifierData scaling) =>
            new JObject { ["attribute"] = scaling.attribute, ["grade"] = scaling.grade };
    }
}

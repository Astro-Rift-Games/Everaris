#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using Grimhold.Backend;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// TASK-303 T1 (S5, S7): backend armor instances carry modifiers {upgradeLevel: 0..3} only (TASK-302,
/// ItemInstanceArchitecture §4.4). Unity must round-trip that level in Town without changing weapon shapes,
/// and Raid admission must accept it (the level is not carried into the Raid; follow-up per L6).
/// </summary>
public sealed class ArmorUpgradeLevelRoundTripTests
{
    private const string Id = "0123456789abcdef01234567";
    private const string Armor = "heavy_armor_breastplate";
    private LootDefinitionCatalog Catalog => AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
        "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");

    private LootDefinition Def(string id)
    {
        Assert.That(Catalog.TryGet(id, out var definition), Is.True, id);
        return definition;
    }

    [Test]
    public void V3_Hydration_KeepsArmorUpgradeLevelInLoadoutAndPreparedEquipment()
    {
        string json = "{\"inventorySchemaVersion\":3,\"revision\":4,\"loadout\":[{\"slot\":2,\"lootId\":\"" + Armor +
            "\",\"amount\":1,\"instance\":{\"instanceId\":\"" + Id + "\",\"modifiers\":{\"upgradeLevel\":2}}}]," +
            "\"preparedEquipment\":{\"armor\":{\"lootId\":\"" + Armor + "\",\"instance\":{\"instanceId\":\"1123456789abcdef01234567\",\"modifiers\":{\"upgradeLevel\":3}}}}}";
        var data = JsonConvert.DeserializeObject<InventoryData>(json);
        var profile = new ProfileId("49494949494949494949494949494949");
        var snapshot = new LocalProfileSnapshot { ProfileId = profile, RequiresEquipmentHydration = true };

        Assert.That(ApplicationStashServiceBootstrapper.HydrateSnapshot(profile, snapshot, data, null, Catalog, null), Is.True);
        Assert.That(snapshot.Loadout[0].Instance.InstanceId, Is.EqualTo(Id));
        Assert.That(snapshot.Loadout[0].Instance.ArmorUpgradeLevel, Is.EqualTo(2));
        Assert.That(snapshot.Loadout[0].Instance.Modifiers, Is.Null);
        Assert.That(snapshot.PreparedEquipment.GetInstance(EquipmentSlot.Armor).ArmorUpgradeLevel, Is.EqualTo(3));
    }

    [Test]
    public void PlainArmorWithoutModifiers_IsPlusZero()
    {
        var data = JsonConvert.DeserializeObject<EquipmentInstanceData>("{\"instanceId\":\"" + Id + "\"}");
        Assert.That(data.TryToInstance(Def(Armor), true, out var instance, out string error), Is.True, error);
        Assert.That(instance.ArmorUpgradeLevel, Is.EqualTo(0));
    }

    [TestCase(Armor, "{\"upgradeLevel\":4}")]
    [TestCase(Armor, "{\"upgradeLevel\":-1}")]
    [TestCase(Armor, "{\"upgradeLevel\":1,\"gemSocketCount\":1,\"gems\":[null]}")]
    [TestCase(Armor, "{\"gemSocketCount\":0,\"gems\":[]}")]
    [TestCase("rapier", "{\"upgradeLevel\":1}")]
    [TestCase("rapier", "{\"upgradeLevel\":1,\"gemSocketCount\":0,\"gems\":[]}")]
    public void InvalidArmorOrWeaponShapes_AreRejected(string lootId, string modifiers)
    {
        var data = JsonConvert.DeserializeObject<EquipmentInstanceData>("{\"instanceId\":\"" + Id + "\",\"modifiers\":" + modifiers + "}");
        Assert.That(data.TryToInstance(Def(lootId), true, out var instance, out _), Is.False);
        Assert.That(instance, Is.Null);
    }

    [TestCase("{\"upgradeLevel\":1}")]
    [TestCase("{\"gemSocketCount\":0,\"gems\":[]}")]
    public void Shield_WithAnyModifiers_IsRejected(string modifiers)
    {
        var data = JsonConvert.DeserializeObject<EquipmentInstanceData>("{\"instanceId\":\"" + Id + "\",\"modifiers\":" + modifiers + "}");
        Assert.That(data.TryToInstance(Def("shield"), true, out var instance, out _), Is.False);
        Assert.That(instance, Is.Null);
    }

    [Test]
    public void WeaponModifiersJson_KeySetAndOrderUnchanged()
    {
        var json = JObject.Parse(JsonConvert.SerializeObject(new WeaponInstanceModifiersData { gemSocketCount = 1, gems = new[] { "RES_RUBY_GEM" } }));
        Assert.That(string.Join(",", System.Linq.Enumerable.Select(json.Properties(), p => p.Name)),
            Is.EqualTo("primary,secondary,gemSocketCount,gems"));
    }

    [Test]
    public void ArmorInstanceSerializes_OnlyUpgradeLevel_AndPlusZeroOmitsModifiers()
    {
        JObject upgraded = JObject.Parse(JsonConvert.SerializeObject(EquipmentInstanceData.FromInstance(new EquipmentInstance(Id, null, 2))));
        Assert.That(upgraded["modifiers"].ToString(Formatting.None), Is.EqualTo("{\"upgradeLevel\":2}"));
        JObject plain = JObject.Parse(JsonConvert.SerializeObject(EquipmentInstanceData.FromInstance(new EquipmentInstance(Id))));
        Assert.That(plain["modifiers"]?.Type ?? JTokenType.Null, Is.EqualTo(JTokenType.Null));
    }

    [Test]
    public void WeaponInstanceJson_HasNoUpgradeLevelKey()
    {
        var weapon = EquipmentInstance.PlainCache(Def("rapier"));
        JObject json = JObject.Parse(JsonConvert.SerializeObject(EquipmentInstanceData.FromInstance(new EquipmentInstance(Id, weapon.Modifiers))));
        Assert.That(((JObject)json["modifiers"]).Property("upgradeLevel"), Is.Null);
        Assert.That(json["modifiers"]["gemSocketCount"].Value<int>(), Is.EqualTo(0));
        Assert.That(json["modifiers"]["gems"], Is.Not.Null);
    }

    [TestCase(-1)]
    [TestCase(4)]
    public void EquipmentInstance_RejectsLevelsOutsideZeroToThree(int level) =>
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new EquipmentInstance(Id, null, level));

    [Test]
    public void ArmorLevel_TakesPartInEquality()
    {
        Assert.That(new EquipmentInstance(Id, null, 1), Is.EqualTo(new EquipmentInstance(Id, null, 1)));
        Assert.That(new EquipmentInstance(Id, null, 1), Is.Not.EqualTo(new EquipmentInstance(Id, null, 2)));
    }

    [Test]
    public void LocalCache_RoundTripsArmorUpgradeLevel()
    {
        var profile = new ProfileId("49494949494949494949494949494949");
        var snapshot = new LocalProfileSnapshot { ProfileId = profile };
        snapshot.Loadout.Add(new StashItem(new LootId(Armor), 1, 0, new EquipmentInstance(Id, null, 2), string.Empty, string.Empty));

        string json = LocalProfileSaveCodec.Encode(snapshot);
        Assert.That(LocalProfileSaveCodec.TryDecode(json, profile, Catalog, out var decoded, out _, out string error), Is.True, error);
        Assert.That(decoded.Loadout[0].Instance.ArmorUpgradeLevel, Is.EqualTo(2));
        Assert.That(decoded.Loadout[0].Instance.InstanceId, Is.EqualTo(Id));
    }

    [Test]
    public void PublishBody_SendsArmorModifiersAsUpgradeLevelOnly()
    {
        var request = new PublishExtractionResultRequest
        {
            raidId = "raid", resultId = "raid:profile:1", resultSequence = 1, targetCharacterId = "profile",
            items = new[]
            {
                new InventoryItemData { lootId = Armor, amount = 1, instance = EquipmentInstanceData.FromInstance(new EquipmentInstance(null, null, 1)) }
            },
            preparedEquipment = PreparedEquipmentData.FromLoadout(default),
            experienceGranted = 0, issuedAt = "now", hostSignature = ""
        };

        JObject body = JObject.Parse(InventoryRequestJson.Encode(request));
        Assert.That(body["items"][0]["modifiers"].ToString(Formatting.None), Is.EqualTo("{\"upgradeLevel\":1}"));
    }

    private const string Attributes = "\"characterAttributes\":{\"vitality\":5,\"resistance\":5,\"strength\":5,\"dexterity\":5,\"intelligence\":5,\"luck\":5,\"availablePoints\":0},\"preparedAbilities\":{}";

    [Test]
    public void RaidAdmission_AcceptsUpgradedArmor_WithoutCarryingTheLevel()
    {
        string json = "{\"level\":1,\"experience\":0," + Attributes + ",\"items\":[{\"lootId\":\"" + Armor +
            "\",\"amount\":1,\"modifiers\":{\"upgradeLevel\":2}}],\"preparedEquipment\":{\"armor\":{\"lootId\":\"" + Armor +
            "\",\"modifiers\":{\"upgradeLevel\":1}}}}";
        var dto = JsonConvert.DeserializeObject<AuthoritativeRaidAdmissionSnapshotDto>(json);

        Assert.That(RaidAdmissionSnapshotMapper.TryProject(dto, out _, out _, out var items, out var equipment), Is.True);
        Assert.That(items[0].LootId.Value, Is.EqualTo(Armor));
        Assert.That(items[0].Modifiers, Is.Null);
        Assert.That(equipment.Get(EquipmentSlot.Armor).Value, Is.EqualTo(Armor));
    }

    [Test]
    public void RaidAdmission_RejectsInvalidArmorLevel()
    {
        string json = "{\"level\":1,\"experience\":0," + Attributes + ",\"items\":[{\"lootId\":\"" + Armor +
            "\",\"amount\":1,\"modifiers\":{\"upgradeLevel\":7}}],\"preparedEquipment\":{}}";
        var dto = JsonConvert.DeserializeObject<AuthoritativeRaidAdmissionSnapshotDto>(json);
        Assert.That(RaidAdmissionSnapshotMapper.TryProject(dto, out _, out _, out _, out _), Is.False);
    }
}
#endif

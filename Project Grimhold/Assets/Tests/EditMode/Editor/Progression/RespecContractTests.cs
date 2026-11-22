#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Linq;
using Grimhold.Backend;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Progression
{
    /// <summary>TASK-304 T3 (S1, S6): the Unity DTOs match the backend's respec and progression bodies.</summary>
    public sealed class RespecContractTests
    {
        private const string RespecBody = @"{
            ""alreadySecured"": true,
            ""transactionId"": ""1700000000000_0123456789abcdef0123456789abcdef"",
            ""cost"": 100,
            ""currency"": 150,
            ""characterAttributes"": { ""vitality"": 0, ""resistance"": 0, ""strength"": 0, ""dexterity"": 0, ""intelligence"": 0, ""luck"": 0, ""availablePoints"": 33 },
            ""level"": 4,
            ""experience"": 820,
            ""revision"": 12,
            ""preparedEquipment"": {
                ""weaponSlot1"": """", ""weaponSlot2"": """", ""helmet"": """", ""armor"": ""heavy_armor_breastplate"",
                ""gloves"": """", ""boots"": """", ""offHand1"": """", ""offHand2"": """"
            },
            ""preparedAbilities"": { ""slot1"": """", ""slot2"": ""guard"" },
            ""stash"": [ { ""slot"": 19, ""lootId"": ""RES_RUNE_FRAGMENT"", ""amount"": 5 } ],
            ""loadout"": [ { ""slot"": 4, ""lootId"": ""rapier"", ""amount"": 1, ""instance"": { ""instanceId"": ""223456789012345678901234"" } } ]
        }";

        [Test]
        public void ParseRespecResult_ReadsEveryFieldOfTheBackendBody()
        {
            Assert.That(ProgressionClient.TryParseRespecResult(RespecBody, out RespecResult result), Is.True);

            Assert.That(result.alreadySecured, Is.True);
            Assert.That(result.transactionId, Is.EqualTo("1700000000000_0123456789abcdef0123456789abcdef"));
            Assert.That(result.cost, Is.EqualTo(100));
            Assert.That(result.currency, Is.EqualTo(150));
            Assert.That(result.characterAttributes.availablePoints, Is.EqualTo(33));
            Assert.That(result.characterAttributes.strength, Is.Zero);
            Assert.That(result.level, Is.EqualTo(4));
            Assert.That(result.experience, Is.EqualTo(820));
            Assert.That(result.revision, Is.EqualTo(12));
            Assert.That(result.preparedEquipment.armor, Is.EqualTo("heavy_armor_breastplate"));
            Assert.That(result.preparedAbilities.slot1, Is.Empty);
            Assert.That(result.preparedAbilities.slot2, Is.EqualTo("guard"));
            Assert.That(result.stash.Single().lootId, Is.EqualTo("RES_RUNE_FRAGMENT"));
            Assert.That(result.loadout.Single().instance.instanceId, Is.EqualTo("223456789012345678901234"));
        }

        [Test]
        public void ParseRespecResult_RejectsAnUnreadableBody()
        {
            Assert.That(ProgressionClient.TryParseRespecResult("not json", out _), Is.False);
            Assert.That(ProgressionClient.TryParseRespecResult("", out _), Is.False);
            Assert.That(ProgressionClient.TryParseRespecResult(null, out _), Is.False);
        }

        [Test]
        public void RespecRequest_SerializesTheContractFieldNames()
        {
            var json = JObject.Parse(JsonUtility.ToJson(new RespecRequest { transactionId = "5_abc", expectedRevision = 9 }));

            Assert.That(json.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "transactionId", "expectedRevision" }));
            Assert.That((string)json["transactionId"], Is.EqualTo("5_abc"));
            Assert.That((int)json["expectedRevision"], Is.EqualTo(9));
        }

        [Test]
        public void ProgressionData_ReadsTheServerRespecCost_AndStillReadsTheRest()
        {
            ProgressionData data = JsonUtility.FromJson<ProgressionData>(@"{
                ""level"": 3, ""experience"": 40, ""lastAppliedProgressionResultSequence"": 2,
                ""characterAttributes"": { ""vitality"": 6, ""resistance"": 5, ""strength"": 5, ""dexterity"": 5, ""intelligence"": 5, ""luck"": 5, ""availablePoints"": 1 },
                ""unlockedAbilities"": [""charge""], ""respecCost"": 175, ""revision"": 4 }");

            Assert.That(data.respecCost, Is.EqualTo(175));
            Assert.That(data.level, Is.EqualTo(3));
            Assert.That(data.characterAttributes.vitality, Is.EqualTo(6));
            Assert.That(data.unlockedAbilities, Is.EqualTo(new[] { "charge" }));
            Assert.That(data.revision, Is.EqualTo(4));
        }
    }
}
#endif

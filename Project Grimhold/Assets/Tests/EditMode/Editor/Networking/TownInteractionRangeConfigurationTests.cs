#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Town interactions need the player to stand next to the NPC: the NPC's interaction area is about one body wide
/// and the Town avatar's reach is short. The Raid keeps its own, longer reach.
/// </summary>
public sealed class TownInteractionRangeConfigurationTests
{
    private const string TownConfigPath = "Assets/Scriptable Objects/TownPlayerInteractionConfig.asset";
    private const string RaidConfigPath = "Assets/Scriptable Objects/PlayerInteractionConfig.asset";
    private const string SocialPlayerPath = "Assets/Prefabs/SocialPlayer.prefab";
    private const string RaidPlayerPath = "Assets/Prefabs/NetworkPlayer.prefab";

    // A Town character's artwork is 1.25 world units wide.
    private const float BodyWidth = 1.25f;

    [TestCase("Assets/Prefabs/TownAbilityVendorNPC.prefab")]
    [TestCase("Assets/Prefabs/TownMerchantNPC.prefab")]
    [TestCase("Assets/Prefabs/TownMissionNPC.prefab")]
    [TestCase("Assets/Prefabs/TownRaidNpc.prefab")]
    [TestCase("Assets/Prefabs/TownRespecNpc.prefab")]
    [TestCase("Assets/Prefabs/TownStashNpc.prefab")]
    [TestCase("Assets/Prefabs/TownTrainerNpc.prefab")]
    public void TownNpc_InteractionAreaIsAtMostOneBodyWide(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(prefab, Is.Not.Null, prefabPath);

        CircleCollider2D area = prefab.GetComponent<CircleCollider2D>();

        Assert.That(area, Is.Not.Null, $"{prefab.name} interaction area");
        Assert.That(area.isTrigger, Is.True, "The interaction area never blocks movement.");
        Assert.That(area.radius * 2f, Is.GreaterThan(0f).And.LessThanOrEqualTo(BodyWidth));
    }

    [Test]
    public void TownAvatar_ReachesLessThanOneBodyBeyondTheInteractionArea()
    {
        PlayerInteractionConfig townConfig = AssetDatabase.LoadAssetAtPath<PlayerInteractionConfig>(TownConfigPath);
        PlayerInteractionConfig raidConfig = AssetDatabase.LoadAssetAtPath<PlayerInteractionConfig>(RaidConfigPath);
        Assert.That(townConfig, Is.Not.Null, TownConfigPath);
        Assert.That(raidConfig, Is.Not.Null, RaidConfigPath);

        Assert.That(townConfig.MaximumDistance, Is.GreaterThan(0f).And.LessThan(BodyWidth));
        Assert.That(townConfig.TargetLayerMask.value, Is.EqualTo(raidConfig.TargetLayerMask.value));
    }

    [Test]
    public void TownAvatar_UsesTheTownReachForPredictionAndAuthority()
    {
        PlayerInteractionConfig townConfig = AssetDatabase.LoadAssetAtPath<PlayerInteractionConfig>(TownConfigPath);
        Assert.That(townConfig, Is.Not.Null, TownConfigPath);

        List<PlayerInteractionConfig> configs = CollectInteractionConfigs(SocialPlayerPath);

        // The local prompt and the authoritative controller must agree, or the prompt would lie about range.
        Assert.That(configs, Is.Not.Empty);
        Assert.That(configs, Is.All.SameAs(townConfig));
    }

    [Test]
    public void RaidAvatar_KeepsItsOwnReach()
    {
        PlayerInteractionConfig raidConfig = AssetDatabase.LoadAssetAtPath<PlayerInteractionConfig>(RaidConfigPath);

        List<PlayerInteractionConfig> configs = CollectInteractionConfigs(RaidPlayerPath);

        Assert.That(configs, Is.Not.Empty);
        Assert.That(configs, Is.All.SameAs(raidConfig));
        Assert.That(raidConfig.MaximumDistance, Is.EqualTo(2f));
    }

    private static List<PlayerInteractionConfig> CollectInteractionConfigs(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(prefab, Is.Not.Null, prefabPath);

        var configs = new List<PlayerInteractionConfig>();
        foreach (MonoBehaviour behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
            {
                continue;
            }

            SerializedProperty property = new SerializedObject(behaviour).GetIterator();
            while (property.NextVisible(true))
            {
                if (property.propertyType == SerializedPropertyType.ObjectReference &&
                    property.objectReferenceValue is PlayerInteractionConfig config)
                {
                    configs.Add(config);
                }
            }
        }

        return configs;
    }
}
#endif

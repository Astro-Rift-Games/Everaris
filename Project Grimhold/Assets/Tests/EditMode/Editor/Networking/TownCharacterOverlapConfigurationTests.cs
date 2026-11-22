#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Town characters must not blend into each other: every character sorts as one unit (so two characters never
/// interleave their body parts) and the Town avatar cannot walk through an NPC.
/// </summary>
public sealed class TownCharacterOverlapConfigurationTests
{
    private const string SocialPlayerPath = "Assets/Prefabs/SocialPlayer.prefab";
    private const string RaidPlayerPath = "Assets/Prefabs/NetworkPlayer.prefab";
    private const string BlockerName = "MovementBlocker";

    private const string TownConfigPath = "Assets/Scriptable Objects/TownPlayerInteractionConfig.asset";


    private static readonly string[] TownNpcPaths =
    {
        "Assets/Prefabs/TownAbilityVendorNPC.prefab",
        "Assets/Prefabs/TownMerchantNPC.prefab",
        "Assets/Prefabs/TownMissionNPC.prefab",
        "Assets/Prefabs/TownRaidNpc.prefab",
        "Assets/Prefabs/TownRespecNpc.prefab",
        "Assets/Prefabs/TownStashNpc.prefab",
        "Assets/Prefabs/TownTrainerNpc.prefab"
    };

    private static readonly string[] TownCharacterPaths =
    {
        SocialPlayerPath,
        "Assets/Prefabs/TownAbilityVendorNPC.prefab",
        "Assets/Prefabs/TownMerchantNPC.prefab",
        "Assets/Prefabs/TownMissionNPC.prefab",
        "Assets/Prefabs/TownRaidNpc.prefab",
        "Assets/Prefabs/TownRespecNpc.prefab",
        "Assets/Prefabs/TownStashNpc.prefab",
        "Assets/Prefabs/TownTrainerNpc.prefab"
    };

    [TestCaseSource(nameof(TownCharacterPaths))]
    public void TownCharacter_SortsAsOneUnitLikeTheRaidAvatar(string prefabPath)
    {
        GameObject prefab = Load(prefabPath);
        SortingGroup raidGroup = Load(RaidPlayerPath).GetComponent<SortingGroup>();
        Assert.That(raidGroup, Is.Not.Null, "Raid avatar reference sorting group");

        SortingGroup group = prefab.GetComponent<SortingGroup>();

        Assert.That(group, Is.Not.Null, $"{prefab.name} needs a root {nameof(SortingGroup)}.");
        Assert.That(group.enabled, Is.True);
        Assert.That(group.sortingLayerID, Is.EqualTo(raidGroup.sortingLayerID));
        Assert.That(group.sortingOrder, Is.EqualTo(raidGroup.sortingOrder));
    }

    [TestCaseSource(nameof(TownNpcPaths))]
    public void TownNpc_BlocksTheTownAvatarAtItsFeet(string prefabPath)
    {
        GameObject prefab = Load(prefabPath);
        int avatarCollisionMask = new SerializedObject(Load(SocialPlayerPath).GetComponent<Kinematic2DMovementMotor>())
            .FindProperty("_collisionMask").FindPropertyRelative("m_Bits").intValue;

        Transform blocker = prefab.transform.Find(BlockerName);
        Assert.That(blocker, Is.Not.Null, $"{prefab.name} needs a '{BlockerName}' child.");
        BoxCollider2D box = blocker.GetComponent<BoxCollider2D>();
        Assert.That(box, Is.Not.Null, $"{prefab.name} {BlockerName} collider");

        Assert.That(box.isTrigger, Is.False, "A blocker is solid.");
        Assert.That(
            avatarCollisionMask & (1 << blocker.gameObject.layer),
            Is.Not.Zero,
            "The blocker must sit on a layer the Town avatar's movement motor collides with.");

        Vector2 centre = (Vector2)blocker.localPosition + box.offset;
        Assert.That(centre.x, Is.EqualTo(0f).Within(0.0001f));
    }

    [TestCaseSource(nameof(TownNpcPaths))]
    public void TownAvatar_StillReachesTheNpcFromTheFrontAndTheSides(string prefabPath)
    {
        BoxCollider2D avatar = AvatarFootprint();
        GameObject npc = Load(prefabPath);
        BoxCollider2D box = BlockerOf(npc);
        CircleCollider2D interactionArea = npc.GetComponent<CircleCollider2D>();
        float reach = AssetDatabase.LoadAssetAtPath<PlayerInteractionConfig>(TownConfigPath).MaximumDistance;
        Vector2 centre = (Vector2)box.transform.localPosition + box.offset;

        // Closest avatar origins the blocker allows, relative to the NPC root.
        float side = (avatar.size.x + box.size.x) * 0.5f;
        float inFront = (centre.y - (box.size.y * 0.5f)) - (avatar.offset.y + (avatar.size.y * 0.5f));
        // Standing behind an NPC is deliberately left out: the blocker covers its body, so the player talks to
        // an NPC from where it can face them.
        Vector2[] closestStandingPoints =
        {
            new(side, 0f), new(-side, 0f), new(0f, inFront)
        };

        foreach (Vector2 point in closestStandingPoints)
        {
            float distanceToArea = Mathf.Max(0f, (point - interactionArea.offset).magnitude - interactionArea.radius);
            Assert.That(distanceToArea, Is.LessThan(reach - 0.05f), $"standing at {point}");
        }
    }

    private static BoxCollider2D AvatarFootprint()
    {
        BoxCollider2D avatar = Load(SocialPlayerPath).GetComponent<BoxCollider2D>();
        Assert.That(avatar, Is.Not.Null, "Town avatar footprint");
        Assert.That(avatar.isTrigger, Is.False);
        return avatar;
    }

    private static BoxCollider2D BlockerOf(GameObject npc)
    {
        Transform blocker = npc.transform.Find(BlockerName);
        Assert.That(blocker, Is.Not.Null, $"{npc.name} needs a '{BlockerName}' child.");
        return blocker.GetComponent<BoxCollider2D>();
    }

    [TestCaseSource(nameof(TownNpcPaths))]
    public void TownNpc_KeepsItsInteractionAreaSeparateFromTheBlocker(string prefabPath)
    {
        GameObject prefab = Load(prefabPath);

        CircleCollider2D interactionArea = prefab.GetComponent<CircleCollider2D>();

        Assert.That(interactionArea, Is.Not.Null);
        Assert.That(interactionArea.isTrigger, Is.True);
        Assert.That(prefab.layer, Is.EqualTo(LayerMask.NameToLayer("Interactable")));
    }

    private static GameObject Load(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null, path);
        return prefab;
    }
}
#endif

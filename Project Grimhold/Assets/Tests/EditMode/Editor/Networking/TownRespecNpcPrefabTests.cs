#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Linq;
using Fusion;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// TASK-304 T7 (S10-S13): the Mentor, a dedicated interactable Town NPC that hosts the attribute respec, its
/// presenter on the Social player and its single instance in the Town scene.
/// </summary>
public sealed class TownRespecNpcPrefabTests
{
    private const string PrefabPath = "Assets/Prefabs/TownRespecNpc.prefab";
    private const string PanelPath = "Assets/Prefabs/TownRespecPanel.prefab";
    private const string SocialPlayerPath = "Assets/Prefabs/SocialPlayer.prefab";
    private const string RaidParticipantPath = "Assets/Prefabs/NetworkRaidParticipant.prefab";
    private const string TownScenePath = "Assets/Scenes/Lobby-Town.unity";
    private const string ControllerPath = "Assets/Animations/NPCs/Shared/TownNpcIdle.controller";
    private static readonly string[] Parts = { "Legs", "Body", "Head", "LeftHand", "RightHand" };

    [Test]
    public void Prefab_HasNetworkObjectInteractablePromptAndDialogue()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null, "TownRespecNpc.prefab must exist in Assets/Prefabs/");

        NetworkObject networkObject = prefab.GetComponent<NetworkObject>();
        Assert.That(networkObject, Is.Not.Null);
        Assert.That(networkObject.Flags.HasFlag(NetworkObjectFlags.MasterClientObject), Is.True,
            "a scene NPC follows the master client like the Trainer, Stash and Raid NPCs");
        Assert.That(networkObject.NetworkedBehaviours.OfType<TownRespecNpcInteractable>().Count(), Is.EqualTo(1));
        TownRespecNpcInteractable interactable = prefab.GetComponent<TownRespecNpcInteractable>();
        Assert.That(interactable, Is.Not.Null);
        Assert.That(interactable, Is.InstanceOf<IInteractable>());
        Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true).OfType<IInteractable>().Count(), Is.EqualTo(1));
        Assert.That(prefab.GetComponent<DialogueInteractable>(), Is.Null);
        Assert.That(prefab.layer, Is.EqualTo(8), "the same interaction layer as every Town NPC");

        InteractionPromptMetadata prompt = prefab.GetComponent<InteractionPromptMetadata>();
        Assert.That(prompt, Is.Not.Null);
        Assert.That(prompt.PromptText, Is.EqualTo("Hablar con el Mentor"));

        CircleCollider2D trigger = prefab.GetComponent<CircleCollider2D>();
        Assert.That(trigger, Is.Not.Null);
        Assert.That(trigger.isTrigger, Is.True);
        Assert.That(trigger.radius, Is.EqualTo(0.6f), "about one body wide, like every Town NPC");
    }

    [Test]
    public void Prefab_DialogueIsOneShortSpanishLineFromTheMentor()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        TownNpcDialogueTrigger trigger = prefab.GetComponent<TownNpcDialogueTrigger>();

        Assert.That(trigger, Is.Not.Null);
        Assert.That(trigger.PrimarySequence, Is.Not.Null);
        Assert.That(trigger.PrimarySequence.name, Is.EqualTo("TownRespecNpcDialogue"));
        Assert.That(trigger.PrimarySequence.Lines, Has.Length.EqualTo(1));
        Assert.That(trigger.PrimarySequence.Lines[0].SpeakerName, Is.EqualTo("Mentor"));
        Assert.That(trigger.PrimarySequence.Lines[0].Text,
            Is.EqualTo("¿Querés empezar de nuevo? Puedo devolverte todos tus puntos de atributo… por un precio."));
        Assert.That(trigger.PrimarySequence.Lines[0].AutoAdvance, Is.False);
        Assert.That(trigger.SecondarySequence, Is.Null);
        Assert.That(trigger.LockedSequence, Is.Null, "a Ready player gets the panel's own explanation");
    }

    [Test]
    public void Prefab_IsThePlayersModularBaseBodyOnTheSharedIdleControllerWithNoNpcExclusiveSet()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        TownNpcDirectionalView view = prefab.GetComponent<TownNpcDirectionalView>();
        Transform root = prefab.transform.Find("VisualRoot");
        Assert.That(view, Is.Not.Null);
        Assert.That(root, Is.Not.Null);
        Assert.That(view.InitialFacing, Is.EqualTo(CharacterVisualDirection.South));
        Assert.That(root.localPosition, Is.EqualTo(new Vector3(0f, 0.75f, 0f)));

        Animator animator = root.GetComponent<Animator>();
        Assert.That(animator, Is.Not.Null);
        Assert.That(prefab.GetComponentsInChildren<Animator>(true), Has.Length.EqualTo(1));
        Assert.That(AssetDatabase.GetAssetPath(animator.runtimeAnimatorController), Is.EqualTo(ControllerPath));
        Assert.That(new SerializedObject(view).FindProperty("_animator").objectReferenceValue, Is.SameAs(animator));

        Assert.That(root.Cast<Transform>().Select(child => child.name), Is.EquivalentTo(Parts));
        SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        Assert.That(renderers, Has.Length.EqualTo(5), "the five base parts only, no exclusive layer");
        AnimationClip southIdle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Player/Idle/Idle_S.anim");
        foreach (string part in Parts)
        {
            SpriteRenderer renderer = root.Find(part).GetComponent<SpriteRenderer>();
            Assert.That(renderer.sprite, Is.Not.Null, part);
            var binding = new EditorCurveBinding { path = part, type = typeof(SpriteRenderer), propertyName = "m_Sprite" };
            Assert.That(renderer.sprite, Is.SameAs(AnimationUtility.GetObjectReferenceCurve(southIdle, binding)[0].value as Sprite), part);
            Assert.That(renderer.sortingLayerName, Is.EqualTo("Characters"));
        }

        string[] exclusiveSets = { "Blacksmith-Set", "DungeonMaster-Set", "Merchant-Set", "Merchanr-Set" };
        Assert.That(renderers.Select(renderer => renderer.sprite.texture.name).Intersect(exclusiveSets), Is.Empty);
    }

    [Test]
    public void SocialPlayer_CarriesOnePresenterWithSerializedDependenciesAndTheAttributeGateSource()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(SocialPlayerPath);
        Assert.That(player, Is.Not.Null);

        TownRespecNpcPresenter[] presenters = player.GetComponentsInChildren<TownRespecNpcPresenter>(true);
        Assert.That(presenters, Has.Length.EqualTo(1), "a serialized presenter, never added at runtime");
        var serialized = new SerializedObject(presenters[0]);
        var panel = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
        Assert.That(panel, Is.Not.Null);
        Assert.That(serialized.FindProperty("_viewPrefab").objectReferenceValue, Is.SameAs(panel.GetComponent<TownRespecPanelView>()));
        Assert.That(serialized.FindProperty("_interactionController").objectReferenceValue,
            Is.SameAs(player.GetComponent<PlayerInteractionNetworkController>()));
        Assert.That(serialized.FindProperty("_dialoguePresenter").objectReferenceValue,
            Is.SameAs(player.GetComponent<DialoguePresenter>()));
        Assert.That(serialized.FindProperty("_attributePresenter").objectReferenceValue,
            Is.SameAs(player.GetComponentInChildren<TownAttributeAssignmentPresenter>(true)),
            "the respec shares the attribute presenter's Ready and busy gates");
    }

    [Test]
    public void RaidParticipant_DoesNotCarryTheMentorPresenter()
    {
        GameObject raid = AssetDatabase.LoadAssetAtPath<GameObject>(RaidParticipantPath);
        Assert.That(raid, Is.Not.Null);
        Assert.That(raid.GetComponentsInChildren<TownRespecNpcPresenter>(true), Is.Empty);
    }

    [Test]
    public void TownScene_HasExactlyOneMentorThatClearsTheWorldAndTheOtherNpcs()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);
        Scene scene = SceneManager.GetSceneByPath(TownScenePath);
        bool openedForTest = !scene.IsValid() || !scene.isLoaded;
        if (openedForTest)
        {
            scene = EditorSceneManager.OpenScene(TownScenePath, OpenSceneMode.Additive);
        }

        try
        {
            var mentors = new List<TownRespecNpcInteractable>();
            var others = new List<NetworkObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                mentors.AddRange(root.GetComponentsInChildren<TownRespecNpcInteractable>(true));
                others.AddRange(root.GetComponentsInChildren<NetworkObject>(true)
                    .Where(networkObject => networkObject.GetComponent<TownRespecNpcInteractable>() == null));
            }

            Assert.That(mentors, Has.Count.EqualTo(1));
            GameObject mentor = mentors[0].gameObject;
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(mentor), Is.SameAs(prefab));
            Assert.That(PrefabUtility.GetPrefabInstanceStatus(mentor), Is.EqualTo(PrefabInstanceStatus.Connected));

            foreach (NetworkObject other in others.Where(candidate => candidate.GetComponent<IInteractable>() != null))
            {
                float distance = Vector2.Distance(mentor.transform.position, other.transform.position);
                Assert.That(distance, Is.GreaterThanOrEqualTo(2f),
                    $"the Mentor's interaction circle overlaps {other.name}");
            }

            // The Mentor's body (a 0.5 radius footprint at its feet) must stand in free space.
            var probe = new GameObject("Mentor footprint probe");
            try
            {
                probe.transform.position = mentor.transform.position;
                CircleCollider2D footprint = probe.AddComponent<CircleCollider2D>();
                footprint.radius = 0.5f;
                Physics2D.SyncTransforms();
                foreach (Collider2D solid in scene.GetRootGameObjects()
                             .SelectMany(root => root.GetComponentsInChildren<Collider2D>(true))
                             // The Mentor's own solid footprint (its MovementBlocker child) is not an obstacle to itself.
                             .Where(collider => !collider.isTrigger && collider.enabled &&
                                                !collider.transform.IsChildOf(mentor.transform)))
                {
                    Assert.That(footprint.Distance(solid).isOverlapped, Is.False,
                        $"the Mentor stands inside the solid '{solid.name}'");
                }
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }
        finally
        {
            if (openedForTest)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
#endif

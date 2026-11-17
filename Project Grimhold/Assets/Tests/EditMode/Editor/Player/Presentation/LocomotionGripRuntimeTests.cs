using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// The held weapon hangs from MainHandGrip, which the body clips key while the RightHand layer draws the hand. On
// the evaluated Animator, with a weapon's attack clips overriding the placeholders as the view does, the grip must
// keep the drawn hand's place on every frame of Walk and Sprint.
public sealed class LocomotionGripRuntimeTests
{
    private const string PrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";
    private const string ControllerPath = "Assets/Animations/Player/Character.controller";
    private const string DefinitionsRoot = "Assets/Scriptable Objects/Loot/Definitions/";
    private const string Hand = "RightHandPivot/RightHand";
    private const string Grip = Hand + "/MainHandGrip";
    // Off the 12 fps key grid, so no sample lands on a key the body and hand clips round differently.
    private const float Step = 0.0137f;
    private static readonly string[] Directions = { "N", "NE", "NW", "S", "SE", "SW" };

    [Test]
    public void MainHandGrip_FollowsTheDrawnRightHand_WithoutWeapon(
        [Values("Walk", "Sprint")] string motion, [Values(0, 1, 2, 3, 4, 5)] int direction)
    {
        AssertGripFollowsHand(null, motion, direction);
    }

    [Test]
    public void MainHandGrip_FollowsTheDrawnRightHand_WithWeaponAttackOverrides(
        [Values("ArmingSwordWeaponDefinition", "MagicWandWeaponDefinition")] string weapon,
        [Values("Walk", "Sprint")] string motion, [Values(0, 1, 2, 3, 4, 5)] int direction)
    {
        AssertGripFollowsHand(weapon, motion, direction);
    }

    // The view seeks the RightHand layer alone through an attack and hands it back to its Idle state when the attack
    // ends, while the Base Layer keeps walking: the hand must resume the body's cycle, not restart its own.
    [Test]
    public void MainHandGrip_FollowsTheDrawnRightHand_AfterAnAttackEndsMidCycle(
        [Values("Walk", "Sprint")] string motion, [Values(0, 3, 4)] int direction)
    {
        AssertGripFollowsHand("ArmingSwordWeaponDefinition", motion, direction, attackMidCycle: true);
    }

    private static void AssertGripFollowsHand(string weaponName, string motion, int direction, bool attackMidCycle = false)
    {
        string facing = Directions[direction];
        Vector2 expected = AuthoredOffset(motion, facing);
        var anchors = new Dictionary<Sprite, Vector2>();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject player = null;
        AnimatorOverrideController overrides = null;
        try
        {
            player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            Animator animator = player.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);
            if (weaponName != null)
            {
                var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(DefinitionsRoot + weaponName + ".asset");
                Assert.That(weapon, Is.Not.Null, weaponName);
                Assert.That(weapon.Presentation.HasGenericAttack, Is.True, weaponName);
                overrides = new AnimatorOverrideController(AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath));
                for (int i = 0; i < Directions.Length; i++)
                    overrides[AssetDatabase.LoadAssetAtPath<AnimationClip>(
                        $"Assets/Animations/Player/Attack/GenericAttack_{Directions[i]}.anim")] = weapon.Presentation.GetAttackClip(i);
                animator.runtimeAnimatorController = overrides;
                animator.SetBool("HasGenericAttack", true);
            }

            Transform grip = animator.transform.Find(Grip);
            SpriteRenderer hand = animator.transform.Find(Hand).GetComponent<SpriteRenderer>();
            Vector2 move = PlayerWeaponPresentationMath.CanonicalFacing(direction);
            animator.SetFloat("MoveX", move.x);
            animator.SetFloat("MoveY", move.y);
            animator.SetFloat("LocomotionPlaybackRate", 1f);
            animator.SetFloat("SprintBlend", motion == "Sprint" ? 1f : 0f);
            animator.SetBool("IsMoving", true);
            animator.Update(0f);

            if (attackMidCycle)
            {
                int layer = animator.GetLayerIndex("RightHand");
                for (int step = 0; step < 17; step++) animator.Update(Step);
                for (float seconds = 0f; seconds < 0.2f; seconds += Step)
                {
                    animator.Play(Animator.StringToHash("RightHand.Attack"), layer, seconds / 0.9f);
                    animator.Update(Step);
                }
                // What PlayerAnimatorView does when the presented attack ends.
                PlayerAnimatorView.ResumeMainHandLocomotion(animator, layer);
            }

            for (int step = 0; step < 90; step++)
            {
                animator.Update(Step);
                Sprite sprite = hand.sprite;
                Assert.That(sprite, Is.Not.Null, $"step {step}");
                if (!anchors.TryGetValue(sprite, out Vector2 anchor))
                    anchors[sprite] = anchor = DirectionalAnimationGenerator.ResolveSpriteAnchor(sprite);
                Vector2 offset = (Vector2)grip.localPosition - anchor;
                Assert.That(Vector2.Distance(offset, expected), Is.LessThan(0.001f),
                    $"{weaponName ?? "Unarmed"} {motion}_{facing} step {step}: grip {grip.localPosition} on {sprite.name}" +
                    $" ({LayerPhase(animator, 0)} / {LayerPhase(animator, animator.GetLayerIndex("RightHand"))})");
            }
        }
        finally
        {
            if (player != null) Object.DestroyImmediate(player);
            if (overrides != null) Object.DestroyImmediate(overrides);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    private static string LayerPhase(Animator animator, int layer)
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
        var clips = new List<AnimatorClipInfo>();
        animator.GetCurrentAnimatorClipInfo(layer, clips);
        string names = string.Join(",", clips.ConvertAll(c => $"{c.clip.name}:{c.weight:F2}"));
        return $"{animator.GetLayerName(layer)} t={state.normalizedTime:F3} len={state.length:F3} [{names}]";
    }

    // The grip key minus the drawn right hand's anchor at the clip's first frame.
    private static Vector2 AuthoredOffset(string motion, string facing)
    {
        var body = AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/Animations/Player/{motion}/{motion}_{facing}.anim");
        var hand = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            $"Assets/Animations/Player/{motion}/RightHand/RightHand_{motion}_{facing}.anim");
        Assert.That(body, Is.Not.Null);
        Assert.That(hand, Is.Not.Null);
        ObjectReferenceKeyframe[] frames = AnimationUtility.GetObjectReferenceCurve(hand,
            EditorCurveBinding.PPtrCurve(Hand, typeof(SpriteRenderer), "m_Sprite"));
        AnimationCurve x = AnimationUtility.GetEditorCurve(body, EditorCurveBinding.FloatCurve(Grip, typeof(Transform), "m_LocalPosition.x"));
        AnimationCurve y = AnimationUtility.GetEditorCurve(body, EditorCurveBinding.FloatCurve(Grip, typeof(Transform), "m_LocalPosition.y"));
        Vector2 anchor = DirectionalAnimationGenerator.ResolveSpriteAnchor((Sprite)frames[0].value);
        return new Vector2(x.Evaluate(0.001f), y.Evaluate(0.001f)) - anchor;
    }
}

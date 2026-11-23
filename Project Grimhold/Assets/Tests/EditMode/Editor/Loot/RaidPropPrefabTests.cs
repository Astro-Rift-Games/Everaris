#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using ProjectGrimhold.Gameplay.Visibility;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Loot
{
    /// <summary>
    /// Raid props are part of the world: chests and breakables block movement. The base breakable is the
    /// crate, which vanishes when broken; the catacombs jar is its variant and leaves broken remains behind.
    /// </summary>
    public sealed class RaidPropPrefabTests
    {
        private const string RaidPlayerPath = "Assets/Prefabs/NetworkPlayer.prefab";
        private const string BreakablePath = "Assets/Prefabs/BreakableObject.prefab";
        private const string JarPath = "Assets/Prefabs/JarBreakable.prefab";
        private const string BlockerName = "WorldBlocker";

        [TestCase("Assets/Prefabs/LootContainer.prefab")]
        [TestCase("Assets/Prefabs/LockedChest.prefab")]
        [TestCase(BreakablePath)]
        [TestCase(JarPath)]
        public void Prop_BlocksMovementWithinItsArtwork(string prefabPath)
        {
            GameObject chest = Load(prefabPath);
            int avatarCollisionMask = new SerializedObject(Load(RaidPlayerPath).GetComponent<Kinematic2DMovementMotor>())
                .FindProperty("_collisionMask").FindPropertyRelative("m_Bits").intValue;

            Transform blocker = chest.transform.Find(BlockerName);
            Assert.That(blocker, Is.Not.Null, $"{chest.name} needs a '{BlockerName}' child, like the breakable.");
            BoxCollider2D box = blocker.GetComponent<BoxCollider2D>();
            Assert.That(box, Is.Not.Null);
            Assert.That(box.isTrigger, Is.False, "A blocker is solid.");
            Assert.That(
                avatarCollisionMask & (1 << blocker.gameObject.layer),
                Is.Not.Zero,
                "The blocker must sit on a layer the Raid avatar's movement motor collides with.");

            Rect artwork = ArtworkRect(chest);
            Vector2 centre = (Vector2)blocker.localPosition + box.offset;
            var footprint = new Rect(centre - (box.size * 0.5f), box.size);
            const float tolerance = 0.05f;
            Assert.That(footprint.xMin, Is.GreaterThanOrEqualTo(artwork.xMin - tolerance), "left");
            Assert.That(footprint.xMax, Is.LessThanOrEqualTo(artwork.xMax + tolerance), "right");
            Assert.That(footprint.yMin, Is.GreaterThanOrEqualTo(artwork.yMin - tolerance), "bottom");
            Assert.That(footprint.yMax, Is.LessThanOrEqualTo(artwork.yMax + tolerance), "top");
            Assert.That(footprint.width, Is.GreaterThanOrEqualTo(artwork.width * 0.6f), "covers most of the chest width");
        }

        [TestCase("Assets/Prefabs/LootContainer.prefab")]
        [TestCase("Assets/Prefabs/LockedChest.prefab")]
        public void Chest_KeepsItsInteractionTriggerOnTheRoot(string prefabPath)
        {
            GameObject chest = Load(prefabPath);

            BoxCollider2D trigger = chest.GetComponent<BoxCollider2D>();

            Assert.That(trigger, Is.Not.Null);
            Assert.That(trigger.isTrigger, Is.True);
            Assert.That(chest.layer, Is.EqualTo(LayerMask.NameToLayer("Interactable")));
        }

        [Test]
        public void Breakable_IsTheCrate_WhichLeavesNoRemains()
        {
            GameObject crate = Load(BreakablePath);
            var serialized = new SerializedObject(crate.GetComponent<BreakableObject>());
            SerializedProperty intact = serialized.FindProperty("_intactRenderers");
            SerializedProperty destroyed = serialized.FindProperty("_destroyedRenderers");

            Assert.That(intact.arraySize, Is.EqualTo(1));
            var intactRenderer = intact.GetArrayElementAtIndex(0).objectReferenceValue as SpriteRenderer;
            Assert.That(intactRenderer, Is.Not.Null);
            Assert.That(intactRenderer.sprite.name, Is.EqualTo("Objects_27"));
            Assert.That(destroyed.arraySize, Is.Zero);
            Assert.That(crate.transform.Find("Remains"), Is.Null);
        }

        [Test]
        public void Jar_IsABreakableVariant_IntactThenBroken()
        {
            GameObject crate = Load(BreakablePath);
            GameObject jar = Load(JarPath);
            Assert.That(PrefabUtility.GetPrefabAssetType(jar), Is.EqualTo(PrefabAssetType.Variant));
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(jar), Is.SameAs(crate));

            var serialized = new SerializedObject(jar.GetComponent<BreakableObject>());
            SerializedProperty intact = serialized.FindProperty("_intactRenderers");
            SerializedProperty destroyed = serialized.FindProperty("_destroyedRenderers");
            Assert.That(intact.arraySize, Is.EqualTo(1));
            Assert.That(destroyed.arraySize, Is.EqualTo(1));

            var intactRenderer = intact.GetArrayElementAtIndex(0).objectReferenceValue as SpriteRenderer;
            var destroyedRenderer = destroyed.GetArrayElementAtIndex(0).objectReferenceValue as SpriteRenderer;
            Assert.That(intactRenderer, Is.Not.Null);
            Assert.That(destroyedRenderer, Is.Not.Null);
            Assert.That(destroyedRenderer, Is.Not.SameAs(intactRenderer));
            Assert.That(intactRenderer.sprite.texture.name, Is.EqualTo("Pot-Close"));
            Assert.That(destroyedRenderer.sprite.texture.name, Is.EqualTo("Pot-Open"));
            Assert.That(intactRenderer.enabled, Is.True, "a fresh jar shows its intact look");
            Assert.That(destroyedRenderer.enabled, Is.False, "the remains appear only once it is broken");
            Assert.That(destroyedRenderer.sortingLayerID, Is.EqualTo(intactRenderer.sortingLayerID));
        }

        [Test]
        public void Jar_RemainsAreHiddenByFogLikeTheIntactJar()
        {
            GameObject jar = Load(JarPath);
            var serialized = new SerializedObject(jar.GetComponent<BreakableObject>());
            Object destroyedRenderer = serialized.FindProperty("_destroyedRenderers")
                .GetArrayElementAtIndex(0).objectReferenceValue;
            SerializedProperty fogRenderers = new SerializedObject(jar.GetComponent<EntityVisibilityPresenter>())
                .FindProperty("_renderers");

            bool covered = false;
            for (int i = 0; i < fogRenderers.arraySize; i++)
            {
                covered |= fogRenderers.GetArrayElementAtIndex(i).objectReferenceValue == destroyedRenderer;
            }

            Assert.That(covered, Is.True);
        }

        private static Rect ArtworkRect(GameObject prefab)
        {
            SpriteRenderer renderer = prefab.GetComponentInChildren<SpriteRenderer>(true);
            Assert.That(renderer, Is.Not.Null);
            Assert.That(renderer.sprite, Is.Not.Null);

            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);
            foreach (Vector2 vertex in renderer.sprite.vertices)
            {
                Vector2 point = prefab.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertex));
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static GameObject Load(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            return prefab;
        }
    }
}
#endif

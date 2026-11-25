using System.Linq;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using ProjectGrimhold.Gameplay.Visibility;
using Assert = NUnit.Framework.Assert;

namespace Tests.EditMode.Scenario
{
    public sealed class ExtractionSanctuaryPrefabTests
    {
        private const string PrefabPath = "Assets/Prefabs/ExtractionSanctuary.prefab";
        private const string ScenePath = "Assets/Scenes/Gameplay.unity";

        [Test]
        public void ExtractionFlowHasNoStandaloneZonePrefab()
        {
            Assert.That(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ExtractionZone.prefab"),
                Is.Null);
        }

        [Test]
        public void Prefab_ComposesSanctuaryAndInteractionAreaUnderOneRoot()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<ExtractionSanctuary>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<ExtractionZone>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<ExtractionSanctuaryPresenter>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BoxCollider2D>(), Is.Not.Null);
            SpriteRenderer zoneRenderer = prefab.transform.Find("Visuals/Off").GetComponent<SpriteRenderer>();
            Assert.That(zoneRenderer, Is.Not.Null);
            Assert.That(zoneRenderer.enabled, Is.True);
            Assert.That(zoneRenderer.sortingLayerName, Is.EqualTo("Characters"));
            Assert.That(zoneRenderer.color.a, Is.GreaterThan(0f));
            Assert.That(prefab.GetComponent<IInteractable>(), Is.SameAs(prefab.GetComponent<ExtractionSanctuary>()));
            Assert.That(prefab.GetComponent<InteractionPromptMetadata>().PromptText, Is.EqualTo("Usar santuario"));
            Assert.That(prefab.layer, Is.EqualTo(LayerMask.NameToLayer("Interactable")));
            Assert.That(prefab.GetComponent<ExtractionZone>().IsAvailable, Is.False);
            EntityVisibilityPresenter visibilityPresenter = prefab.GetComponent<EntityVisibilityPresenter>();
            Assert.That(visibilityPresenter, Is.Not.Null);
            SerializedObject serializedVisibility = new SerializedObject(visibilityPresenter);
            Assert.That(
                serializedVisibility.FindProperty("_visibilityCollider").objectReferenceValue,
                Is.SameAs(prefab.GetComponent<BoxCollider2D>()));
            Assert.That(
                serializedVisibility.FindProperty("_useColliderBounds").boolValue,
                Is.True);
            Assert.That(
                typeof(ExtractionZone).GetField("_spriteRenderer", BindingFlags.Instance | BindingFlags.NonPublic),
                Is.Null);
        }

        [Test]
        public void Gameplay_ContainsExactlyFourDistinctSanctuaryInstances()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                ExtractionSanctuary[] sanctuaries = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<ExtractionSanctuary>(true))
                    .ToArray();

                Assert.That(sanctuaries, Has.Length.EqualTo(4));
                Assert.That(sanctuaries.Select(item => item.gameObject.name).Distinct().Count(), Is.EqualTo(4));
                Assert.That(sanctuaries.All(item => item.GetComponent<NetworkObject>() != null), Is.True);
                Assert.That(sanctuaries.All(item => item.GetComponent<ExtractionZone>() != null), Is.True);

                ExtractionZone[] zones = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<ExtractionZone>(true))
                    .ToArray();
                Assert.That(zones, Has.Length.EqualTo(4));
                Assert.That(zones.All(zone => zone.GetComponent<ExtractionSanctuary>() != null), Is.True);

                Tilemap floor = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Tilemap>(true))
                    .Single(tilemap => tilemap.name == "Floor");
                foreach (ExtractionZone zone in zones)
                {
                    AssertZoneFootprintIsOnFloor(zone, floor);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }
            }
        }

        [Test]
        public void ExtractionPresentationUsesAuthoredCuesWithoutAddingGameplayOrAudioComponents()
        {
            GameObject sanctuary = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            ExtractionSanctuaryPresenter presenter = sanctuary.GetComponent<ExtractionSanctuaryPresenter>();
            SerializedObject serializedPresenter = new SerializedObject(presenter);

            Assert.That(sanctuary.GetComponentsInChildren<AudioSource>(true), Is.Empty);
            Assert.That(sanctuary.GetComponentsInChildren<ParticleSystem>(true), Is.Empty);
            Assert.That(sanctuary.GetComponentsInChildren<NetworkBehaviour>(true),
                Has.Length.EqualTo(2));
            Assert.That(sanctuary.GetComponent<ExtractionSanctuary>(), Is.Not.Null);
            Assert.That(sanctuary.GetComponent<ExtractionZone>(), Is.Not.Null);

            string[] audioCueFields =
            {
                "_assignedCue",
                "_ritualStartedCue",
                "_ritualCancelledCue",
                "_ritualCompletedCue"
            };
            foreach (string fieldName in audioCueFields)
            {
                SerializedProperty cue = serializedPresenter.FindProperty(fieldName);
                SerializedProperty clips = cue.FindPropertyRelative("_clips");
                Assert.That(
                    clips.arraySize,
                    Is.GreaterThan(0),
                    $"Expected an authored clip for {fieldName}.");
                Assert.That(clips.GetArrayElementAtIndex(0).objectReferenceValue, Is.Not.Null);
                Assert.That(cue.FindPropertyRelative("_spatialBlend").floatValue, Is.EqualTo(1f));
            }

            ParticleSystem authoredParticleCue = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/VFX/HealingParticles.prefab").GetComponent<ParticleSystem>();
            Assert.That(serializedPresenter.FindProperty("_ritualCompletedParticles").objectReferenceValue,
                Is.SameAs(authoredParticleCue));
        }

        [Test]
        public void Presenter_OnlyEmitsCuesForObservedConfirmedTransitions()
        {
            AssertCue(
                "None",
                false,
                ExtractionRitualState.NotStarted,
                ExtractionRitualState.InProgress,
                false,
                true,
                true);
            AssertCue(
                "None",
                true,
                ExtractionRitualState.InProgress,
                ExtractionRitualState.InProgress,
                true,
                true,
                true);
            AssertCue("Started", true, ExtractionRitualState.NotStarted,
                ExtractionRitualState.InProgress, true, true, false);
            AssertCue("Assigned", true, ExtractionRitualState.NotStarted,
                ExtractionRitualState.NotStarted, false, true, true);
            AssertCue("Cancelled", true, ExtractionRitualState.InProgress,
                ExtractionRitualState.Cancelled, true, true, true);
            AssertCue("None", true, ExtractionRitualState.InProgress,
                ExtractionRitualState.Cancelled, true, true, false);
            AssertCue("Completed", true, ExtractionRitualState.InProgress,
                ExtractionRitualState.Completed, true, true, false);
        }

        [TestCase(ExtractionRitualState.NotStarted, 0f)]
        [TestCase(ExtractionRitualState.Cancelled, 0f)]
        [TestCase(ExtractionRitualState.Completed, 1f)]
        public void ActivationAlpha_NonRunningStates(ExtractionRitualState state, float expected)
        {
            Assert.That(ResolveAlpha(state, 0.5f, 0.5f), Is.EqualTo(expected));
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(0.5f, 0f, 0.375f)]
        [TestCase(0.5f, 1f, 0.5f)]
        [TestCase(1f, 0f, 0.75f)]
        [TestCase(-1f, -1f, 0f)]
        [TestCase(2f, 2f, 1f)]
        public void ActivationAlpha_ProgressTimesBoundedSoftPulse(float progress, float pulse, float expected)
        {
            Assert.That(ResolveAlpha(ExtractionRitualState.InProgress, progress, pulse),
                Is.EqualTo(expected).Within(0.00001f));
        }

        private static float ResolveAlpha(ExtractionRitualState state, float progress, float pulse)
        {
            System.Type rules = typeof(ExtractionSanctuaryPresenter).Assembly.GetType("SanctuaryActivationVisual");
            Assert.That(rules, Is.Not.Null, "Sanctuary activation alpha helper is required.");
            return (float)rules.GetMethod("ResolveAlpha").Invoke(null, new object[] { state, progress, pulse });
        }

        [Test]
        public void Prefab_V1VisualsPreserveGameplayGeometryAndVisibility()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab.transform.localScale, Is.EqualTo(new Vector3(1.5f, 1.5f, 1f)));
            Transform visuals = prefab.transform.Find("Visuals");
            Assert.That(visuals, Is.Not.Null, "Compensated Visuals child is required.");
            Assert.That(visuals.localScale.x, Is.EqualTo(0.6666667f).Within(0.000001f));
            SpriteRenderer off = visuals.Find("Off").GetComponent<SpriteRenderer>();
            SpriteRenderer on = visuals.Find("OnOverlay").GetComponent<SpriteRenderer>();
            Assert.That(prefab.GetComponent<SpriteRenderer>(), Is.Null);
            Assert.That(off.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(on.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(off.color, Is.EqualTo(Color.white));
            Assert.That(on.color, Is.EqualTo(new Color(1f, 1f, 1f, 0f)));
            Assert.That(on.sortingLayerID, Is.EqualTo(off.sortingLayerID));
            Assert.That(on.sortingOrder, Is.EqualTo(off.sortingOrder + 1));
            foreach (SpriteRenderer renderer in new[] { off, on })
            {
                string path = "Assets/Art/Props/Sanctuary/New/Sanctuary-Large-" + (renderer == off ? "Inactive" : "Active") + ".png";
                Assert.That(AssetDatabase.GetAssetPath(renderer.sprite), Is.EqualTo(path));
                TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16f));
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
                TextureImporterSettings settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect));
            }
            SerializedObject presenter = new SerializedObject(prefab.GetComponent<ExtractionSanctuaryPresenter>());
            Assert.That(presenter.FindProperty("_spriteRenderer").objectReferenceValue, Is.SameAs(off));
            Assert.That(presenter.FindProperty("_onOverlayRenderer").objectReferenceValue, Is.SameAs(on));
            SerializedProperty renderers = new SerializedObject(prefab.GetComponent<EntityVisibilityPresenter>()).FindProperty("_renderers");
            Assert.That(renderers.arraySize, Is.EqualTo(2));
            Assert.That(renderers.GetArrayElementAtIndex(0).objectReferenceValue, Is.SameAs(off));
            Assert.That(renderers.GetArrayElementAtIndex(1).objectReferenceValue, Is.SameAs(on));
            BoxCollider2D collider = prefab.GetComponent<BoxCollider2D>();
            Assert.That(prefab.GetComponentsInChildren<Collider2D>(true), Has.Length.EqualTo(1));
            Assert.That(collider.size, Is.EqualTo(new Vector2(2f, 2f)));
            Assert.That(collider.offset, Is.EqualTo(Vector2.zero));
            Assert.That(collider.isTrigger, Is.True);
            Assert.That(new SerializedObject(prefab.GetComponent<ExtractionZone>()).FindProperty("_zoneCollider").objectReferenceValue, Is.SameAs(collider));
            Assert.That(new SerializedObject(prefab.GetComponent<ExtractionSanctuary>()).FindProperty("_interactionCollider").objectReferenceValue, Is.SameAs(collider));
            Vector3[] positions = { new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
            for (int i = 0; i < positions.Length; i++)
            {
                Transform spawn = prefab.transform.Find("Spawnpoint " + (i + 1));
                Assert.That(spawn.localPosition, Is.EqualTo(positions[i]));
                Assert.That(spawn.localScale, Is.EqualTo(new Vector3(0.6666667f, 0.6666667f, 1f)));
                Assert.That(prefab.transform.TransformVector(spawn.localPosition).magnitude, Is.LessThan(2.5f));
            }
        }

        [TestCase(ExtractionRitualState.NotStarted, false, false)]
        [TestCase(ExtractionRitualState.NotStarted, true, true)]
        [TestCase(ExtractionRitualState.InProgress, true, false)]
        [TestCase(ExtractionRitualState.Completed, true, true)]
        [TestCase(ExtractionRitualState.Cancelled, true, true)]
        public void Presenter_UsesUntintedSpritesAndRestoresOnDisable(ExtractionRitualState state, bool reserved, bool owner)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ExtractionSanctuaryPresenter presenter = root.GetComponent<ExtractionSanctuaryPresenter>();
                SerializedObject serialized = new SerializedObject(presenter);
                SpriteRenderer off = (SpriteRenderer)serialized.FindProperty("_spriteRenderer").objectReferenceValue;
                SerializedProperty overlay = serialized.FindProperty("_onOverlayRenderer");
                Assert.That(overlay, Is.Not.Null, "On overlay dependency is required.");
                SpriteRenderer on = (SpriteRenderer)overlay.objectReferenceValue;
                Color originalOff = off.color;
                Color originalOn = on.color;
                InvokePresenter(presenter, "CaptureOriginalState");
                InvokePresenter(presenter, "PresentSnapshot", new ExtractionRitualSnapshot(state, 10f, 5f, 0.5f), reserved, owner);
                Assert.That(off.color, Is.EqualTo(Color.white));
                Assert.That(new Vector3(on.color.r, on.color.g, on.color.b), Is.EqualTo(Vector3.one));
                if (state == ExtractionRitualState.InProgress)
                    Assert.That(on.color.a, Is.InRange(0.375f, 0.5f));
                else
                    Assert.That(on.color.a, Is.EqualTo(state == ExtractionRitualState.Completed ? 1f : 0f));
                InvokePresenter(presenter, "OnDisable");
                Assert.That(off.color, Is.EqualTo(originalOff));
                Assert.That(on.color, Is.EqualTo(originalOn));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void Presenter_MissingOverlayFailsClearlyOnEnable()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ExtractionSanctuaryPresenter presenter = root.GetComponent<ExtractionSanctuaryPresenter>();
                SerializedObject serialized = new SerializedObject(presenter);
                serialized.FindProperty("_onOverlayRenderer").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                    "ExtractionSanctuaryPresenter requires a sanctuary and distinct Off and On overlay renderers.");
                InvokePresenter(presenter, "OnEnable");
                Assert.That(presenter.enabled, Is.False);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void InvokePresenter(ExtractionSanctuaryPresenter presenter, string method, params object[] args)
        {
            typeof(ExtractionSanctuaryPresenter).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presenter, args);
        }

        private static void AssertCue(
            string expected,
            bool hasPreviousSnapshot,
            ExtractionRitualState previousState,
            ExtractionRitualState currentState,
            bool previouslyReserved,
            bool currentlyReserved,
            bool isLocalOwner)
        {
            MethodInfo resolver = typeof(ExtractionSanctuaryPresenter).GetMethod(
                "ResolveObservedCues",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(resolver, Is.Not.Null);
            // Single-team view: the local owner's own ritual is the world signal; other viewers own nothing.
            ExtractionRitualState previousOwnState = isLocalOwner ? previousState : ExtractionRitualState.NotStarted;
            ExtractionRitualState currentOwnState = isLocalOwner ? currentState : ExtractionRitualState.NotStarted;
            object result = resolver.Invoke(null, new object[]
            {
                hasPreviousSnapshot,
                previousState,
                currentState,
                previousOwnState,
                currentOwnState,
                previouslyReserved,
                currentlyReserved,
                isLocalOwner
            });
            Assert.That(result.ToString(), Is.EqualTo(expected));
        }

        private static void AssertZoneFootprintIsOnFloor(ExtractionZone zone, Tilemap floor)
        {
            Collider2D collider = zone.GetComponent<Collider2D>();
            Assert.That(collider, Is.Not.Null, zone.name);

            const float boundsInset = 0.01f;
            Bounds bounds = collider.bounds;
            Vector3 min = bounds.min + new Vector3(boundsInset, boundsInset, 0f);
            Vector3 max = bounds.max - new Vector3(boundsInset, boundsInset, 0f);
            Vector3Int minCell = floor.WorldToCell(min);
            Vector3Int maxCell = floor.WorldToCell(max);

            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    Vector3Int cell = new Vector3Int(x, y, 0);
                    Assert.That(
                        floor.HasTile(cell),
                        Is.True,
                        $"{zone.name} footprint leaves Floor at cell {cell}.");
                }
            }
        }
    }
}

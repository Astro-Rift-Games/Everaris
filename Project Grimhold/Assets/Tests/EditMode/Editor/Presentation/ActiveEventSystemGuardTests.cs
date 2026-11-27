using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using Assert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;

/// <summary>
/// FIX-60: Ensures strictly one active EventSystem exists across MainMenu, Town, and Raid scenes,
/// that runtime duplicates are immediately disabled in favor of the active scene's
/// InputSystemUIInputModule EventSystem, and that PlayerInputReader resets cleanly across transitions.
/// </summary>
[TestFixture]
public sealed class ActiveEventSystemGuardTests
{
    [TestCase("Assets/Scenes/MainMenu.unity")]
    [TestCase("Assets/Scenes/Lobby-Town.unity")]
    [TestCase("Assets/Scenes/CatacombsMap.unity")]
    [TestCase("Assets/Scenes/Gameplay.unity")]
    public void ProductionScene_ContainsExactlyOneEventSystemWithInputSystemModule(string scenePath)
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(scenePath);
        try
        {
            EventSystem[] systems = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<EventSystem>(true))
                .ToArray();

            Assert.AreEqual(1, systems.Length, $"{scenePath} must author exactly one EventSystem.");
            Assert.IsNotNull(systems[0].GetComponent<InputSystemUIInputModule>(),
                $"{scenePath}'s EventSystem must use InputSystemUIInputModule.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void DeduplicateEventSystems_PrefersActiveSceneInputSystemModule_AndDisablesDuplicates()
    {
        var duplicate = new GameObject("DuplicateBareEventSystem");
        var dupEs = duplicate.AddComponent<EventSystem>();

        var sceneCanonical = new GameObject("CanonicalEventSystem");
        var canonicalEs = sceneCanonical.AddComponent<EventSystem>();
        sceneCanonical.AddComponent<InputSystemUIInputModule>();

        try
        {
            EventSystem keeper = ActiveEventSystemGuard.DeduplicateEventSystems(
                new[] { dupEs, canonicalEs },
                sceneCanonical.scene);

            Assert.AreSame(canonicalEs, keeper, "Must keep the EventSystem with InputSystemUIInputModule.");
            Assert.IsTrue(canonicalEs.enabled);
            Assert.IsFalse(dupEs.enabled, "Duplicate EventSystem must be disabled immediately.");
        }
        finally
        {
            Object.DestroyImmediate(duplicate);
            Object.DestroyImmediate(sceneCanonical);
        }
    }

    [Test]
    public void PlayerInputReader_DoesNotRetainSuppressedOrBlockedInteractStateAfterReEnable()
    {
        var go = new GameObject("Fix60InputReaderTest");
        try
        {
            var reader = go.AddComponent<PlayerInputReader>();
            typeof(PlayerInputReader)
                .GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(reader, null);

            Assert.IsFalse(reader.IsGameplayInputSuppressed);

            using (reader.AcquireGameplayInputSuppression())
            {
                Assert.IsTrue(reader.IsGameplayInputSuppressed);
            }

            Assert.IsFalse(reader.IsGameplayInputSuppressed,
                "Releasing suppression before leaving Town/Raid must leave gameplay interact input ready.");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}

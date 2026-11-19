#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

namespace Tests.EditMode.Audio
{
    /// <summary>
    /// Every scene that can be the active single-loaded scene (all enabled build scenes: MainMenu is the
    /// app entry, Lobby-Town and CatacombsMap are loaded Single by Fusion, Gameplay is a direct entry)
    /// must carry exactly one AudioManager prefab instance. Otherwise entering Play Mode there leaves
    /// <c>AudioManager.Instance</c> null. The singleton keeps the first instance and destroys duplicates.
    /// Scene files are read as text so no scene is opened or dirtied.
    /// </summary>
    public sealed class AudioManagerSceneCompositionTests
    {
        private const string AudioManagerPrefabPath = "Assets/Prefabs/Audio/AudioManager.prefab";

        [Test]
        public void EveryEntryScene_ContainsExactlyOneAudioManagerPrefabInstance()
        {
            string prefabGuid = AssetDatabase.AssetPathToGUID(AudioManagerPrefabPath);
            Assert.That(prefabGuid, Is.Not.Empty, $"Missing prefab at {AudioManagerPrefabPath}.");

            var sourcePrefab = new Regex(
                @"m_SourcePrefab:\s*\{fileID:\s*\d+,\s*guid:\s*" + prefabGuid + @",");

            var problems = new List<string>();
            int sceneCount = 0;

            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
            {
                if (!buildScene.enabled) continue;

                sceneCount++;
                int instances = sourcePrefab.Matches(File.ReadAllText(buildScene.path)).Count;
                if (instances != 1)
                {
                    problems.Add($"{buildScene.path} has {instances} AudioManager instances (expected 1)");
                }
            }

            Assert.That(sceneCount, Is.GreaterThan(0), "No enabled build scenes were found.");
            Assert.That(problems, Is.Empty,
                "Entry scenes without exactly one AudioManager prefab instance:\n" + string.Join("\n", problems));
        }
    }
}
#endif

#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Audio
{
    /// <summary>
    /// Every EntityAudioConfig asset must satisfy the key declaration of its config type, and every
    /// SceneMusicPresenter must point at a track that exists in its music config. Runtime lookups are
    /// case-insensitive and fail silently, so typos and missing required keys are only caught here.
    /// </summary>
    public sealed class AudioConfigKeyValidationTests
    {
        private const string ConfigsRoot = "Assets/Scriptable Objects/Audio";
        private const string PrefabsRoot = "Assets/Prefabs";
        private const string ScenesRoot = "Assets/Scenes";
        private const string SceneMusicPrefabPath = "Assets/Prefabs/Audio/SceneMusic.prefab";

        [Test]
        public void EveryAudioConfig_HasItsRequiredKeys_WithContent()
        {
            var offenders = new List<string>();
            foreach (LoadedConfig config in LoadConfigs())
            {
                foreach (string missing in config.Spec.FindMissing(config.Keys))
                {
                    offenders.Add($"{config.Path}: missing {missing}");
                }

                foreach (string required in config.Spec.Required)
                {
                    if (config.Keys.Contains(required) && !config.KeysWithClips.Contains(required))
                    {
                        offenders.Add($"{config.Path}: required key '{required}' has no clips");
                    }
                }
            }

            Assert.That(offenders, Is.Empty, "Required audio keys missing:\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void EveryAudioConfigEntry_IsAKnownKeyForItsConfigType()
        {
            var offenders = new List<string>();
            foreach (LoadedConfig config in LoadConfigs())
            {
                foreach (string unknown in config.Spec.FindUnknown(config.Keys))
                {
                    offenders.Add($"{config.Path} ({config.TypeName}): '{unknown}'");
                }
            }

            Assert.That(offenders, Is.Empty,
                "Unknown audio keys (typo, wrong case or another config type's key):\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void EveryAudioConfig_HasNoDuplicateKeys()
        {
            var offenders = new List<string>();
            foreach (LoadedConfig config in LoadConfigs())
            {
                foreach (string duplicate in AudioKeySpec.FindDuplicates(config.Keys))
                {
                    offenders.Add($"{config.Path}: '{duplicate}'");
                }
            }

            Assert.That(offenders, Is.Empty, "Duplicate audio keys (case-insensitive):\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void MusicEntriesAre2D_AndEveryOtherAudioConfigEntryIs3D()
        {
            var offenders = new List<string>();
            int music = 0;
            int world = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:EntityAudioConfig", new[] { ConfigsRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<EntityAudioConfig>(path);
                bool isMusic = config is MusicAudioConfig;
                float expected = isMusic ? 0f : 1f;

                SerializedProperty entries = new SerializedObject(config).FindProperty("_entries");
                for (int i = 0; i < entries.arraySize; i++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                    float blend = entry.FindPropertyRelative("Clip._spatialBlend").floatValue;
                    if (isMusic) music++; else world++;
                    if (blend != expected)
                    {
                        string key = entry.FindPropertyRelative("Key").stringValue;
                        offenders.Add($"{path}: '{key}' SpatialBlend {blend} (expected {expected})");
                    }
                }
            }

            Assert.That(music, Is.GreaterThan(0), "Vacuous check: no music entries.");
            Assert.That(world, Is.GreaterThan(0), "Vacuous check: no world entries.");
            Assert.That(offenders, Is.Empty, "Music must be 2D and every other audio entry 3D:\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void EverySceneMusicPresenter_TrackKeyExistsInItsMusicConfig()
        {
            var offenders = new List<string>();
            int checkedTracks = 0;

            // Presenters authored in prefabs (includes the SceneMusic prefab defaults).
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;

                foreach (SceneMusicPresenter presenter in root.GetComponentsInChildren<SceneMusicPresenter>(true))
                {
                    var serialized = new SerializedObject(presenter);
                    var config = serialized.FindProperty("_musicConfig").objectReferenceValue as MusicAudioConfig;
                    checkedTracks++;
                    CheckTrack(offenders, path, serialized.FindProperty("_trackKey").stringValue, config);
                }
            }

            // Scene overrides of the SceneMusic prefab, read as text so no scene is loaded or dirtied.
            var sceneMusic = AssetDatabase.LoadAssetAtPath<GameObject>(SceneMusicPrefabPath);
            Assert.That(sceneMusic, Is.Not.Null, SceneMusicPrefabPath);
            var prefabPresenter = sceneMusic.GetComponentInChildren<SceneMusicPresenter>(true);
            Assert.That(prefabPresenter, Is.Not.Null, SceneMusicPrefabPath);
            var prefabMusicConfig = new SerializedObject(prefabPresenter).FindProperty("_musicConfig").objectReferenceValue as MusicAudioConfig;
            string prefabGuid = AssetDatabase.AssetPathToGUID(SceneMusicPrefabPath);

            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { ScenesRoot }))
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(guid);
                foreach (string overrideKey in ReadTrackKeyOverrides(scenePath, prefabGuid))
                {
                    checkedTracks++;
                    CheckTrack(offenders, scenePath, overrideKey, prefabMusicConfig);
                }
            }

            Assert.That(checkedTracks, Is.GreaterThan(1),
                "Vacuous check: expected the SceneMusic prefab and at least one scene override.");
            Assert.That(offenders, Is.Empty, "SceneMusicPresenter tracks missing from the music config:\n  " + string.Join("\n  ", offenders));
        }

        private static void CheckTrack(List<string> offenders, string location, string trackKey, MusicAudioConfig config)
        {
            if (config == null)
            {
                offenders.Add($"{location}: no _musicConfig assigned (track '{trackKey}')");
            }
            else if (!config.TryGetClip(trackKey, out CustomClip clip) || !clip.IsValid)
            {
                offenders.Add($"{location}: track '{trackKey}' not found (or without clips) in {AssetDatabase.GetAssetPath(config)}");
            }
        }

        // Matches `propertyPath: _trackKey` overrides whose target belongs to the SceneMusic prefab.
        private static IEnumerable<string> ReadTrackKeyOverrides(string scenePath, string prefabGuid)
        {
            string text = File.ReadAllText(scenePath);
            var pattern = new Regex(
                @"guid: " + prefabGuid + @", type: 3\}\s*\r?\n\s*propertyPath: _trackKey\s*\r?\n\s*value: (?<key>[^\r\n]*)");
            foreach (Match match in pattern.Matches(text))
            {
                yield return match.Groups["key"].Value.Trim();
            }
        }

        private static List<LoadedConfig> LoadConfigs()
        {
            var configs = new List<LoadedConfig>();
            foreach (string guid in AssetDatabase.FindAssets("t:EntityAudioConfig", new[] { ConfigsRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<EntityAudioConfig>(path);
                Assert.That(config, Is.Not.Null, path);

                AudioKeySpec spec = AudioKeys.SpecFor(config.GetType());
                Assert.That(spec, Is.Not.Null, $"{path}: no AudioKeys declaration for {config.GetType().Name}");

                var keys = new List<string>();
                var withClips = new HashSet<string>();
                SerializedProperty entries = new SerializedObject(config).FindProperty("_entries");
                for (int i = 0; i < entries.arraySize; i++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                    string key = entry.FindPropertyRelative("Key").stringValue;
                    keys.Add(key);
                    if (entry.FindPropertyRelative("Clip._clips").arraySize > 0) withClips.Add(key);
                }

                configs.Add(new LoadedConfig(path, config.GetType().Name, spec, keys, withClips));
            }

            Assert.That(configs, Is.Not.Empty, $"No EntityAudioConfig under '{ConfigsRoot}'; the check would be vacuous.");
            return configs;
        }

        private readonly struct LoadedConfig
        {
            public readonly string Path;
            public readonly string TypeName;
            public readonly AudioKeySpec Spec;
            public readonly List<string> Keys;
            public readonly HashSet<string> KeysWithClips;

            public LoadedConfig(string path, string typeName, AudioKeySpec spec, List<string> keys, HashSet<string> keysWithClips)
            {
                Path = path;
                TypeName = typeName;
                Spec = spec;
                Keys = keys;
                KeysWithClips = keysWithClips;
            }
        }
    }
}
#endif

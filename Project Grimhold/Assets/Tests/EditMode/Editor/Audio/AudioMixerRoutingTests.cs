#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Audio
{
    /// <summary>
    /// All audio must be routed through the mixer so the Master/SFX sliders control it. An AudioSource
    /// without an output group (or one created by AudioSource.PlayClipAtPoint) bypasses the mixer.
    /// </summary>
    public sealed class AudioMixerRoutingTests
    {
        private const string PrefabsRoot = "Assets/Prefabs";
        private const string ScriptsRoot = "Assets/Scripts";
        private const string ForbiddenCall = "AudioSource.PlayClipAtPoint";

        [Test]
        public void EveryPrefabAudioSource_HasAMixerGroup()
        {
            int sourceCount = 0;
            var offenders = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;

                foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
                {
                    sourceCount++;
                    if (source.outputAudioMixerGroup == null)
                    {
                        offenders.Add($"{path} :: {HierarchyPath(source.transform)}");
                    }
                }
            }

            Assert.That(sourceCount, Is.GreaterThan(0),
                $"No AudioSource found under '{PrefabsRoot}'; the check would be vacuous.");
            Assert.That(offenders, Is.Empty,
                "AudioSources without an output mixer group (bypass Master/SFX volume):\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void Scripts_DoNotCallPlayClipAtPoint()
        {
            string scriptsFullPath = Path.GetFullPath(ScriptsRoot);
            Assert.That(Directory.Exists(scriptsFullPath), Is.True, $"'{ScriptsRoot}' not found.");

            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(scriptsFullPath, "*.cs", SearchOption.AllDirectories))
            {
                if (ContainsForbiddenCall(File.ReadAllText(file)))
                {
                    offenders.Add(file.Replace('\\', '/'));
                }
            }

            Assert.That(offenders, Is.Empty,
                $"{ForbiddenCall} creates a source without a mixer group; use AudioManager.PlaySfx instead. Offending files:\n  "
                + string.Join("\n  ", offenders));
        }

        private static bool ContainsForbiddenCall(string source)
        {
            foreach (string line in source.Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//")) continue;
                if (trimmed.Contains(ForbiddenCall)) return true;
            }

            return false;
        }

        private static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }
    }
}
#endif

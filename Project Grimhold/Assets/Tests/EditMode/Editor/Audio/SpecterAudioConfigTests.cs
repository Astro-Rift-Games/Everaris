#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Audio
{
    /// <summary>
    /// The Specter state cues (Channel / Detonate / Reappear) are optional keys of <see cref="SpecterAudioConfig"/>
    /// only; the other enemies keep strict validation. The shipped asset carries placeholder clips for them.
    /// </summary>
    public sealed class SpecterAudioConfigTests
    {
        private const string ConfigPath = "Assets/Scriptable Objects/Audio/SpecterAudioConfig.asset";
        private const string PrefabPath = "Assets/Prefabs/Enemies/NetworkEnemySpecter.prefab";
        private static readonly string[] CueKeys = { AudioKeys.Channel, AudioKeys.Detonate, AudioKeys.Reappear };

        [Test]
        public void KeyConstants_MatchTheSpelling_UsedByTheAsset()
        {
            Assert.That(CueKeys, Is.EqualTo(new[] { "Channel", "Detonate", "Reappear" }));
        }

        [Test]
        public void SpecterSpec_DeclaresTheCuesAsOptional_KeepingTheEnemyRequirements()
        {
            AudioKeySpec specter = AudioKeys.SpecFor(typeof(SpecterAudioConfig));
            AudioKeySpec enemy = AudioKeys.SpecFor(typeof(EnemyAudioConfig));

            Assert.That(specter, Is.Not.Null);
            Assert.That(specter, Is.Not.SameAs(enemy), "Specter needs its own declaration.");
            foreach (string key in CueKeys)
            {
                Assert.That(specter.Optional, Does.Contain(key), key);
                Assert.That(specter.Required, Does.Not.Contain(key), key);
                Assert.That(specter.RequiredAnyOf, Does.Not.Contain(key), key);
            }

            Assert.That(specter.Required, Is.EqualTo(enemy.Required));
            Assert.That(specter.RequiredAnyOf, Is.EqualTo(enemy.RequiredAnyOf));
            Assert.That(specter.Optional, Does.Contain(AudioKeys.Death));
        }

        [Test]
        public void SpecterCues_AreSilentWhenAbsent_AndNeverRequired()
        {
            AudioKeySpec specter = AudioKeys.SpecFor(typeof(SpecterAudioConfig));
            var withoutCues = new[] { AudioKeys.TakeDamage, AudioKeys.Step, AudioKeys.Attack };

            Assert.That(specter.FindMissing(withoutCues), Is.Empty);
            Assert.That(specter.FindUnknown(withoutCues), Is.Empty);
            Assert.That(specter.FindUnknown(CueKeys), Is.Empty);
        }

        [Test]
        public void OtherEnemies_RejectTheSpecterCues()
        {
            AudioKeySpec enemy = AudioKeys.SpecFor(typeof(EnemyAudioConfig));

            Assert.That(enemy.FindUnknown(CueKeys), Is.EqualTo(CueKeys), "Skeleton/Slime configs must not accept Specter cues.");
            Assert.That(AudioKeys.SpecFor(typeof(PlayerAudioConfig)).FindUnknown(CueKeys), Is.EqualTo(CueKeys));
            Assert.That(AudioKeys.SpecFor(typeof(WeaponAudioConfig)).FindUnknown(CueKeys), Is.EqualTo(CueKeys));
        }

        [Test]
        public void SpecterAsset_IsASpecterAudioConfig_WithItsExistingKeys()
        {
            var config = AssetDatabase.LoadAssetAtPath<EntityAudioConfig>(ConfigPath);

            Assert.That(config, Is.InstanceOf<SpecterAudioConfig>(), "Script type must be SpecterAudioConfig.");
            foreach (string key in new[] { AudioKeys.Attack, AudioKeys.TakeDamage, AudioKeys.Step })
            {
                Assert.That(config.TryGetClip(key, out CustomClip clip) && clip.IsValid, Is.True, $"{key} must keep its clips.");
            }
        }

        [Test]
        public void SpecterAsset_HasDistinct3DOneShotPlaceholders_ForEveryCue()
        {
            var config = AssetDatabase.LoadAssetAtPath<EntityAudioConfig>(ConfigPath);
            SerializedProperty entries = new SerializedObject(config).FindProperty("_entries");
            var clipsByKey = new Dictionary<string, HashSet<Object>>();

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                string key = entry.FindPropertyRelative("Key").stringValue;
                var clips = new HashSet<Object>();
                SerializedProperty list = entry.FindPropertyRelative("Clip._clips");
                for (int c = 0; c < list.arraySize; c++) clips.Add(list.GetArrayElementAtIndex(c).objectReferenceValue);
                clipsByKey[key] = clips;

                if (System.Array.IndexOf(CueKeys, key) < 0) continue;
                Assert.That(entry.FindPropertyRelative("Clip._spatialBlend").floatValue, Is.EqualTo(1f), $"{key} is a world sound (3D).");
                Assert.That(entry.FindPropertyRelative("Clip._loop").boolValue, Is.False, $"{key} must be a one-shot, never a loop.");
            }

            foreach (string key in CueKeys)
            {
                Assert.That(clipsByKey.ContainsKey(key), Is.True, $"SpecterAudioConfig has no '{key}' entry.");
                Assert.That(clipsByKey[key], Is.Not.Empty, $"'{key}' has no clips.");
                Assert.That(clipsByKey[key], Has.None.Null, $"'{key}' has a missing clip.");
            }

            var every = new[] { AudioKeys.Channel, AudioKeys.Detonate, AudioKeys.Reappear, AudioKeys.Attack, AudioKeys.TakeDamage, AudioKeys.Step };
            for (int a = 0; a < every.Length; a++)
            {
                for (int b = a + 1; b < every.Length; b++)
                {
                    Assert.That(clipsByKey[every[a]].Overlaps(clipsByKey[every[b]]), Is.False,
                        $"'{every[a]}' and '{every[b]}' must not share a clip (cues must be distinguishable).");
                }
            }
        }

        [Test]
        public void SpecterPrefab_HasOneAudioPresenter_ReferencingTheSpecterConfig()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var expected = AssetDatabase.LoadAssetAtPath<SpecterAudioConfig>(ConfigPath);
            Assert.That(expected, Is.Not.Null);

            SpecterAudioPresenter[] presenters = prefab.GetComponentsInChildren<SpecterAudioPresenter>(true);
            Assert.That(presenters, Has.Length.EqualTo(1));

            var serialized = new SerializedObject(presenters[0]);
            Assert.That(serialized.FindProperty("_audioConfig").objectReferenceValue, Is.SameAs(expected));
            Assert.That(serialized.FindProperty("_controller").objectReferenceValue,
                Is.SameAs(prefab.GetComponent<ProjectGrimhold.Gameplay.Enemies.Specter.SpecterAIController>()));

            var enemyPresenter = prefab.GetComponentInChildren<EnemyAudioPresenter>(true);
            Assert.That(enemyPresenter, Is.Not.Null);
            Assert.That(new SerializedObject(enemyPresenter).FindProperty("_audioConfig").objectReferenceValue, Is.SameAs(expected),
                "The enemy presenter keeps sharing the Specter config.");
        }
    }
}
#endif

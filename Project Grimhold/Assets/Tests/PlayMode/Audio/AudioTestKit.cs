#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Assert = NUnit.Framework.Assert;

namespace Tests.PlayMode.Audio
{
    /// <summary>
    /// Shared audio test helpers. Configs and references are authored through <see cref="SerializedObject"/> (the same
    /// path the Inspector uses) instead of reflection on private fields; playback is observed on the pooled sources of
    /// the real <see cref="AudioManager"/>.
    /// </summary>
    internal static class AudioTestKit
    {
        /// <summary>A clip long enough to still be playing while a test inspects the pool.</summary>
        public static AudioClip Clip(string name) => AudioClip.Create(name, 44100 * 10, 1, 44100, false);

        /// <summary>Builds a real ScriptableObject config whose entries each carry one clip.</summary>
        public static T Config<T>(params (string key, AudioClip clip)[] entries) where T : EntityAudioConfig
        {
            var config = ScriptableObject.CreateInstance<T>();
            var serialized = new SerializedObject(config);
            SerializedProperty list = serialized.FindProperty("_entries");
            list.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Key").stringValue = entries[i].key;
                SerializedProperty clips = entry.FindPropertyRelative("Clip").FindPropertyRelative("_clips");
                clips.arraySize = 1;
                clips.GetArrayElementAtIndex(0).objectReferenceValue = entries[i].clip;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        public static void SetReference(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            Assert.That(property, Is.Not.Null, $"{target.GetType().Name} has no serialized field '{field}'.");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetReferences(Object target, string field, IReadOnlyList<Object> values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            Assert.That(property, Is.Not.Null, $"{target.GetType().Name} has no serialized field '{field}'.");
            property.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetString(Object target, string field, string value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            Assert.That(property, Is.Not.Null, $"{target.GetType().Name} has no serialized field '{field}'.");
            property.stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetFloat(Object target, string field, float value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            Assert.That(property, Is.Not.Null, $"{target.GetType().Name} has no serialized field '{field}'.");
            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Number of pooled sources currently playing <paramref name="clip"/>.</summary>
        public static int PlayingCount(AudioManager manager, AudioClip clip)
        {
            int count = 0;
            foreach (AudioSource source in manager.GetComponentsInChildren<AudioSource>())
            {
                if (source.isPlaying && source.clip == clip) count++;
            }

            return count;
        }

        /// <summary>Every clip any pooled source was ever given (a source keeps its clip after it stops).</summary>
        public static List<AudioClip> ClipsUsedBy(AudioManager manager)
        {
            var clips = new List<AudioClip>();
            foreach (AudioSource source in manager.GetComponentsInChildren<AudioSource>())
            {
                if (source.clip != null) clips.Add(source.clip);
            }

            return clips;
        }

        public static IEnumerator WaitFrames(int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
        }

        /// <summary>An <see cref="ICombatController"/> the presenters subscribe to, raised on demand.</summary>
        public sealed class StubCombatController : MonoBehaviour, ICombatController
        {
            public event Action<AttackPerformedEvent> AttackPerformed;
            public NetworkBool IsAttackEnabled => true;

            public void Raise(AttackPerformedEvent attack) => AttackPerformed?.Invoke(attack);

            public void Raise(AttackType type) =>
                Raise(new AttackPerformedEvent(new EntityId(1), type, Vector2.zero, Vector2.right, 1));
        }
    }

    /// <summary>
    /// A real Fusion Single-mode runner (the harness the other gameplay PlayMode suites use) that spawns the real
    /// gameplay prefabs, so presenters are exercised against a real <see cref="CharacterBase"/>.
    /// </summary>
    internal sealed class AudioRunnerFixture
    {
        public NetworkRunner Runner { get; private set; }
        public GameObject Root { get; private set; }

        public IEnumerator Start(string name, params Type[] simulationBehaviours)
        {
            Root = new GameObject(name);
            Runner = Root.AddComponent<NetworkRunner>();
            PlayModeRunnerComposition.AddLocalInputContext(Runner);
            Root.AddComponent<EntityRegistry>();
            foreach (Type type in simulationBehaviours) Root.AddComponent(type);

            var start = Runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Single,
                SessionName = $"{name}-{Guid.NewGuid():N}",
                SceneManager = Root.AddComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = Root.AddComponent<NetworkObjectProviderDefault>()
            });
            while (!start.IsCompleted) yield return null;
            Assert.That(start.Result.Ok, Is.True, start.Result.ShutdownReason.ToString());
        }

        public NetworkObject Load(string prefabGuid)
        {
            NetworkPrefabId id = Runner.Config.PrefabTable.GetId(NetworkObjectGuid.Parse(prefabGuid));
            Assert.That(id.IsValid, Is.True, $"Prefab {prefabGuid} must be registered with Fusion.");
            NetworkObject prefab = Runner.Config.PrefabTable.Load(id, true);
            Assert.That(prefab, Is.Not.Null);
            return prefab;
        }

        public IEnumerator Shutdown()
        {
            if (Runner != null && Runner.IsRunning)
            {
                var shutdown = Runner.Shutdown();
                while (!shutdown.IsCompleted) yield return null;
            }

            if (Root != null) Object.DestroyImmediate(Root);
            Runner = null;
            Root = null;
        }
    }
}
#endif

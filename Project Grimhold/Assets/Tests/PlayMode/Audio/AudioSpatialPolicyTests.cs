#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.Audio
{
    /// <summary>2D/3D policy: central distance settings on every SFX source and the explicit spatial mode of PlaySfx.</summary>
    public sealed class AudioSpatialPolicyTests
    {
        private const string AudioManagerPrefabPath = "Assets/Prefabs/Audio/AudioManager.prefab";
        private const string SkeletonConfigPath = "Assets/Scriptable Objects/Audio/SkeletonAudioConfig.asset";

        private readonly List<Object> _owned = new();
        private AudioManager _manager;

        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }

        [SetUp]
        public void CreateManager()
        {
            Assume.That(AudioManager.Instance, Is.Null, "Another AudioManager is already published.");
            _manager = Own(new GameObject("Audio spatial test manager")).AddComponent<AudioManager>();
        }

        [TearDown]
        public void Cleanup()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
            Assert.That(AudioManager.Instance, Is.Null, "Cleanup must leave no published AudioManager.");
        }

        private AudioClip NewClip(string name) => Own(AudioClip.Create(name, 4410, 1, 44100, false));

        private AudioSource PlayedSourceWith(AudioClip clip)
        {
            foreach (AudioSource source in _manager.GetComponentsInChildren<AudioSource>(true))
                if (source.clip == clip) return source;
            return null;
        }

        private static void AssertCentralSettings(AudioSource source)
        {
            Assert.That(source.rolloffMode, Is.EqualTo(AudioRolloffMode.Linear));
            Assert.That(source.minDistance, Is.EqualTo(2f));
            Assert.That(source.maxDistance, Is.EqualTo(20f));
        }

        [Test]
        public void EveryPooledSfxSource_UsesTheCentralSpatialSettings()
        {
            Transform pool = _manager.transform.Find("SFX_Pool");
            Assert.That(pool, Is.Not.Null);
            AudioSource[] sources = pool.GetComponentsInChildren<AudioSource>(true);
            Assert.That(sources, Is.Not.Empty);

            foreach (AudioSource source in sources) AssertCentralSettings(source);
        }

        [Test]
        public void CreateSfxSource_UsesTheCentralSpatialSettings()
        {
            AudioSource created = _manager.CreateSfxSource(_manager.transform, "created");

            AssertCentralSettings(created);
        }

        [Test]
        public void ApplySpatialSettings_AppliesThePolicyToAnOwnerControlledSource()
        {
            AudioSource own = Own(new GameObject("owned source")).AddComponent<AudioSource>();
            Assert.That(own.maxDistance, Is.EqualTo(500f), "Precondition: Unity default.");

            _manager.ApplySpatialSettings(own);

            AssertCentralSettings(own);
            Assert.DoesNotThrow(() => _manager.ApplySpatialSettings(null));
        }

        [Test]
        public void PlaySfx_AudioClip_WithoutSpatialArgument_StaysFlat2D()
        {
            AudioClip clip = NewClip("default");

            _manager.PlaySfx(clip, new Vector3(3f, 4f, 0f), 0.5f, 1.2f);

            AudioSource source = PlayedSourceWith(clip);
            Assert.That(source, Is.Not.Null);
            Assert.That(source.spatialBlend, Is.EqualTo(0f));
            Assert.That(source.volume, Is.EqualTo(0.5f));
            Assert.That(source.pitch, Is.EqualTo(1.2f));
            Assert.That(source.transform.position, Is.EqualTo(new Vector3(3f, 4f, 0f)));
            AssertCentralSettings(source);
        }

        [Test]
        public void PlaySfx_AudioClip_World3D_IsPositionalWithCentralSettings()
        {
            AudioClip clip = NewClip("world");

            _manager.PlaySfx(clip, new Vector3(3f, 4f, 0f), 1f, 1f, SfxSpatialMode.World3D);

            AudioSource source = PlayedSourceWith(clip);
            Assert.That(source, Is.Not.Null);
            Assert.That(source.spatialBlend, Is.EqualTo(1f));
            Assert.That(source.transform.position, Is.EqualTo(new Vector3(3f, 4f, 0f)));
            AssertCentralSettings(source);
        }

        [Test]
        public void PlaySfx_AudioClip_Flat2D_AfterA3DUseOfTheSameVoice_ResetsToFlat()
        {
            AudioClip world = NewClip("world");
            AudioClip ui = NewClip("ui");
            for (int i = 0; i < 16; i++) _manager.PlaySfx(world, Vector3.zero, 1f, 1f, SfxSpatialMode.World3D);

            _manager.PlaySfx(ui, Vector3.zero, 1f, 1f, SfxSpatialMode.Flat2D);

            Assert.That(PlayedSourceWith(ui).spatialBlend, Is.EqualTo(0f));
        }

        [TestCase(SfxSpatialMode.Default, 1f)]
        [TestCase(SfxSpatialMode.Flat2D, 0f)]
        [TestCase(SfxSpatialMode.World3D, 1f)]
        public void PlaySfx_CustomClip_SpatialMode_OverridesOrKeepsTheConfigBlend(SfxSpatialMode mode, float expectedBlend)
        {
            var config = AssetDatabase.LoadAssetAtPath<EnemyAudioConfig>(SkeletonConfigPath);
            Assert.That(config.TryGetClip("Shoot", out CustomClip shoot), Is.True);
            Assert.That(shoot.SpatialBlend, Is.EqualTo(1f), "Precondition: Skeleton Shoot is authored 3D (S5).");

            _manager.PlaySfx(in shoot, new Vector3(1f, 2f, 0f), mode);

            AudioSource played = null;
            foreach (AudioSource source in _manager.GetComponentsInChildren<AudioSource>(true))
                if (source.clip != null) played = source;
            Assert.That(played, Is.Not.Null);
            Assert.That(played.spatialBlend, Is.EqualTo(expectedBlend));
            AssertCentralSettings(played);
        }

        [UnityTest]
        public IEnumerator AudioManagerPrefab_KeepsTheCentralDefaults_WithoutAnAuthoredSpatialBlock()
        {
            Object.DestroyImmediate(_manager.gameObject);
            Assert.That(AudioManager.Instance, Is.Null);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AudioManagerPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Own(Object.Instantiate(prefab));
            yield return null;

            AudioManager manager = instance.GetComponent<AudioManager>();
            Assert.That(manager.SpatialSettings.RolloffMode, Is.EqualTo(AudioRolloffMode.Linear));
            Assert.That(manager.SpatialSettings.MinDistance, Is.EqualTo(2f));
            Assert.That(manager.SpatialSettings.MaxDistance, Is.EqualTo(20f));
            foreach (AudioSource source in instance.transform.Find("SFX_Pool").GetComponentsInChildren<AudioSource>(true))
                AssertCentralSettings(source);
        }
    }
}
#endif

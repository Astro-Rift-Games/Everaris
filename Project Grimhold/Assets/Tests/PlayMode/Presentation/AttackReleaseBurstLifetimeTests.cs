#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.Presentation
{
    public sealed class AttackReleaseBurstLifetimeTests
    {
        private GameObject _root;
        private GameObject _template;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_template != null) Object.Destroy(_template);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Burst_WaitsForChildParticlesThenCleansUpWithoutMutatingPrefab()
        {
            _root = new GameObject("BurstTestRoot");
            _template = new GameObject("BurstTestTemplate");
            var prefab = _template.AddComponent<ParticleSystem>();
            var child = new GameObject("LongerChild");
            child.transform.SetParent(_template.transform, false);
            var childParticles = child.AddComponent<ParticleSystem>();
            Configure(prefab, .05f);
            Configure(childParticles, .3f);
            var pose = new AttackVfxDefinition.ResolvedPose(new Vector3(1f, 2f), Quaternion.Euler(0, 0, 37), Vector3.one, 21);
            AttackReleaseBurstLifetime.Play(prefab, _root.transform, pose, Color.magenta, 0);
            var burst = _root.transform.Find("AttackReleaseBurst");
            Assert.That(burst, Is.Not.Null);
            Assert.That(burst.localPosition, Is.EqualTo(pose.Position));
            Assert.That(Quaternion.Angle(burst.localRotation, pose.Rotation), Is.LessThan(.001f));
            Assert.That(burst.GetComponent<ParticleSystem>().main.startColor.color, Is.EqualTo(Color.magenta));
            yield return new WaitForSeconds(.15f);
            Assert.That(burst != null, Is.True, "Root completion must not truncate a longer child");
            yield return new WaitForSeconds(.5f);
            Assert.That(burst == null, Is.True);
            Assert.That(prefab.main.loop, Is.True, "Shared prefab remains untouched");
            Assert.That(childParticles.main.loop, Is.True);
            Assert.That(prefab.main.startColor.color, Is.EqualTo(Color.white));
        }

        [UnityTest]
        public IEnumerator Burst_NullPrefabIsSilent()
        {
            _root = new GameObject("BurstTestRoot");
            AttackReleaseBurstLifetime.Play(null, _root.transform, default, Color.white, 0);
            yield return null;
            Assert.That(_root.transform.childCount, Is.Zero);
        }

        private static void Configure(ParticleSystem particles, float lifetime)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.duration = .05f;
            main.startLifetime = lifetime;
            main.startColor = Color.white;
            main.loop = true;
            main.playOnAwake = false;
            var emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        }
    }
}
#endif

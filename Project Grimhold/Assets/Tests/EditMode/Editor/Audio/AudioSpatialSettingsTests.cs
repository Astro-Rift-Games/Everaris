#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Audio
{
    public sealed class AudioSpatialSettingsTests
    {
        private GameObject _owner;

        [TearDown]
        public void Cleanup()
        {
            if (_owner != null) Object.DestroyImmediate(_owner);
        }

        private AudioSource NewSource()
        {
            _owner = new GameObject("spatial test source");
            return _owner.AddComponent<AudioSource>();
        }

        [Test]
        public void Defaults_AreLinearTwoToTwentyUnits()
        {
            var settings = new AudioSpatialSettings();

            Assert.That(settings.RolloffMode, Is.EqualTo(AudioRolloffMode.Linear));
            Assert.That(settings.MinDistance, Is.EqualTo(2f));
            Assert.That(settings.MaxDistance, Is.EqualTo(20f));
        }

        [Test]
        public void Apply_OverridesTheUnityDefaultsOfASource()
        {
            AudioSource source = NewSource();
            Assert.That(source.rolloffMode, Is.EqualTo(AudioRolloffMode.Logarithmic), "Precondition: Unity default.");

            new AudioSpatialSettings().Apply(source);

            Assert.That(source.rolloffMode, Is.EqualTo(AudioRolloffMode.Linear));
            Assert.That(source.minDistance, Is.EqualTo(2f));
            Assert.That(source.maxDistance, Is.EqualTo(20f));
        }

        [Test]
        public void Apply_IsOrderIndependent_WhenNewRangeIsAboveTheOldOne()
        {
            AudioSource source = NewSource();
            source.minDistance = 1f;
            source.maxDistance = 5f;

            new AudioSpatialSettings(AudioRolloffMode.Logarithmic, 30f, 90f).Apply(source);

            Assert.That(source.rolloffMode, Is.EqualTo(AudioRolloffMode.Logarithmic));
            Assert.That(source.minDistance, Is.EqualTo(30f));
            Assert.That(source.maxDistance, Is.EqualTo(90f));
        }

        [Test]
        public void Apply_KeepsAValidRange_WhenConfiguredMaxIsNotAboveMin()
        {
            AudioSource source = NewSource();

            new AudioSpatialSettings(AudioRolloffMode.Linear, 8f, 3f).Apply(source);

            Assert.That(source.minDistance, Is.EqualTo(8f));
            Assert.That(source.maxDistance, Is.GreaterThan(source.minDistance));
        }

        [Test]
        public void Apply_IgnoresANullSource()
        {
            Assert.DoesNotThrow(() => new AudioSpatialSettings().Apply(null));
        }

        [Test]
        public void Apply_LeavesSpatialBlendAndVolumeToTheCaller()
        {
            AudioSource source = NewSource();
            source.spatialBlend = 0.4f;
            source.volume = 0.3f;

            new AudioSpatialSettings().Apply(source);

            Assert.That(source.spatialBlend, Is.EqualTo(0.4f));
            Assert.That(source.volume, Is.EqualTo(0.3f));
        }
    }
}
#endif

#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.Audio
{
    public sealed class PlanarAudioListenerTests
    {
        private readonly List<Object> _owned = new();

        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }

        [TearDown]
        public void Cleanup()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        private static List<AudioListener> EnabledListeners()
        {
            var enabled = new List<AudioListener>();
            foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
                if (listener.enabled && listener.gameObject.activeInHierarchy) enabled.Add(listener);
            return enabled;
        }

        private GameObject NewCamera(Vector3 position, bool withOwnListener)
        {
            var camera = Own(new GameObject("Test Camera"));
            camera.transform.position = position;
            if (withOwnListener) camera.AddComponent<AudioListener>();
            return camera;
        }

        [UnityTest]
        public IEnumerator CameraAtMinusTen_YieldsOneEnabledListenerOnTheGameplayPlane()
        {
            Assume.That(EnabledListeners(), Is.Empty, "Another AudioListener is already enabled.");
            GameObject camera = NewCamera(new Vector3(0f, 0f, -10f), withOwnListener: true);
            var planar = camera.AddComponent<PlanarAudioListener>();
            yield return null;

            List<AudioListener> enabled = EnabledListeners();
            Assert.That(enabled, Has.Count.EqualTo(1));
            Assert.That(enabled[0], Is.SameAs(planar.Listener));
            Assert.That(enabled[0].transform.position.z, Is.EqualTo(0f));
            Assert.That(camera.transform.position.z, Is.EqualTo(-10f), "The camera itself must not move.");
        }

        [UnityTest]
        public IEnumerator Listener_FollowsCameraXY_AndStaysOnThePlane_AfterTheCameraMoves()
        {
            Assume.That(EnabledListeners(), Is.Empty, "Another AudioListener is already enabled.");
            GameObject camera = NewCamera(new Vector3(0f, 0f, -10f), withOwnListener: false);
            var planar = camera.AddComponent<PlanarAudioListener>();
            yield return null;

            camera.transform.position = new Vector3(7f, -3f, -10f);
            yield return null;
            Assert.That(planar.Listener.transform.position, Is.EqualTo(new Vector3(7f, -3f, 0f)));

            camera.transform.position = new Vector3(-2f, 5f, -4f);
            yield return null;
            Assert.That(planar.Listener.transform.position, Is.EqualTo(new Vector3(-2f, 5f, 0f)));
            Assert.That(EnabledListeners(), Has.Count.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ThreeDSound_OnTheCameraXY_IsAtZeroListenerDistance()
        {
            Assume.That(EnabledListeners(), Is.Empty, "Another AudioListener is already enabled.");
            GameObject camera = NewCamera(new Vector3(4f, 4f, -10f), withOwnListener: true);
            var planar = camera.AddComponent<PlanarAudioListener>();
            yield return null;

            var soundAtPlayer = new Vector3(4f, 4f, 0f);

            Assert.That(Vector3.Distance(planar.Listener.transform.position, soundAtPlayer), Is.EqualTo(0f).Within(0.0001f));
        }

        [UnityTest]
        public IEnumerator DisablingAndEnablingTheComponent_KeepsExactlyOneEnabledListener()
        {
            Assume.That(EnabledListeners(), Is.Empty, "Another AudioListener is already enabled.");
            GameObject camera = NewCamera(new Vector3(0f, 0f, -10f), withOwnListener: true);
            var planar = camera.AddComponent<PlanarAudioListener>();
            yield return null;

            planar.enabled = false;
            yield return null;
            planar.enabled = true;
            camera.transform.position = new Vector3(1f, 2f, -10f);
            yield return null;

            List<AudioListener> enabled = EnabledListeners();
            Assert.That(enabled, Has.Count.EqualTo(1));
            Assert.That(enabled[0].transform.position, Is.EqualTo(new Vector3(1f, 2f, 0f)));
        }
    }
}
#endif

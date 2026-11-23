#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using ProjectGrimhold.Gameplay.Enemies.Specter;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;

namespace Tests.PlayMode
{
    public sealed class SpecterLeashPlayModeTests
    {
        private const string SpecterPrefabPath = "Assets/Prefabs/Enemies/NetworkEnemySpecter.prefab";
        private readonly List<Object> _owned = new();
        private NetworkRunner _runner;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_runner != null)
            {
                var task = _runner.Shutdown();
                while (!task.IsCompleted) yield return null;
                Object.Destroy(_runner.gameObject);
                _runner = null;
            }

            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                if (_owned[i] != null) Object.Destroy(_owned[i]);
            }
            _owned.Clear();
        }

        [UnityTest]
        public IEnumerator SpecterBeyondLeash_ReturnsToHomeAreaViaFade_WithoutConsumingReactiveCooldown()
        {
            var runnerObj = new GameObject("SpecterLeashTestRunner");
            _runner = runnerObj.AddComponent<NetworkRunner>();
            runnerObj.AddComponent<EntityRegistry>();
            runnerObj.AddComponent<NetworkSceneManagerDefault>();

            var startTask = _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Single,
                SessionName = "SpecterLeashTest_" + System.Guid.NewGuid().ToString("N")
            });
            while (!startTask.IsCompleted) yield return null;
            Assert.That(startTask.Result.Ok, Is.True, startTask.Result.ErrorMessage);

            GameObject specterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpecterPrefabPath);
            Assert.That(specterPrefab, Is.Not.Null);

            Vector3 homePos = new Vector3(0f, 0f, 0f);
            NetworkObject specterObj = _runner.Spawn(specterPrefab, homePos, Quaternion.identity);
            Assert.That(specterObj, Is.Not.Null);

            SpecterAIController specter = specterObj.GetComponent<SpecterAIController>();
            Assert.That(specter, Is.Not.Null);

            // Allow Spawned() and initial tick to settle
            for (int i = 0; i < 5; i++) yield return new WaitForFixedUpdate();

            // Verify EnemyPathfindingNavigator stays enabled on Specter
            EnemyPathfindingNavigator navigator = specter.GetComponent<EnemyPathfindingNavigator>();
            Assert.That(navigator != null && navigator.enabled, Is.True,
                "Specter must keep EnemyPathfindingNavigator enabled after Spawned().");

            // Move Specter past its leash radius (15 u -> 17 u) and force Persecucion with no eligible target
            if (specter.TryGetComponent(out NetworkTransform netTransform))
            {
                netTransform.Teleport(new Vector3(17f, 0f, 0f));
            }
            else
            {
                specter.transform.position = new Vector3(17f, 0f, 0f);
            }

            // Drive state transition to Persecucion so UpdatePersecucion evaluates Leash/no-target return
            specter.TransitionTo(SpecterStateType.Persecucion);

            // Wait for FadeOut (0.4s) + Hidden (0.5s) + FadeIn (0.4s) + margin (~2.0s)
            float deadline = Time.realtimeSinceStartup + 3.0f;
            bool observedFade = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (specter.CurrentState == SpecterStateType.Desvanecimiento ||
                    specter.CurrentState == SpecterStateType.Desvanecido ||
                    specter.CurrentState == SpecterStateType.Reaparicion)
                {
                    observedFade = true;
                }

                if (observedFade && specter.CurrentState == SpecterStateType.Reposo)
                {
                    break;
                }

                yield return new WaitForFixedUpdate();
            }

            Assert.That(observedFade, Is.True,
                "Specter beyond Leash must return to Home Area via Desvanecimiento -> Desvanecido -> Reaparicion.");
            Assert.That(specter.CurrentState, Is.EqualTo(SpecterStateType.Reposo),
                "Specter must finish its Home Area return in Reposo.");
            Assert.That(Vector2.Distance(specter.transform.position, homePos), Is.LessThan(1.0f),
                $"Specter must physically return to HomeAreaPosition {homePos}, but remained at {specter.transform.position}.");
            Assert.That(specter.ReactiveDisplacementTimer.ExpiredOrNotRunning(_runner), Is.True,
                "Home Area return fade must NOT consume ReactiveDisplacementTimer.");
        }
    }
}
#endif

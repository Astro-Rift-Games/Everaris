using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Editor.Player.Movement
{
    [TestFixture]
    public sealed class EnemyLocomotionAndFacingTests
    {
        [TestCase("Assets/Prefabs/Enemies/NetworkEnemy.prefab")]
        [TestCase("Assets/Prefabs/Enemies/Slimes/GreenSlime.prefab")]
        [TestCase("Assets/Prefabs/Enemies/NetworkEnemyRanged.prefab")]
        public void EnemyPrefabs_HavePathSmoothingEnabled_AndConfigurableLocomotionFields(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);

            EnemyPathfindingNavigator navigator = prefab.GetComponent<EnemyPathfindingNavigator>();
            EnemyMovementAIController controller = prefab.GetComponent<EnemyMovementAIController>();
            Assert.That(navigator, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);

            FieldInfo smoothField = typeof(EnemyPathfindingNavigator)
                .GetField("_enablePathSmoothing", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That((bool)smoothField.GetValue(navigator), Is.True,
                $"{prefabPath} must have _enablePathSmoothing enabled to avoid 8-dir zig-zag.");

            FieldInfo accelField = typeof(EnemyMovementAIController)
                .GetField("_acceleration", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo decelField = typeof(EnemyMovementAIController)
                .GetField("_deceleration", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo turnField = typeof(EnemyMovementAIController)
                .GetField("_turnSpeedDegreesPerSecond", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(accelField, Is.Not.Null);
            Assert.That(decelField, Is.Not.Null);
            Assert.That(turnField, Is.Not.Null);

            Assert.That((float)accelField.GetValue(controller), Is.GreaterThan(0f));
            Assert.That((float)decelField.GetValue(controller), Is.GreaterThan(0f));
            Assert.That((float)turnField.GetValue(controller), Is.GreaterThan(0f));
        }

        [Test]
        public void StepVelocity_AcceleratesProgressivelyFromIdle_AndDeceleratesSmoothlyToStop()
        {
            const float dt = 0.02f;
            const float accel = 8.0f;
            const float decel = 12.0f;
            Vector2 velocity = Vector2.zero;
            Vector2 target = Vector2.right * 1.5f;

            // Tick 1: progressive ramp, not instant jump to 1.5
            velocity = EnemyLocomotionRules.StepVelocity(velocity, target, accel, decel, dt);
            Assert.That(velocity.x, Is.EqualTo(accel * dt).Within(0.001f));
            Assert.That(velocity.x, Is.LessThan(target.x));

            // After 15 ticks (0.30s): reaches target speed
            for (int i = 0; i < 15; i++)
            {
                velocity = EnemyLocomotionRules.StepVelocity(velocity, target, accel, decel, dt);
            }
            Assert.That(velocity.x, Is.EqualTo(target.x).Within(0.001f));

            // Braking toward zero (e.g. entering Attack): decelerates smoothly, not instant drop to 0
            velocity = EnemyLocomotionRules.StepVelocity(velocity, Vector2.zero, accel, decel, dt);
            Assert.That(velocity.x, Is.EqualTo(1.5f - decel * dt).Within(0.001f));
            Assert.That(velocity.x, Is.GreaterThan(0f));

            for (int i = 0; i < 10; i++)
            {
                velocity = EnemyLocomotionRules.StepVelocity(velocity, Vector2.zero, accel, decel, dt);
            }
            Assert.That(velocity, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void RotateDirectionTowards_ClampsAngularStepPerTick()
        {
            Vector2 current = Vector2.right;
            Vector2 target = Vector2.up; // +90 degrees
            Vector2 next = EnemyLocomotionRules.RotateDirectionTowards(current, target, maxTurnDegreesPerSecond: 450f, deltaTime: 0.02f);

            float deltaAngle = Vector2.Angle(current, next);
            Assert.That(deltaAngle, Is.EqualTo(9.0f).Within(0.05f),
                "Turn step must be clamped to turnSpeed * dt (450 * 0.02 = 9 degrees).");
        }

        [Test]
        public void ResolveWithHysteresis_PreventsFlickerAcrossDiagonalBoundaries_AndSwitchesBeyondMargin()
        {
            const float margin = 8f;

            // Near 0 deg boundary between NorthEast (>0) and SouthEast (<=0):
            // Previously NorthEast, oscillating slightly to -5 deg stays NorthEast
            Assert.That(
                CharacterVisualDirectionResolver.ResolveWithHysteresis(FromDeg(-5f), CharacterVisualDirection.NorthEast, margin),
                Is.EqualTo(CharacterVisualDirection.NorthEast));

            // Crossing beyond -8 deg switches cleanly to SouthEast
            Assert.That(
                CharacterVisualDirectionResolver.ResolveWithHysteresis(FromDeg(-10f), CharacterVisualDirection.NorthEast, margin),
                Is.EqualTo(CharacterVisualDirection.SouthEast));

            // Wrap-around boundary at +-180 deg between NorthWest and SouthWest:
            // Previously NorthWest, dipping to -175 deg stays NorthWest
            Assert.That(
                CharacterVisualDirectionResolver.ResolveWithHysteresis(FromDeg(-175f), CharacterVisualDirection.NorthWest, margin),
                Is.EqualTo(CharacterVisualDirection.NorthWest));

            // Crossing beyond -172 deg switches to SouthWest
            Assert.That(
                CharacterVisualDirectionResolver.ResolveWithHysteresis(FromDeg(-170f), CharacterVisualDirection.NorthWest, margin),
                Is.EqualTo(CharacterVisualDirection.SouthWest));
        }

        private static Vector2 FromDeg(float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }
    }
}

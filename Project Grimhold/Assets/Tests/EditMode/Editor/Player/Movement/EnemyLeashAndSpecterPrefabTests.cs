using System.Reflection;
using NUnit.Framework;
using ProjectGrimhold.Gameplay.Enemies.Specter;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Editor.Player.Movement
{
    [TestFixture]
    public sealed class EnemyLeashAndSpecterPrefabTests
    {
        [Test]
        public void SpecterPrefab_HasEnemyPathfindingNavigatorEnabled()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Enemies/NetworkEnemySpecter.prefab");
            Assert.That(prefab, Is.Not.Null);

            EnemyPathfindingNavigator navigator = prefab.GetComponent<EnemyPathfindingNavigator>();
            Assert.That(navigator, Is.Not.Null);
            Assert.That(navigator.enabled, Is.True,
                "Specter prefab must keep EnemyPathfindingNavigator enabled so it navigates around walls.");
        }

        [TestCase("Assets/Prefabs/Enemies/Slimes/GreenSlime.prefab", 12f)]
        [TestCase("Assets/Prefabs/Enemies/NetworkEnemyRanged.prefab", 14f)]
        public void SlimeAndRangedPrefabs_HaveConfigurablePositiveLeashRadius(string prefabPath, float expectedDefault)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);

            EnemyMovementAIController controller = prefab.GetComponent<EnemyMovementAIController>();
            Assert.That(controller, Is.Not.Null);

            FieldInfo leashField = typeof(EnemyMovementAIController)
                .GetField("_leashRadius", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(leashField, Is.Not.Null, "EnemyMovementAIController must define serialized _leashRadius.");

            float leashRadius = (float)leashField.GetValue(controller);
            Assert.That(leashRadius, Is.EqualTo(expectedDefault).Within(0.01f));
        }

        [Test]
        public void EnemyLeashRules_BeyondLeash_TriggersAndBlocksReengageAtBorder()
        {
            Vector2 home = Vector2.zero;
            const float leash = 12f;
            EntityId playerA = new EntityId(10);
            EntityId playerB = new EntityId(20);

            Assert.That(EnemyLeashRules.IsBeyondLeash(new Vector2(11.9f, 0f), home, leash), Is.False);
            Assert.That(EnemyLeashRules.IsBeyondLeash(new Vector2(12.1f, 0f), home, leash), Is.True);

            // Still outside leash: block ANY target so enemy does not oscillate at border
            Assert.That(EnemyLeashRules.ShouldBlockReengage(
                playerB, playerA, isReturningHome: true, isLockoutTimerActive: false,
                currentPosition: new Vector2(12.5f, 0f), home, leash, 0.75f), Is.True);

            // Inside leash, lockout active: leashed player blocked
            Assert.That(EnemyLeashRules.ShouldBlockReengage(
                playerA, playerA, isReturningHome: true, isLockoutTimerActive: true,
                currentPosition: new Vector2(5f, 0f), home, leash, 0.75f), Is.True);

            // Inside leash near outer edge (> 0.75 * 12 = 9), lockout expired: leashed player still blocked to prevent oscillation
            Assert.That(EnemyLeashRules.ShouldBlockReengage(
                playerA, playerA, isReturningHome: true, isLockoutTimerActive: false,
                currentPosition: new Vector2(10f, 0f), home, leash, 0.75f), Is.True);

            // Inside inner radius (<= 9) and lockout expired: re-engage allowed again
            Assert.That(EnemyLeashRules.ShouldBlockReengage(
                playerA, playerA, isReturningHome: true, isLockoutTimerActive: false,
                currentPosition: new Vector2(8f, 0f), home, leash, 0.75f), Is.False);

            // After arriving home (isReturningHome == false): detection fully re-armed
            Assert.That(EnemyLeashRules.HasArrivedAtHome(new Vector2(0.5f, 0f), home, 1.0f), Is.True);
            Assert.That(EnemyLeashRules.ShouldBlockReengage(
                playerA, playerA, isReturningHome: false, isLockoutTimerActive: false,
                currentPosition: new Vector2(0.5f, 0f), home, leash, 0.75f), Is.False);
        }

        [Test]
        public void SpecterRules_ShouldReturnToHomeViaFade_MatchesDesignSpec()
        {
            Vector2 home = Vector2.zero;

            // Beyond leash -> must return via fade even if a target is eligible
            Assert.That(SpecterRules.ShouldReturnToHomeViaFade(new Vector2(16f, 0f), home, 15f, hasEligibleTarget: true), Is.True);
            // Within leash with target -> keep chasing
            Assert.That(SpecterRules.ShouldReturnToHomeViaFade(new Vector2(10f, 0f), home, 15f, hasEligibleTarget: true), Is.False);
            // No eligible target and away from home -> return via fade
            Assert.That(SpecterRules.ShouldReturnToHomeViaFade(new Vector2(6f, 0f), home, 15f, hasEligibleTarget: false), Is.True);
            // No eligible target and already at home -> enter Reposo directly without fading
            Assert.That(SpecterRules.ShouldReturnToHomeViaFade(new Vector2(0.4f, 0f), home, 15f, hasEligibleTarget: false), Is.False);
        }
    }
}

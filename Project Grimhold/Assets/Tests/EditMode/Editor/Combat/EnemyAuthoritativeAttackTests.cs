using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Editor.Combat
{
    [TestFixture]
    public sealed class EnemyAuthoritativeAttackTests
    {
        [TestCase("Assets/Prefabs/Enemies/Slimes/GreenSlime.prefab", 0.35f, 0.35f)]
        [TestCase("Assets/Prefabs/Enemies/NetworkEnemyRanged.prefab", 0.40f, 0.75f)]
        public void EnemyPrefabs_HaveConfigurableAnticipationDurationAndRangeHysteresis(
            string prefabPath,
            float expectedAnticipation,
            float expectedExitMargin)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);

            EnemyCombatAIController combat = prefab.GetComponent<EnemyCombatAIController>();
            EnemyMovementAIController movement = prefab.GetComponent<EnemyMovementAIController>();
            Assert.That(combat, Is.Not.Null);
            Assert.That(movement, Is.Not.Null);

            FieldInfo anticipationField = typeof(EnemyCombatAIController)
                .GetField("_anticipationDurationSeconds", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo exitMarginField = typeof(EnemyMovementAIController)
                .GetField("_attackExitRangeMargin", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo minStayField = typeof(EnemyMovementAIController)
                .GetField("_minAttackStateDurationSeconds", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(anticipationField, Is.Not.Null);
            Assert.That(exitMarginField, Is.Not.Null);
            Assert.That(minStayField, Is.Not.Null);

            Assert.That((float)anticipationField.GetValue(combat), Is.EqualTo(expectedAnticipation).Within(0.01f));
            Assert.That((float)exitMarginField.GetValue(movement), Is.EqualTo(expectedExitMargin).Within(0.01f));
            Assert.That((float)minStayField.GetValue(movement), Is.GreaterThan(0f));
        }

        [Test]
        public void EnemyCombatAIController_ExposesReplicatedAnticipationObservationProperties()
        {
            PropertyInfo preparingProp = typeof(EnemyCombatAIController).GetProperty("IsPreparingAttack");
            PropertyInfo directionProp = typeof(EnemyCombatAIController).GetProperty("PreparedAttackDirection");
            PropertyInfo sequenceProp = typeof(EnemyCombatAIController).GetProperty("PreparationSequence");

            Assert.That(preparingProp, Is.Not.Null, "Proxies must observe IsPreparingAttack.");
            Assert.That(directionProp, Is.Not.Null, "Proxies must observe PreparedAttackDirection.");
            Assert.That(sequenceProp, Is.Not.Null, "Proxies must observe PreparationSequence.");
        }

        [Test]
        public void AnticipationRules_StartContinueAndExecute_WithoutAnyAnimationEvent()
        {
            // 1. Start preparation when requested, ready, valid target, and clear LOS
            EnemyAnticipationStepOutcome start = EnemyAttackAnticipationRules.EvaluateStep(
                isAlive: true, isStunned: false, isAttackEnabled: true, isAttackRequested: true,
                isPreparing: false, hasValidTarget: true, hasLineOfSight: true,
                anticipationExpired: false, cooldownReady: true);
            Assert.That(start, Is.EqualTo(EnemyAnticipationStepOutcome.StartPreparation));

            // 2. Before anticipation timer expires: continues preparing, NEVER executes damage early
            EnemyAnticipationStepOutcome mid = EnemyAttackAnticipationRules.EvaluateStep(
                isAlive: true, isStunned: false, isAttackEnabled: true, isAttackRequested: true,
                isPreparing: true, hasValidTarget: true, hasLineOfSight: true,
                anticipationExpired: false, cooldownReady: true);
            Assert.That(mid, Is.EqualTo(EnemyAnticipationStepOutcome.ContinuePreparation));

            // 3. When TickTimer expires: executes authoritatively with zero animation event needed
            EnemyAnticipationStepOutcome fire = EnemyAttackAnticipationRules.EvaluateStep(
                isAlive: true, isStunned: false, isAttackEnabled: true, isAttackRequested: true,
                isPreparing: true, hasValidTarget: true, hasLineOfSight: true,
                anticipationExpired: true, cooldownReady: true);
            Assert.That(fire, Is.EqualTo(EnemyAnticipationStepOutcome.Execute));
        }

        [Test]
        public void AnticipationRules_InvalidTargetOrStunOrLostLos_CancelsPreparationCleanly()
        {
            Assert.That(EnemyAttackAnticipationRules.EvaluateStep(
                isAlive: true, isStunned: false, isAttackEnabled: true, isAttackRequested: true,
                isPreparing: true, hasValidTarget: false, hasLineOfSight: true,
                anticipationExpired: false, cooldownReady: true),
                Is.EqualTo(EnemyAnticipationStepOutcome.Cancel));

            Assert.That(EnemyAttackAnticipationRules.EvaluateStep(
                isAlive: true, isStunned: true, isAttackEnabled: true, isAttackRequested: true,
                isPreparing: true, hasValidTarget: true, hasLineOfSight: true,
                anticipationExpired: true, cooldownReady: true),
                Is.EqualTo(EnemyAnticipationStepOutcome.Cancel));

            Assert.That(EnemyAttackAnticipationRules.EvaluateStep(
                isAlive: true, isStunned: false, isAttackEnabled: true, isAttackRequested: true,
                isPreparing: true, hasValidTarget: true, hasLineOfSight: false,
                anticipationExpired: true, cooldownReady: true),
                Is.EqualTo(EnemyAnticipationStepOutcome.Cancel));
        }

        [Test]
        public void RangeHysteresis_PreventsChaseAttackFlickerAtAttackBoundary()
        {
            const float enterRange = 1.25f;
            const float exitMargin = 0.35f;
            const float disengageRange = 6.0f;

            // Not attacking: threshold is exact enterRange (1.25)
            float enterThreshold = EnemyRangeHysteresisRules.ResolveEffectiveAttackRange(
                currentlyAttacking: false, enterRange, exitMargin, minStayActive: false, disengageRange);
            Assert.That(enterThreshold, Is.EqualTo(1.25f).Within(0.001f));

            // Attacking without minStay: threshold widens to 1.60 so a player jittering at 1.26..1.55 stays in Attack
            float exitThreshold = EnemyRangeHysteresisRules.ResolveEffectiveAttackRange(
                currentlyAttacking: true, enterRange, exitMargin, minStayActive: false, disengageRange);
            Assert.That(exitThreshold, Is.EqualTo(1.60f).Within(0.001f));

            // Attacking during minStay: widens further (1.775) up to disengageRange
            float minStayThreshold = EnemyRangeHysteresisRules.ResolveEffectiveAttackRange(
                currentlyAttacking: true, enterRange, exitMargin, minStayActive: true, disengageRange);
            Assert.That(minStayThreshold, Is.GreaterThan(exitThreshold));
        }
    }
}

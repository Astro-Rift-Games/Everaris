using System.Reflection;
using NUnit.Framework;
using ProjectGrimhold.Gameplay.Enemies.Specter;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Editor.Player.Movement
{
    [TestFixture]
    public sealed class EnemyVisionAndLosTests
    {
        private const int WorldCollisionLayer = 6;
        private const int WorldCollisionBit = 1 << WorldCollisionLayer;

        private static readonly string[] MovementEnemyPrefabPaths =
        {
            "Assets/Prefabs/Enemies/NetworkEnemy.prefab",
            "Assets/Prefabs/Enemies/Slimes/GreenSlime.prefab",
            "Assets/Prefabs/Enemies/Slimes/BlueSlime.prefab",
            "Assets/Prefabs/Enemies/Slimes/RedSlime.prefab",
            "Assets/Prefabs/Enemies/NetworkEnemyRanged.prefab"
        };

        private GameObject _wall;

        [TearDown]
        public void TearDown()
        {
            if (_wall != null)
            {
                Object.DestroyImmediate(_wall);
                _wall = null;
            }
        }

        [TestCaseSource(nameof(MovementEnemyPrefabPaths))]
        public void RaidEnemyPrefab_ObstacleLayer_IncludesWorldCollision(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"Missing prefab at {prefabPath}");

            EnemyMovementAIController controller = prefab.GetComponent<EnemyMovementAIController>();
            Assert.That(controller, Is.Not.Null, $"Missing {nameof(EnemyMovementAIController)} on {prefabPath}");

            LayerMask obstacleLayer = GetPrivateField<LayerMask>(controller, "_obstacleLayer");
            Assert.That(
                EnemyVisionRules.IncludesWorldCollision(obstacleLayer),
                Is.True,
                $"{prefabPath} _obstacleLayer ({obstacleLayer.value}) must include WorldCollision (layer {WorldCollisionLayer}).");
        }

        [Test]
        public void SpecterPrefab_ObstacleLayer_IncludesWorldCollision()
        {
            const string specterPath = "Assets/Prefabs/Enemies/NetworkEnemySpecter.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(specterPath);
            Assert.That(prefab, Is.Not.Null);

            SpecterAIController specter = prefab.GetComponent<SpecterAIController>();
            Assert.That(specter, Is.Not.Null);

            LayerMask obstacleLayer = GetPrivateField<LayerMask>(specter, "_obstacleLayer");
            Assert.That((obstacleLayer.value & WorldCollisionBit) != 0, Is.True);
        }

        [Test]
        public void LineOfSight_UsingSlimeAndRangedPrefabMask_IsBlockedByWorldCollisionWall_AndClearWithoutWall()
        {
            GameObject slimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Slimes/GreenSlime.prefab");
            GameObject rangedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/NetworkEnemyRanged.prefab");
            LayerMask slimeMask = GetPrivateField<LayerMask>(slimePrefab.GetComponent<EnemyMovementAIController>(), "_obstacleLayer");
            LayerMask rangedMask = GetPrivateField<LayerMask>(rangedPrefab.GetComponent<EnemyMovementAIController>(), "_obstacleLayer");

            Vector2 enemyPos = new Vector2(100f, 100f);
            Vector2 playerPos = new Vector2(103f, 100f);

            // Clear LOS without wall
            Physics2D.SyncTransforms();
            Assert.That(EnemyVisionRules.HasLineOfSight(enemyPos, playerPos, slimeMask), Is.True);
            Assert.That(EnemyVisionRules.HasLineOfSight(enemyPos, playerPos, rangedMask), Is.True);

            // Solid wall on WorldCollision between enemy and player
            _wall = new GameObject("TestWorldCollisionWall", typeof(BoxCollider2D)) { layer = WorldCollisionLayer };
            _wall.transform.position = new Vector2(101.5f, 100f);
            _wall.GetComponent<BoxCollider2D>().size = new Vector2(0.5f, 4f);
            Physics2D.SyncTransforms();

            Assert.That(EnemyVisionRules.HasLineOfSight(enemyPos, playerPos, slimeMask), Is.False,
                "Slime obstacle mask must block vision through WorldCollision wall.");
            Assert.That(EnemyVisionRules.HasLineOfSight(enemyPos, playerPos, rangedMask), Is.False,
                "Ranged obstacle mask must block vision through WorldCollision wall.");
        }

        [Test]
        public void TargetWithinAttackRangeBehindWall_CannotAttack_AndDisengagesAfterGracePeriod()
        {
            const int graceTicks = 10;
            int lostTicks = 0;

            for (int tick = 1; tick < graceTicks; tick++)
            {
                EnemyTargetEngagementDecision decision = EnemyVisionRules.EvaluateTargetEngagement(
                    distance: 1.0f,
                    disengageRange: 6.0f,
                    attackRangeThreshold: 5.0f,
                    hasLineOfSight: false,
                    aggroActive: false,
                    isStunned: false,
                    currentPursuitLostTicks: lostTicks,
                    pursuitLostGraceTicks: graceTicks);

                Assert.That(decision.IsAttacking, Is.False, "Must never attack through a wall.");
                Assert.That(decision.DropTarget, Is.False, "Must remain in grace period before graceTicks.");
                Assert.That(decision.IsOnPursuit, Is.True);
                lostTicks = decision.NextPursuitLostTickCount;
            }

            EnemyTargetEngagementDecision finalDecision = EnemyVisionRules.EvaluateTargetEngagement(
                distance: 1.0f,
                disengageRange: 6.0f,
                attackRangeThreshold: 5.0f,
                hasLineOfSight: false,
                aggroActive: false,
                isStunned: false,
                currentPursuitLostTicks: lostTicks,
                pursuitLostGraceTicks: graceTicks);

            Assert.That(finalDecision.DropTarget, Is.True, "Must disengage after grace period expires.");
            Assert.That(finalDecision.IsOnPursuit, Is.False);
            Assert.That(finalDecision.IsAttacking, Is.False);
        }

        [Test]
        public void TargetWithinAttackRangeBehindWall_EvenWithAggro_NeverAttacks()
        {
            EnemyTargetEngagementDecision decision = EnemyVisionRules.EvaluateTargetEngagement(
                distance: 2.0f,
                disengageRange: 6.0f,
                attackRangeThreshold: 5.0f,
                hasLineOfSight: false,
                aggroActive: true,
                isStunned: false,
                currentPursuitLostTicks: 0,
                pursuitLostGraceTicks: 10);

            Assert.That(decision.DropTarget, Is.False);
            Assert.That(decision.IsOnPursuit, Is.True);
            Assert.That(decision.IsAttacking, Is.False, "Aggro enables pursuit around cover, never shooting/striking through walls.");
        }

        [Test]
        public void TargetWithinAttackRangeWithClearLos_EntersAttackAndClearsAggro()
        {
            EnemyTargetEngagementDecision decision = EnemyVisionRules.EvaluateTargetEngagement(
                distance: 1.2f,
                disengageRange: 6.0f,
                attackRangeThreshold: 1.25f,
                hasLineOfSight: true,
                aggroActive: true,
                isStunned: false,
                currentPursuitLostTicks: 5,
                pursuitLostGraceTicks: 10);

            Assert.That(decision.DropTarget, Is.False);
            Assert.That(decision.IsOnPursuit, Is.True);
            Assert.That(decision.IsAttacking, Is.True);
            Assert.That(decision.NextPursuitLostTickCount, Is.EqualTo(0));
            Assert.That(decision.ShouldClearAggro, Is.True);
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field {fieldName} not found on {instance.GetType().Name}");
            return (T)field.GetValue(instance);
        }
    }
}

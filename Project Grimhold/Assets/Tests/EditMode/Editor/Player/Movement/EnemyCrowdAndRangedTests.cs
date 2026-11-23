using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Editor.Player.Movement
{
    [TestFixture]
    public sealed class EnemyCrowdAndRangedTests
    {
        private const int WorldCollisionLayer = 6;
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        [Test]
        public void SlimePrefab_HasConfigurableCrowdSeparationAndSurroundFields()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Enemies/Slimes/GreenSlime.prefab");
            Assert.That(prefab, Is.Not.Null);

            EnemyMovementAIController controller = prefab.GetComponent<EnemyMovementAIController>();
            Assert.That(controller, Is.Not.Null);

            FieldInfo sepRadiusField = typeof(EnemyMovementAIController)
                .GetField("_separationRadius", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo slotRadiusField = typeof(EnemyMovementAIController)
                .GetField("_surroundSlotRadius", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo maxSlotsField = typeof(EnemyMovementAIController)
                .GetField("_maxSurroundSlots", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(sepRadiusField, Is.Not.Null);
            Assert.That(slotRadiusField, Is.Not.Null);
            Assert.That(maxSlotsField, Is.Not.Null);

            Assert.That((float)sepRadiusField.GetValue(controller), Is.GreaterThan(0.8f));
            Assert.That((float)slotRadiusField.GetValue(controller), Is.GreaterThan(0.5f));
            Assert.That((int)maxSlotsField.GetValue(controller), Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void RangedPrefab_HasConfigurablePreferredBandAndLosRepositioningEnabled()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Enemies/NetworkEnemyRanged.prefab");
            Assert.That(prefab, Is.Not.Null);

            EnemyMovementAIController controller = prefab.GetComponent<EnemyMovementAIController>();
            Assert.That(controller, Is.Not.Null);

            FieldInfo useRangedField = typeof(EnemyMovementAIController)
                .GetField("_useRangedPositioning", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo retreatField = typeof(EnemyMovementAIController)
                .GetField("_rangedRetreatDistance", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo prefMinField = typeof(EnemyMovementAIController)
                .GetField("_rangedPreferredMinDistance", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That((bool)useRangedField.GetValue(controller), Is.True);
            Assert.That((float)retreatField.GetValue(controller), Is.EqualTo(2.5f).Within(0.01f));
            Assert.That((float)prefMinField.GetValue(controller), Is.EqualTo(3.0f).Within(0.01f));
        }

        [Test]
        public void CrowdRules_ThreeSlimesOnStationaryTarget_ReceiveDistinctNonOverlappingSlots()
        {
            Vector2 targetPos = new Vector2(10f, 10f);
            var peers = new List<EntityId> { new EntityId(101), new EntityId(102), new EntityId(103) };

            Assert.That(EnemyCrowdRules.TryAssignSurroundSlot(peers[0], targetPos, peers, 1.05f, 6, null, null, out Vector2 slot0), Is.True);
            Assert.That(EnemyCrowdRules.TryAssignSurroundSlot(peers[1], targetPos, peers, 1.05f, 6, null, null, out Vector2 slot1), Is.True);
            Assert.That(EnemyCrowdRules.TryAssignSurroundSlot(peers[2], targetPos, peers, 1.05f, 6, null, null, out Vector2 slot2), Is.True);

            Assert.That(Vector2.Distance(slot0, slot1), Is.GreaterThan(0.8f));
            Assert.That(Vector2.Distance(slot1, slot2), Is.GreaterThan(0.8f));
            Assert.That(Vector2.Distance(slot0, slot2), Is.GreaterThan(0.8f));
        }

        [Test]
        public void CrowdRules_SoloEnemy_PreservesDirectPursuit()
        {
            var solo = new List<EntityId> { new EntityId(101) };
            bool assigned = EnemyCrowdRules.TryAssignSurroundSlot(
                solo[0], new Vector2(10f, 10f), solo, 1.05f, 6, null, null, out _);

            Assert.That(assigned, Is.False, "A solo pursuer must pursue the target directly.");
        }

        [Test]
        public void CrowdRules_NarrowCorridorSeparation_PreservesForwardSingleFileFlow()
        {
            var peers = new List<EnemyCrowdPeerSnapshot>
            {
                new EnemyCrowdPeerSnapshot(new EntityId(1), new Vector2(0f, 0f)),
                new EnemyCrowdPeerSnapshot(new EntityId(2), new Vector2(0.5f, 0.1f))
            };

            Vector2 intendedForward = Vector2.right;
            Vector2 sep = EnemyCrowdRules.ComputeSeparationVector(
                selfPos: new Vector2(0f, 0f),
                selfId: new EntityId(1),
                intendedDirection: intendedForward,
                peers: peers,
                separationRadius: 0.95f,
                separationStrength: 0.75f,
                inNarrowCorridor: true);

            Vector2 blended = EnemyCrowdRules.BlendDirectionWithSeparation(intendedForward, sep);
            Assert.That(blended.x, Is.GreaterThan(0.5f),
                "Enemies in a 1-tile corridor must keep advancing single-file without reversing or jamming into walls.");
            Assert.That(Mathf.Abs(sep.y), Is.LessThan(0.2f),
                "Lateral separation must be attenuated inside a narrow corridor.");
        }

        [Test]
        public void RangedRules_DistanceBandHysteresis_AndRetreatRespectsWallsAndLeash()
        {
            // Below retreat threshold -> Retreat
            Assert.That(EnemyRangedPositioningRules.EvaluateIntent(2.0f, true, 2.5f, 3.0f, 5.0f, false),
                Is.EqualTo(EnemyRangedMovementIntent.Retreat));
            // Hysteresis: still retreating at 2.7 until reaching 3.0
            Assert.That(EnemyRangedPositioningRules.EvaluateIntent(2.7f, true, 2.5f, 3.0f, 5.0f, true),
                Is.EqualTo(EnemyRangedMovementIntent.Retreat));
            // In preferred band -> HoldAndAttack
            Assert.That(EnemyRangedPositioningRules.EvaluateIntent(4.0f, true, 2.5f, 3.0f, 5.0f, false),
                Is.EqualTo(EnemyRangedMovementIntent.HoldAndAttack));
            // Without LOS -> RepositionForLos (never HoldAndAttack)
            Assert.That(EnemyRangedPositioningRules.EvaluateIntent(4.0f, false, 2.5f, 3.0f, 5.0f, false),
                Is.EqualTo(EnemyRangedMovementIntent.RepositionForLos));

            // Open space retreat moves away from player within leash
            Vector2 enemyPos = new Vector2(0f, 0f);
            Vector2 playerPos = new Vector2(1.5f, 0f);
            Assert.That(EnemyRangedPositioningRules.TryComputeRetreatDestination(
                enemyPos, playerPos, homePos: Vector2.zero, leashRadius: 14f, stepDistance: 1.5f,
                isPositionWalkable: _ => true, hasLineOfSight: (_, _) => true, out Vector2 retreatPos), Is.True);
            Assert.That(Vector2.Distance(retreatPos, playerPos), Is.GreaterThan(Vector2.Distance(enemyPos, playerPos)));

            // Cornered (no walkable space around) -> returns false so enemy holds ground instead of pushing into walls
            Assert.That(EnemyRangedPositioningRules.TryComputeRetreatDestination(
                enemyPos, playerPos, homePos: Vector2.zero, leashRadius: 14f, stepDistance: 1.5f,
                isPositionWalkable: _ => false, hasLineOfSight: (_, _) => true, out _), Is.False);
        }

        [Test]
        public void RangedRules_PlayerBehindPillar_FindsWalkableVantagePointWithLosInsideLeash()
        {
            // Pillar blocking direct line from enemy (200, 200) to player (204, 200)
            var pillar = new GameObject("TestPillar", typeof(BoxCollider2D)) { layer = WorldCollisionLayer };
            pillar.transform.position = new Vector2(202f, 200f);
            pillar.GetComponent<BoxCollider2D>().size = new Vector2(1f, 1.5f);
            _created.Add(pillar);
            Physics2D.SyncTransforms();

            LayerMask wallMask = 1 << WorldCollisionLayer;
            Vector2 enemyPos = new Vector2(200f, 200f);
            Vector2 playerPos = new Vector2(204f, 200f);
            Vector2 homePos = enemyPos;

            Assert.That(EnemyVisionRules.HasLineOfSight(enemyPos, playerPos, wallMask), Is.False);

            bool found = EnemyRangedPositioningRules.TryFindVantagePointWithLos(
                enemyPos,
                playerPos,
                homePos,
                leashRadius: 14f,
                preferredMinDistance: 3.0f,
                attackRange: 5.0f,
                searchRadius: 6.0f,
                isPositionWalkable: pt => Physics2D.OverlapCircle(pt, 0.42f, wallMask) == null,
                hasLineOfSight: (a, b) => EnemyVisionRules.HasLineOfSight(a, b, wallMask),
                out Vector2 vantage);

            Assert.That(found, Is.True, "Ranged enemy must find a clear firing vantage point around a pillar.");
            Assert.That(EnemyVisionRules.HasLineOfSight(vantage, playerPos, wallMask), Is.True,
                "Chosen vantage point must have unobstructed line of sight to the player.");
            Assert.That(EnemyLeashRules.IsBeyondLeash(vantage, homePos, 14f), Is.False,
                "Vantage point must stay within Leash.");
        }
    }
}

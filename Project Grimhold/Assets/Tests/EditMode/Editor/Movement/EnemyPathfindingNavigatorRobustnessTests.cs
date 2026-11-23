using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Tests.EditMode.Movement
{
    [TestFixture]
    public sealed class EnemyPathfindingNavigatorRobustnessTests
    {
        private const int WorldCollisionLayer = 6;
        private readonly List<Object> _createdObjects = new();
        private PathfindingGridConfig _config;
        private Tile _tile;

        [TearDown]
        public void TearDown()
        {
            for (int i = _createdObjects.Count - 1; i >= 0; i--)
            {
                if (_createdObjects[i] != null)
                {
                    Object.DestroyImmediate(_createdObjects[i]);
                }
            }
            _createdObjects.Clear();

            if (_config != null)
            {
                Object.DestroyImmediate(_config);
                _config = null;
            }

            if (_tile != null)
            {
                Object.DestroyImmediate(_tile);
                _tile = null;
            }
        }

        [Test]
        public void UnreachableTarget_ApproachesClosestReachablePoint_AndDoesNotRetryEveryTick()
        {
            PathfindingGrid grid = CreateGrid(12, 12);
            // Full horizontal wall sealing y = 6 from x = 0 to x = 12
            CreateWall("SealWall", new Vector2(6f, 6f), new Vector2(12f, 1f));
            Physics2D.SyncTransforms();
            grid.Build();

            EnemyPathfindingNavigator nav = CreateNavigator(repathIntervalSeconds: 0.5f);
            nav.Initialize(null);

            Vector2 start = new Vector2(6f, 2f);
            Vector2 sealedTarget = new Vector2(6f, 10f);

            // Tick 1: should move north toward the closest reachable node near the wall (y ~ 5), NOT stall at Vector2.zero
            Vector2 dirTick1 = nav.GetDirectionToTarget(start, sealedTarget, currentTick: 1);
            Assert.That(dirTick1.sqrMagnitude, Is.GreaterThan(0.5f),
                "When target is sealed off, enemy must approach the closest reachable node instead of freezing.");
            Assert.That(dirTick1.y, Is.GreaterThan(0.5f));

            // Tick 2: must respect repath cadence (0.5s = 25 ticks at 50Hz), NOT overwrite _lastRepathTick every tick
            nav.GetDirectionToTarget(start, sealedTarget, currentTick: 2);
            int lastRepathTick = GetPrivateField<int>(nav, "_lastRepathTick");
            Assert.That(lastRepathTick, Is.EqualTo(1),
                "Navigator must not rerun A* on consecutive tick 2 when target is unreachable.");
        }

        [Test]
        public void ReachingFinalWaypoint_ExhaustsPathInsteadOfStayingValidForever()
        {
            PathfindingGrid grid = CreateGrid(10, 10);
            Physics2D.SyncTransforms();
            grid.Build();

            EnemyPathfindingNavigator nav = CreateNavigator(repathIntervalSeconds: 2.0f);
            nav.Initialize(null);

            Vector2 start = new Vector2(2f, 2f);
            Vector2 goal = new Vector2(5f, 2f);

            Vector2 dir = nav.GetDirectionToTarget(start, goal, currentTick: 1);
            Assert.That(dir.sqrMagnitude, Is.GreaterThan(0.5f));

            // Move right onto the goal on tick 2 (well before repath interval)
            Vector2 dirAtGoal = nav.GetDirectionToTarget(goal - new Vector2(0.1f, 0f), goal, currentTick: 2);
            Assert.That(dirAtGoal, Is.EqualTo(Vector2.zero),
                "Upon reaching the final waypoint within reach radius, navigator must report arrival (Vector2.zero).");
            Assert.That(nav.HasValidPath, Is.False,
                "Path must be marked exhausted once the final waypoint is reached.");
            Assert.That(nav.HasReachedDestination, Is.True,
                "Navigator must report HasReachedDestination == true on final waypoint arrival.");
        }

        [Test]
        public void NoProgressForStuckTimeout_FlagsIsStuck_AndMovingResetsIt()
        {
            PathfindingGrid grid = CreateGrid(10, 10);
            Physics2D.SyncTransforms();
            grid.Build();

            EnemyPathfindingNavigator nav = CreateNavigator(repathIntervalSeconds: 2.0f);
            SetField(nav, "_stuckTimeoutSeconds", 0.5f); // 25 ticks at default 0.02s dt
            SetField(nav, "_stuckMinProgressDistance", 0.25f);
            nav.Initialize(null, hasStateAuthority: true, staggerSeedOverride: 0);

            Vector2 stuckPos = new Vector2(2f, 2f);
            Vector2 goal = new Vector2(8f, 2f);

            for (int tick = 1; tick <= 24; tick++)
            {
                nav.GetDirectionToTarget(stuckPos, goal, currentTick: tick);
                Assert.That(nav.IsStuck, Is.False, $"Should not be stuck yet at tick {tick}.");
            }

            // At tick 26 (25 ticks elapsed from anchor tick 1), must flag IsStuck
            nav.GetDirectionToTarget(stuckPos, goal, currentTick: 26);
            Assert.That(nav.IsStuck, Is.True, "Navigator must report IsStuck after stuckTimeoutSeconds without progress.");

            // Moving beyond _stuckMinProgressDistance clears IsStuck
            nav.GetDirectionToTarget(stuckPos + new Vector2(0.5f, 0f), goal, currentTick: 27);
            Assert.That(nav.IsStuck, Is.False, "Meaningful progress must clear IsStuck.");
        }

        [Test]
        public void MultipleNavigatorsActivatedOnSameTick_StaggerInitialPathRequests()
        {
            PathfindingGrid grid = CreateGrid(10, 10);
            Physics2D.SyncTransforms();
            grid.Build();

            EnemyPathfindingNavigator navA = CreateNavigator(repathIntervalSeconds: 0.5f); // 25 ticks
            EnemyPathfindingNavigator navB = CreateNavigator(repathIntervalSeconds: 0.5f);
            navA.Initialize(null, hasStateAuthority: true, staggerSeedOverride: 0);
            navB.Initialize(null, hasStateAuthority: true, staggerSeedOverride: 5);

            Vector2 start = new Vector2(2f, 2f);
            Vector2 goal = new Vector2(8f, 2f);

            // Both activated on tick 10: navA runs immediately (offset 0), navB waits until tick 15 (offset 5)
            navA.GetDirectionToTarget(start, goal, currentTick: 10);
            navB.GetDirectionToTarget(start, goal, currentTick: 10);

            Assert.That(GetPrivateField<int>(navA, "_lastRepathTick"), Is.EqualTo(10));
            Assert.That(GetPrivateField<int>(navB, "_lastRepathTick"), Is.EqualTo(int.MinValue),
                "Second enemy activated on the same tick must defer its first A* call by its stagger offset.");

            navB.GetDirectionToTarget(start, goal, currentTick: 15);
            Assert.That(GetPrivateField<int>(navB, "_lastRepathTick"), Is.EqualTo(15));
        }

        [Test]
        public void ClosingDoorOnActivePath_InvalidatesCachedPathOnNextTick()
        {
            PathfindingGrid grid = CreateGrid(12, 12);
            // Horizontal wall with a doorway gap at x in [5.5, 6.5] and an open detour around x = 10.5
            CreateWall("LeftWall", new Vector2(2.75f, 6f), new Vector2(5.5f, 1f));   // x:[0, 5.5]
            CreateWall("RightWall", new Vector2(8.0f, 6f), new Vector2(3.0f, 1f));   // x:[6.5, 9.5]
            GameObject door = CreateWall("DoorBlocker", new Vector2(6.0f, 6f), new Vector2(1.0f, 1f));
            BoxCollider2D doorCollider = door.GetComponent<BoxCollider2D>();
            doorCollider.enabled = false; // Initially open!

            Physics2D.SyncTransforms();
            grid.Build();

            EnemyPathfindingNavigator nav = CreateNavigator(repathIntervalSeconds: 5.0f); // Very long repath interval
            nav.Initialize(null);

            Vector2 start = new Vector2(6.0f, 3.0f);
            Vector2 goal = new Vector2(6.0f, 9.0f);

            // Tick 1: door is open -> straight path north through x=6
            Vector2 dirOpen = nav.GetDirectionToTarget(start, goal, currentTick: 1);
            Assert.That(dirOpen.y, Is.GreaterThan(0.8f));
            int tickBeforeClose = GetPrivateField<int>(nav, "_lastRepathTick");

            // Close the door and refresh grid region (exact call NetworkDoor makes)
            doorCollider.enabled = true;
            Physics2D.SyncTransforms();
            grid.RefreshRegion(doorCollider.bounds);

            // Tick 2 (well before 5s repath interval!): navigator MUST invalidate and recalculate immediately!
            nav.GetDirectionToTarget(start, goal, currentTick: 2);
            int tickAfterClose = GetPrivateField<int>(nav, "_lastRepathTick");

            Assert.That(tickAfterClose, Is.EqualTo(2),
                "Closing a door on the active path must invalidate and recalculate on the very next tick.");
        }

        private PathfindingGrid CreateGrid(int tilesX, int tilesY)
        {
            _config = ScriptableObject.CreateInstance<PathfindingGridConfig>();
            SetField(_config, "_nodeSize", 0.5f);
            SetField(_config, "_agentRadius", 0.42f);
            SetField(_config, "_physicalColliderRadius", 0.4f);
            SetField(_config, "_pathSmoothingSafetyMargin", 0.02f);
            SetField(_config, "_obstacleLayer", (LayerMask)(1 << WorldCollisionLayer));
            SetField(_config, "_maxPathIterations", 8000);

            var root = new GameObject("TestNavGridRoot", typeof(Grid));
            _createdObjects.Add(root);

            var tilemapObj = new GameObject("Floor", typeof(Tilemap));
            tilemapObj.transform.SetParent(root.transform, false);
            _tile = ScriptableObject.CreateInstance<Tile>();
            Tilemap tilemap = tilemapObj.GetComponent<Tilemap>();
            for (int x = 0; x < tilesX; x++)
            {
                for (int y = 0; y < tilesY; y++)
                {
                    tilemap.SetTile(new Vector3Int(x, y, 0), _tile);
                }
            }

            PathfindingGrid grid = root.AddComponent<PathfindingGrid>();
            SetField(grid, "_config", _config);
            return grid;
        }

        private EnemyPathfindingNavigator CreateNavigator(float repathIntervalSeconds)
        {
            var go = new GameObject("TestEnemyNavigator");
            _createdObjects.Add(go);
            EnemyPathfindingNavigator nav = go.AddComponent<EnemyPathfindingNavigator>();
            SetField(nav, "_config", _config);
            SetField(nav, "_repathIntervalSeconds", repathIntervalSeconds);
            return nav;
        }

        private GameObject CreateWall(string name, Vector2 center, Vector2 size)
        {
            var wall = new GameObject(name, typeof(BoxCollider2D)) { layer = WorldCollisionLayer };
            wall.transform.position = center;
            wall.GetComponent<BoxCollider2D>().size = size;
            _createdObjects.Add(wall);
            return wall;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return (T)field.GetValue(target);
        }
    }
}

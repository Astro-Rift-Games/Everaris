using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Tests.EditMode.Movement
{
    [TestFixture]
    public sealed class PathfindingGridAndSolverTests
    {
        private const int WorldCollisionLayer = 6;
        private const float ProductionColliderRadius = 0.4f;
        private const float ProductionSafetyMargin = 0.02f;
        private const float ProductionClearance = ProductionColliderRadius + ProductionSafetyMargin;

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
        public void ProductionConfigAsset_MatchesContractForRadiusAndMaxIterations()
        {
            PathfindingGridConfig asset = AssetDatabase.LoadAssetAtPath<PathfindingGridConfig>(
                "Assets/Scriptable Objects/PathfindingGridConfig.asset");
            Assert.That(asset, Is.Not.Null);

            Assert.That(asset.MaxPathIterations, Is.EqualTo(8000),
                "PathfindingGridConfig.asset must match the documented 8000 iteration contract.");
            Assert.That(asset.PathSmoothingRadius, Is.EqualTo(ProductionClearance).Within(0.001f));
            Assert.That(asset.AgentRadius, Is.EqualTo(ProductionClearance).Within(0.05f),
                "AgentRadius in production asset must match the real enemy collider footprint.");
        }

        [Test]
        public void GridBuild_OneTileWideCorridor_WithProductionRadius_HasContinuousPath()
        {
            // 10x10 floor tiles covering [0,10] x [0,10], tile centers at k + 0.5
            PathfindingGrid grid = CreateGrid(10, 10, ProductionColliderRadius, ProductionSafetyMargin);

            // Solid walls leaving a 1-tile corridor along x in [4, 5] (center x = 4.5) for y in [2, 8]
            CreateWall("LeftWall", center: new Vector2(2f, 5f), size: new Vector2(4f, 6f));   // x in [0, 4]
            CreateWall("RightWall", center: new Vector2(7.5f, 5f), size: new Vector2(5f, 6f)); // x in [5, 10]

            Physics2D.SyncTransforms();
            grid.Build();

            var solver = new AStarPathSolver(grid.Width, grid.Height, _config);
            var waypoints = new List<Vector2>();
            int count = solver.FindPath(
                grid,
                startWorld: new Vector2(4.5f, 1.0f),
                endWorld: new Vector2(4.5f, 9.0f),
                enableSmoothing: false,
                pathSmoothingRadius: ProductionClearance,
                waypoints);

            Assert.That(count, Is.GreaterThan(0),
                "A 1-tile-wide corridor (1.0 u) must be traversable by an enemy with collider radius 0.40 + 0.02 margin.");
            Assert.That(Vector2.Distance(waypoints[waypoints.Count - 1], new Vector2(4.5f, 9.0f)), Is.LessThan(0.1f));
        }

        [Test]
        public void GridBuild_GapNarrowerThanColliderDiameter_RemainsBlocked()
        {
            PathfindingGrid grid = CreateGrid(10, 10, ProductionColliderRadius, ProductionSafetyMargin);

            // Leave only a 0.6 u gap (x in [4.7, 5.3]), smaller than 2 * 0.42 = 0.84 u
            CreateWall("LeftWall", center: new Vector2(2.35f, 5f), size: new Vector2(4.7f, 2f));  // x in [0, 4.7]
            CreateWall("RightWall", center: new Vector2(7.65f, 5f), size: new Vector2(4.7f, 2f)); // x in [5.3, 10]

            Physics2D.SyncTransforms();
            grid.Build();

            var solver = new AStarPathSolver(grid.Width, grid.Height, _config);
            var waypoints = new List<Vector2>();
            int count = solver.FindPath(
                grid,
                startWorld: new Vector2(5.0f, 1.5f),
                endWorld: new Vector2(5.0f, 8.5f),
                enableSmoothing: false,
                pathSmoothingRadius: ProductionClearance,
                waypoints);

            // Either no complete path exists or waypoints must not cross y = 5
            bool crossedWall = false;
            for (int i = 0; i < count; i++)
            {
                if (waypoints[i].y > 5.0f)
                {
                    crossedWall = true;
                    break;
                }
            }

            Assert.That(crossedWall, Is.False, "A 0.6 u gap is narrower than the enemy collider and must stay blocked.");
        }

        [Test]
        public void GridBuild_DiagonalTouchingWalls_BlocksCornerCutting()
        {
            PathfindingGrid grid = CreateGrid(8, 8, ProductionColliderRadius, ProductionSafetyMargin);

            // Two diagonally touching walls at (3..4, 3..4) and (4..5, 4..5)
            CreateWall("WallA", center: new Vector2(3.5f, 3.5f), size: new Vector2(1f, 1f));
            CreateWall("WallB", center: new Vector2(4.5f, 4.5f), size: new Vector2(1f, 1f));

            Physics2D.SyncTransforms();
            grid.Build();

            // Verify diagonal corner check blocks moving between two non-walkable orthogonal neighbours
            Assert.That(grid.TryGetNodeFromWorldPoint(new Vector2(3.5f, 4.5f), out int nx, out int ny), Is.True);
            Assert.That(grid.IsValidDiagonalMove(nx, ny, 1, -1), Is.False,
                "Diagonal step between two touching wall corners must be forbidden.");
        }

        [Test]
        public void SmoothPath_FinalSegmentToEndWorld_NeverCutsThroughWall()
        {
            PathfindingGrid grid = CreateGrid(10, 10, ProductionColliderRadius, ProductionSafetyMargin);

            // Wall blocking direct line from (2, 2) to (6, 4.1) while node (5.5, 4.5) is walkable around the corner
            CreateWall("CornerWall", center: new Vector2(4.0f, 3.0f), size: new Vector2(2.0f, 2.0f)); // x:[3,5], y:[2,4]

            Physics2D.SyncTransforms();
            grid.Build();

            var solver = new AStarPathSolver(grid.Width, grid.Height, _config);
            var waypoints = new List<Vector2>();
            // Start below-left of wall, end tucked right behind wall's top-right edge
            Vector2 start = new Vector2(2.0f, 2.5f);
            Vector2 end = new Vector2(5.1f, 3.5f);
            int count = solver.FindPath(
                grid,
                start,
                end,
                enableSmoothing: true,
                pathSmoothingRadius: ProductionClearance,
                waypoints);

            Assert.That(count, Is.GreaterThan(0));
            for (int i = 0; i < count - 1; i++)
            {
                Vector2 seg = waypoints[i + 1] - waypoints[i];
                float dist = seg.magnitude;
                if (dist < 0.001f) continue;

                RaycastHit2D hit = Physics2D.CircleCast(
                    waypoints[i],
                    ProductionColliderRadius * 0.95f,
                    seg / dist,
                    dist,
                    1 << WorldCollisionLayer);

                Assert.That(hit.collider, Is.Null,
                    $"Smoothed segment {i} ({waypoints[i]} -> {waypoints[i + 1]}) intersects wall '{hit.collider?.name}'.");
            }
        }

        [Test]
        public void MultipleNavigators_ShareSingleSolverInstance_MemoryDoesNotScaleWithEnemyCount()
        {
            PathfindingGrid grid = CreateGrid(10, 10, ProductionColliderRadius, ProductionSafetyMargin);
            Physics2D.SyncTransforms();
            grid.Build();

            EnemyPathfindingNavigator navA = CreateNavigator();
            EnemyPathfindingNavigator navB = CreateNavigator();

            navA.Initialize(null);
            navB.Initialize(null);

            navA.GetDirectionToTarget(new Vector2(1.5f, 1.5f), new Vector2(8.5f, 8.5f), currentTick: 1);
            navB.GetDirectionToTarget(new Vector2(1.5f, 8.5f), new Vector2(8.5f, 1.5f), currentTick: 1);

            object solverA = GetPrivateField<object>(navA, "_solver");
            object solverB = GetPrivateField<object>(navB, "_solver");

            Assert.That(solverA, Is.Not.Null);
            Assert.That(ReferenceEquals(solverA, solverB), Is.True,
                "All navigators on the same PathfindingGrid must share one solver instead of allocating per enemy.");
        }

        [Test]
        public void BinaryMinHeap_PushOrDecreaseKey_UpdatesPriorityOfAlreadyOpenNode()
        {
            var heap = new BinaryMinHeap(16);
            heap.PushOrDecreaseKey(cost: 50, nodeIndex: 1, x: 1, y: 0);
            heap.PushOrDecreaseKey(cost: 30, nodeIndex: 2, x: 2, y: 0);
            heap.PushOrDecreaseKey(cost: 10, nodeIndex: 1, x: 1, y: 0);

            Assert.That(heap.Count, Is.EqualTo(2), "DecreaseKey must not insert duplicate entries.");
            (int cost, int x, int y) first = heap.Pop();
            Assert.That(first.cost, Is.EqualTo(10));
            Assert.That(first.x, Is.EqualTo(1));
        }

        [Test]
        public void AStarSolver_DetourGrid_ReturnsOptimalMinimumCostPath()
        {
            PathfindingGrid grid = CreateGrid(12, 12, ProductionColliderRadius, ProductionSafetyMargin);

            // Asymmetric U-obstacle: top bypass is shorter than bottom bypass
            CreateWall("MidWall", center: new Vector2(6.0f, 5.0f), size: new Vector2(1.0f, 6.0f)); // x:[5.5,6.5], y:[2,8]
            Physics2D.SyncTransforms();
            grid.Build();

            var waypoints = new List<Vector2>();
            AStarPathStatus status = grid.SharedSolver.FindPathWithStatus(
                grid,
                startWorld: new Vector2(3.5f, 7.0f),
                endWorld: new Vector2(8.5f, 7.0f),
                enableSmoothing: false,
                pathSmoothingRadius: ProductionClearance,
                waypoints);

            Assert.That(status, Is.EqualTo(AStarPathStatus.Complete));

            // Optimal path goes over the top (y >= 8.5), never around the much longer bottom (y <= 1.5).
            float minY = float.MaxValue;
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (waypoints[i].y < minY) minY = waypoints[i].y;
            }
            Assert.That(minY, Is.GreaterThan(5.0f), "A* must take the shorter top detour.");
        }

        [Test]
        public void AStarSolver_ExhaustedSmallUnreachableRoom_DoesNotFlagIterationLimit()
        {
            PathfindingGrid grid = CreateGrid(8, 8, ProductionColliderRadius, ProductionSafetyMargin);
            // Seal off a tiny pocket so open set empties well before MaxPathIterations
            CreateWall("Seal", center: new Vector2(4.0f, 4.0f), size: new Vector2(8.0f, 1.0f));
            Physics2D.SyncTransforms();
            grid.Build();

            var waypoints = new List<Vector2>();
            AStarPathStatus status = grid.SharedSolver.FindPathWithStatus(
                grid,
                startWorld: new Vector2(2.0f, 1.5f),
                endWorld: new Vector2(2.0f, 6.5f),
                enableSmoothing: false,
                pathSmoothingRadius: ProductionClearance,
                waypoints,
                allowPartialPath: false,
                logIterationWarning: false);

            Assert.That(status, Is.EqualTo(AStarPathStatus.NoPath));
            Assert.That(grid.SharedSolver.LastSearchHitIterationLimit, Is.False,
                "Naturally exhausted search must never report iteration-limit exhaustion.");
        }

        private PathfindingGrid CreateGrid(int tilesX, int tilesY, float colliderRadius, float safetyMargin)
        {
            _config = ScriptableObject.CreateInstance<PathfindingGridConfig>();
            SetField(_config, "_nodeSize", 0.5f);
            SetField(_config, "_agentRadius", colliderRadius + safetyMargin);
            SetField(_config, "_physicalColliderRadius", colliderRadius);
            SetField(_config, "_pathSmoothingSafetyMargin", safetyMargin);
            SetField(_config, "_obstacleLayer", (LayerMask)(1 << WorldCollisionLayer));
            SetField(_config, "_maxPathIterations", 8000);

            var root = new GameObject("TestPathfindingGridRoot", typeof(Grid));
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

        private EnemyPathfindingNavigator CreateNavigator()
        {
            var go = new GameObject("TestEnemyNavigator");
            _createdObjects.Add(go);
            EnemyPathfindingNavigator nav = go.AddComponent<EnemyPathfindingNavigator>();
            SetField(nav, "_config", _config);
            return nav;
        }

        private void CreateWall(string name, Vector2 center, Vector2 size)
        {
            var wall = new GameObject(name, typeof(BoxCollider2D)) { layer = WorldCollisionLayer };
            wall.transform.position = center;
            wall.GetComponent<BoxCollider2D>().size = size;
            _createdObjects.Add(wall);
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

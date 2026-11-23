# Pathfinding Architecture

## Context
Enemies in Project Grimhold previously relied on direct-line pursuit and a basic obstacle avoidance steering layer. This led to enemies becoming stuck behind large walls or complex room geometry in the dungeon. A pathfinding system was required to navigate around these obstacles during `Chase`, `Patrol`, and `ReturnHome` states, as well as during Catacombs Specter pursuit.

## Decision
Implement a custom A* pathfinding system tailored for the existing 2D Grid/Tilemap architecture and Photon Fusion 2.1 network simulation.

We rejected importing a third-party solution (like NavMeshPlus or generic A* assets) because:
1. They often assume they own the movement and transform updates (violating our separation of concerns).
2. They are difficult to make deterministic and idempotent for Fusion's resimulation.
3. They introduce heavy MonoBehaviour dependencies that conflict with our existing pure C# simulation data flow.

## Responsibilities

* **`PathfindingGrid` (MonoBehaviour)**: Scans active Tilemaps once during raid bootstrap and constructs a flat, 1D array of `PathNode` structs representing walkable space.
  * **Alignment & Clearance (US-87 / TASK-312):** Node origins are snapped to multiples of `NodeSize` (`0.5 u`) so 1-tile corridors (`1.0 u`) always contain centered nodes (`k + 0.5`). Walkability is evaluated with `Physics2D.OverlapCircle(worldPos, AgentRadius, ObstacleLayer)` matching the real enemy `CircleCollider2D` (`0.40 u`) plus safety margin (`0.02 u` -> `0.42 u`), avoiding over-erosion while blocking gaps narrower than the enemy body.
  * **Shared Solver Ownership (US-87 / TASK-320):** `PathfindingGrid` instantiates and owns a **single shared `AStarPathSolver`** on `Build()`. Pathfinding memory is therefore $O(1)$ with respect to active enemy count, with zero allocation spikes on the first simulation tick.
  * **Dynamic Topology Versioning (US-87 / TASK-313):** Runtime obstacles (such as `NetworkDoor`) call `RefreshRegion(Bounds)`. Whenever at least one node flips walkability, `TopologyVersion` increments and the modified bounds are recorded in a fixed-size ring buffer so affected navigators invalidate their cached paths on the very next tick.
  * **Crowd Coordinator Ownership (US-87 / TASK-318):** Owns the scene-scoped `EnemyCrowdCoordinator` (cleared on destroy; no global static state) for soft enemy-enemy separation (attenuated laterally in 1-tile corridors) and deterministic surround-slot distribution around shared targets.
* **`PathfindingGridConfig` (ScriptableObject)**: Stores static configuration shared across the grid and solver (`NodeSize = 0.5`, `AgentRadius = 0.42`, `PhysicalColliderRadius = 0.40`, `PathSmoothingSafetyMargin = 0.02`, `ObstacleLayer = WorldCollision`, `MaxPathIterations = 8000`).
* **`AStarPathSolver` (Pure C#)**: Stateless, allocation-free A* solver over `PathfindingGrid`.
  * Uses an indexed `BinaryMinHeap` with $O(\log n)$ `PushOrDecreaseKey` to guarantee optimal minimum-cost paths over the grid.
  * Supports `AStarPathStatus` (`Complete`, `Partial`, `NoPath`), returning a partial fallback path to the closest reachable node when a target is sealed off.
  * Applies bounded $O(n)$ post-process smoothing via `Physics2D.CircleCast` using `PathSmoothingRadius`, validating every segment including the final segment to `endWorld` so smoothed paths never clip corners or walls.
* **`EnemyPathfindingNavigator` (MonoBehaviour)**: Per-enemy path lifecycle manager used by Slimes, Ranged enemies, and Specters.
  * Consumes `PathfindingGrid.SharedSolver`, caches waypoints in a reusable buffer, and advances waypoints (including final arrival `HasReachedDestination`) at most once per simulation tick.
  * Respects configured repath cadence even on unreachable/partial paths, rate-limits iteration-limit warnings, staggers initial A* calls deterministically by `NetworkObject.Id.Raw` on State Authority, detects stuck/no-progress conditions (`IsStuck`) via simulation ticks, and invalidates immediately when `PathfindingGrid.TopologyVersion` intersects remaining path segments.
* **`EnemyMovementAIController` / `SpecterAIController` (NetworkBehaviour)**: Consumers that query the navigator for normalized directions, apply locomotion smoothing/separation/Ranged positioning rules, and pass displacement to `Kinematic2DMovementMotor`.

## Network Authority & Sources of Truth

Pathfinding is **strictly a State Authority (Host) concern**.

* **Grid Construction**: The grid is built only on the Host by `NetworkSpawnManager` during `TryExecuteInitialRaidBootstrap` and rebuilt locally during `SealHostMigrationRoster`. Proxies (Clients) never build the grid or run A*.
* **State Ownership**: Paths and waypoints are **not** `[Networked]` state. They are local, transient data computed on the Host. The network boundary is the *resulting locomotion state* (`FacingDirection`, `IsMoving`, `CurrentVelocity`, `HomeAreaPosition`, `IsReturningHome`), which AI controllers synchronize to clients.
* **Resimulation Tolerance**: `EnemyPathfindingNavigator` uses Fusion's `Runner.Tick` instead of `Time.time` to throttle path recalculations, track stuck progress, and prevent double-advancement of waypoints during `FixedUpdateNetwork` resimulation.
* **Host Migration**: Because paths are transient local state, a migrating Host simply drops the old path. Its `Spawned()` callback re-initializes the navigator against the newly rebuilt grid while preserving `[Networked] HomeAreaPosition`.

## Data Flow

```text
EnemyMovementAIController.FixedUpdateNetwork (Host Only)
  -> EvaluateSensors (WorldCollision | Obstacles LOS, Leash > Aggro, Attack Range Hysteresis, Ranged Positioning)
  -> ComputeMoveDirection (Pursuit/Surround Slot | Ranged Retreat/Vantage | ReturnHome | Patrol)
    -> EnemyPathfindingNavigator.GetDirectionToTarget(currentPos, goalPos, Runner.Tick)
      -> Check TopologyVersion invalidation / initial stagger / cadence / target movement
      -> RequestPath via PathfindingGrid.SharedSolver (indexed A*, partial fallback, wall-safe O(n) smoothing)
      -> Advance waypoints (including final arrival) and evaluate tick-based progress (IsStuck)
    <- Returns normalized Vector2 direction
  -> Blend soft crowd separation via PathfindingGrid.CrowdCoordinator (single-file safe in 1-tile corridors)
  -> Apply acceleration/deceleration & turn-rate smoothing (EnemyLocomotionRules) + independent Knockback
  -> Apply final displacement to Kinematic2DMovementMotor
-> Synchronize Transform, FacingDirection, IsMoving to Clients
```

## Risks and Limitations
* `AStarPathSolver` operates synchronously on the Host tick. Bounded by `PathfindingGridConfig.MaxPathIterations` (`8000`), deterministic initial staggering, repath cadence enforcement on unreachable targets, and partial-path fallback.
* All enemies share a single grid configured for the standard raid enemy physical radius (`0.40 u` + `0.02 u` safety margin = `0.42 u`). If significantly larger boss colliders are introduced in the future, a second clearance layer or grid would be required.

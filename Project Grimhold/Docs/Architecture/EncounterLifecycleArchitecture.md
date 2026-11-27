# Encounter Lifecycle Architecture

**Status:** Implemented (TASK-330, HacknPlan item 885). Automated tests pass; the real multi-process Host Migration run is **pending** and is specified in [section 7](#7-validation).

Authored Dungeon rooms can host an Encounter with a stable `EncounterId`, a state that every peer can query (`NotActivated -> Active -> Resolved -> Cooldown -> Reactivable`) and a configurable reactivation. State Authority is the only writer; the state survives Host Migration. A raid scene without authored rooms behaves exactly as before.

Execution record and decisions (L1-L17): [`odd/tasks/task-330-encounter-lifecycle.md`](../../../odd/tasks/task-330-encounter-lifecycle.md). Code: [`Assets/Scripts/Gameplay/Encounters/`](../../Assets/Scripts/Gameplay/Encounters/).

## 1. Source of truth and open Game Design

Live Game Design **Doc 10 - Reglas de Sesión de Dungeon** (§9.1, §9.2, §10.2, §10.3) owns the intent. This document owns the technical contract only.

| Doc 10 | What it requires | Where it lands |
| --- | --- | --- |
| §9.2 identity | "Cada Encounter posee un EncounterID y sus enemigos continúan perteneciendo a dicho encuentro aunque abandonen físicamente la sala durante una persecución." | `EncounterId`; enemy-side `EncounterIdValue` + `EncounterMemberKey` |
| §9.2 cycle | "El ciclo básico es: No activada → Activa → Resuelta → Cooldown → Reactivable." | `EncounterState`, `EncounterLifecycle` |
| §9.2 resolution | "Un Encounter se considera resuelto cuando todos sus enemigos fueron eliminados válidamente. El cooldown comienza en ese momento, no cuando el jugador abandona la sala." | `EncounterLifecycle.RegisterValidElimination` |
| §9.2 reactivation | "Una sala sólo puede reactivarse si el Encounter anterior está resuelto, el cooldown finalizó, no hay jugadores dentro de la zona de generación, existe capacidad global disponible, la fase de Dungeon permite generar y no se alcanzó MaxReactivations." | `EncounterLifecycle.EvaluateReactivation` (order of `EncounterReactivationBlock`) |
| §9.2 parameters | "Los valores concretos de cooldown y número máximo de reactivaciones quedan para Balance; el sistema debe soportarlos como parámetros configurables." | `EncounterRoom` serialized fields |
| §9.1 composition | Initial weighted validation, then Threat Budget; the room "puede definir MinEnemies, MaxEnemies, AllowedEnemies y un Encounter Profile que modifique pesos de composición". | `EncounterCompositionSpec`, `EncounterProfile`, `EncounterCompositionPlanner` |
| §10.2 | "Desactivar o simplificar la IA por distancia no elimina ni resetea al enemigo. Debe conservar vida, posición y estados relevantes del Encounter." | Membership is independent of position/AI; state survives Host Migration ([section 4](#4-host-migration)) |
| §10.3 | Local Threat Budget. | `EncounterCompositionSpec.ThreatBudget` |

(Quoted verbatim from the live document, read 2026-11-27; the live document is authoritative.)

### Left open by Game Design: neutral non-GD defaults

Doc 10 fixes no concrete values. The implementation ships neutral defaults. **All are pending Game Design / Balance** and must not be read as approved values.

| Parameter | Default | Where |
| --- | --- | --- |
| Cooldown | 120 s placeholder | `EncounterRoom._cooldownSeconds` |
| `MaxReactivations` | 0 (a room never reactivates: today's behavior) | `EncounterRoom._maxReactivations` |
| Dungeon phases that allow generating | all four | `EncounterRegistry._generationPhases` (`EncounterGenerationPhases.All`, serialized `15` on `NetworkMatchController.prefab`) |
| Encounter Profile | none (base Spawn Weights); no profile asset ships | `EncounterCompositionSpec._profile` |
| Initial validation / counts / budget | chance 1, Min 1, Max 1, Threat Budget 0 (only `MaxEnemies` limits) | `EncounterCompositionSpec` |
| Population origin of reactivated enemies | `EnemyPopulationOrigin.Bootstrap` (not confirmed by GD) | `NetworkSpawnManager.TryReactivateEncounterRoom` |
| Reactivation cadence | 1 s, non-GD | `EncounterRegistry._reactivationCheckSeconds` |

## 2. Ownership and state boundaries

| Layer | Owner | Content |
| --- | --- | --- |
| Static configuration | Scene + assets | `EncounterRoom` (id, zone, markers, Balance values), `EncounterCompositionSpec`, `EncounterProfile` ScriptableObject, `NetworkSpawnSceneConfiguration._encounterRooms`, `EncounterRegistry` serialized fields (`_generationPhases`, `_pressureConfig`, `_reactivationCheckSeconds`). Never mutated during gameplay. |
| Pure domain | Plain C# (EditMode-testable) | `EncounterLifecycle` (membership, valid elimination, cooldown, reactivation conditions), `EncounterRegistryModel` (one lifecycle per room, tick-to-seconds), `EncounterBootstrapPlanner` / `EncounterCompositionPlanner` / `EncounterSpawnAssigner` / `EncounterSeedRules`, `EncounterReactivationRules`, `EncounterGenerationPhaseGate`, `EncounterZoneOccupancy`, `EncounterReconciler`, `EncounterSlotCodec`, `EncounterMemberKeyCodec`. |
| Replicated room state | `EncounterRegistry` (`NetworkBehaviour` on `NetworkMatchController.prefab`) | `NetworkArray<EncounterNetworkSlot>` (one slot per room, index = room position in the scene configuration) and its own `[Networked] ElapsedTicks` clock. State Authority writes; every peer reads through `TryGetStatus` / `TryGetState`. |
| Per-enemy replicated identity | `EnemyCharacter` | `[Networked] EncounterIdValue` (0 = no Encounter) and `[Networked] EncounterMemberKey`. Set before spawn; **never derived from `NetworkId`**, which changes on Host Migration. |
| Runner-scoped orchestration | `NetworkSpawnManager` | Binds the registry to the scene's rooms, spawns room compositions, measures zone presence and capacity, reconciles after restore, resets at closure/scene change. |

Slot layout (`EncounterNetworkSlot`): `EncounterId`, `StateRaw`, `ReactivationCount`, `MemberMask`, `AliveMask`, `CooldownStartTick`. The member mask holds the members of the **current cycle**; alive is a subset.

**Own clock.** `ElapsedTicks` is replicated state, so `CopyStateFrom` restores it; it does not depend on the new runner's tick. It advances only while some room is Resolved/Cooldown (so it is idle, and does not replicate, the rest of the raid) and is frozen while the Host Migration recovery window is open.

**Member key.** `key = (cycle << 8) | memberIndex`. `cycle` is the room's `ReactivationCount` when the enemy spawned; cycle 0 is the plain index. Bodies of earlier cycles keep the room's `EncounterId`, so the cycle in the key prevents a stale corpse from being mistaken for a member of the current cycle (deaths and reconcile ignore other cycles). No extra networked word is needed.

**Local-only state (lost on Host Migration, by design).** `EncounterRegistryModel` (rebuilt from the scene's rooms and re-filled from the replicated slots), `EncounterReactivationAttempts` (parking and spawn-failure backoff), the held-deaths queue, the zone-presence buffer, the cadence counter and the "unbound death already reported" flag. Each is either derivable or only an optimization/log filter: a new Host re-measures presence, and a room that had been parked is attempted once more (the seed is deterministic, so a structural outcome repeats and is parked again; a transient failure retries with backoff).

## 3. Flows

### 3.1 Bootstrap (hybrid with the global Enemies group)

1. The scene's rooms are listed in `NetworkSpawnSceneConfiguration._encounterRooms` (validated by `Validate`, [section 6](#6-authoring-guide)). The spawn manager binds them to the registry as soon as both exist, in either order (`TryBindEncounterRooms`); every room starts `NotActivated`.
2. Raid bootstrap runs the global groups, then `TrySpawnEncounterRooms`. Markers owned by a room are removed from the global Enemies group (`EncounterMarkerOwnership.ExcludeOwned`); the group amount is capped by the remaining markers (`EffectiveGroupAmount`) so authored rooms never fail the bootstrap. **Rooms are authored by design/level**; design can retire the global group later by data (L9). No rooms means no change in behavior.
3. Per room: seed = session seed + scene load generation + `EncounterId` (+ cycle) -> initial weighted validation -> `AllowedEnemies` and profile weights -> local Threat Budget -> `MinEnemies/MaxEnemies` -> assignment to distinct room markers with member indices `1..n`.
   - `NoEncounter`: nothing spawns; the room stays `NotActivated`. Only a resolved room reactivates, so it never activates in this raid.
   - `Invalid` (no composition satisfies the limits): warning, room skipped, raid continues.
   - Planned: enemies spawn with `EncounterIdValue` and key; `TryActivateEncounterRoom` moves the room to `Active`. A spawn or activation failure fails the bootstrap; a missing/unconfigured registry also fails it.

### 3.2 Resolution

A **valid elimination** is any enemy death on State Authority: `EnemyCharacter.HandleDeath` calls `NotifyEncounterOfDeath` **before** the death-dependency guard, so a dependency problem cannot keep an Encounter from resolving. Despawn (raid cleanup, rollback of a failed spawn) never reaches `HandleDeath` and never counts (L8). When the last alive member dies the room becomes `Resolved` and the cooldown starts at that tick, not when players leave. Registration is idempotent.

### 3.3 Cooldown

On each State Authority tick while any room is cooling, `ElapsedTicks++` and `Advance` moves `Resolved -> Cooldown -> Reactivable` once `now - CooldownStart >= CooldownSeconds` (a cooldown of 0 reaches `Reactivable` in the same advance that leaves `Resolved`).

### 3.4 Reactivation

Evaluated on State Authority every `_reactivationCheckSeconds`, never per tick, and skipped while Host Migration recovery is open. Per `Reactivable` room, cheap conditions first:

| Step | Condition / action | Result when it fails |
| --- | --- | --- |
| 1 | `MaxReactivations` not reached (lifecycle check, no measuring) | Nothing; no cost |
| 2 | Not in spawn-failure backoff / not parked | Skipped |
| 3 | Dungeon phase allows generating (`_generationPhases` + controller running; gate closed when pressure is not running) | `Blocked`, retried at cadence |
| 4 | No present player inside the generation zone (living or downed, `Raiding`; dead and non-`Raiding` participants do not count; a null or disabled zone is never occupied) | `Blocked` |
| 5 | Global capacity (`PopulationTracker.GetAvailableGlobalCapacity(MaxGlobalEnemies)`) covers `MinEnemies` before planning and the planned count after | `Blocked` |
| 6 | Plan with cycle `ReactivationCount + 1` (own seed stream) | `NoEncounter` / `InvalidComposition`: room parked, **no reactivation consumed** |
| 7 | Spawn assignments with cycle-aware keys | 0 spawned: `SpawnFailed`, backoff (doubling, capped at 32 passes), nothing consumed |
| 8 | Fewer spawned than the room's `MinEnemies` | Spawned enemies despawned, `SpawnFailed`, backoff, nothing consumed |
| 9 | `TryReactivate` commits: `ReactivationCount++`, new member set | Refused: enemies despawned, `ActivationRejected`, parked |

A partial spawn at or above `MinEnemies` activates the room with the enemies that spawned. Reactivated enemies use origin `Bootstrap` (non-GD, [section 1](#1-source-of-truth-and-open-game-design)). `MaxReactivations` must not exceed `EncounterMemberKeyCodec.MaxCycle`.

The reactivation dependencies (`_pressureConfig` with positive `MaxGlobalEnemies`, and a `DungeonPressureController` on the registry's GameObject) are validated once per bind: when missing, one error is logged and reactivation is disabled instead of blocking silently.

### 3.5 Raid closure and scene reset

- Raid closure (`NetworkSpawnManager` Results cleanup): `ResetForRaidClosure` clears model, slots, held deaths and the clock; nothing carries to the next raid. A death arriving afterwards is dropped with a single warning and never queued into a later raid.
- Scene change (`OnSceneLoadStart`): `ResetForSceneChange` clears the state, **except** while a Host Migration restore is still waiting to be adopted (ignored, so the copied state survives).

## 4. Host Migration

`EncounterRegistry` lives on `NetworkMatchController.prefab`, so its slots and clock are restored by `CopyStateFrom`; `Spawned()` does not overwrite them. The contract follows [`HostMigrationRecoveryArchitecture.md`](HostMigrationRecoveryArchitecture.md).

| Concern | Behavior |
| --- | --- |
| Restore adoption | `Spawned()` marks `_restorePending` when State Authority and `HostMigrationRestoreUtility.IsRestoreSpawn`. The first successful `Configure` adopts the replicated slots (`AdoptReplicatedState`) instead of publishing a fresh state. |
| Inconsistent slot | A slot that is not the consistent state of the room at that index is logged as an error and that room restarts `NotActivated`; a slot with no room at its index is discarded with an error. Never silent. |
| Deferred deaths | Deaths reported before the restore is adopted are held (bounded by `MaxRooms x MaxMembersPerRoom`) and applied by that first `Configure`. |
| Unadoptable restore | The restored scene has no rooms / does not require spawn points / contains a null or invalid room / `Configure` fails: `DiscardUnadoptedRestore` clears the copied slots, drops held deaths, ends the pending state and logs an error with the counts. |
| Reconcile | After `ReconcilePopulationTracker`, `HostMigrationSnapshotRestorer` calls `ReconcileEncounterRegistry`: restored enemies are compared with the slots. **The registry is authoritative.** Only a member the registry lists alive whose enemy was restored dead is eliminated (an enemy is the source of truth for its own death); every other mismatch is logged, never changed. Observations of earlier cycles are ignored and counted. |
| Cooldown across migration | While recovery is `Open`/`Sealing`, `FixedUpdateNetwork` returns early: `ElapsedTicks` is frozen, so a cooldown in progress is **lengthened by the recovery window** (accepted; L13). |
| Reactivation | Not evaluated while recovery is open. |
| Identity | Enemies keep `EncounterIdValue` and `EncounterMemberKey` although their `NetworkId`s change. |

## 5. Limits, invariants and known limitations

| Invariant | Value | Enforced by |
| --- | --- | --- |
| Rooms per raid scene | 32 (`EncounterLimits.MaxRooms`, array capacity) | `EncounterRoomSetValidation`, `EncounterRegistryModel.TryCreate` |
| Members per cycle | 32 (`MaxMembersPerRoom`, width of the `uint` masks); member index `1..32` -> bit `index-1` | `EncounterRoom.Validate` (`MaxEnemies`), `EncounterSlotCodec` |
| Key layout | low 8 bits index, high 24 bits cycle; cycle 0 = plain index | `EncounterMemberKeyCodec` |
| `MaxReactivations` cap | `EncounterMemberKeyCodec.MaxCycle` (2^24 - 1) | `EncounterRoom.Validate` |
| `EncounterId` | positive, unique per scene; 0 = none | `EncounterId`, set validation |
| A marker belongs to one room | | `EncounterRoomSetValidation` |
| Single writer | State Authority; clients read only | `EncounterRegistry` guards |

### Known limitations and follow-ups

- **Spatial validation = spawn-point capacity only** (planned count <= room markers). No navigation/reachability check (L6).
- **Room markers may coincide with reinforcement points** (L11); nothing prevents authoring both on the same marker.
- **A restored member that is missing keeps the room `Active`.** Reconcile reports it ("missing") but does not change state (L13).
- **`WeightedEnemySelector` falls back to `UnityEngine.Random`** when no `System.Random` is passed (`WeightedEnemySelector.cs:48`). The Encounter planner always passes one, so Encounter composition does not use it; the fallback is untouched (L6).
- **`NetworkEnemy` word-budget test** (`RaidLootOriginWordCountTests`, `NetworkEnemy.prefab`): pinned 1474, observed 1490. TASK-330 adds 2 words (`EncounterIdValue`, `EncounterMemberKey`); the base derives to 1488, so the pin was already stale by 14. Not run at base. Updating the budget needs an owner decision (L12, L13).
- **Pressure not running closes the phase gate**; per L16 this occurs only at closure or with an invalid config.
- **Concrete values, profiles and phases are pending GD/Balance** ([section 1](#1-source-of-truth-and-open-game-design)); no profile asset and no rooms in `CatacombsMap` ship yet (L6, L8).
- **No shipped UI or log prints encounter status on clients**; the data is readable through `TryGetStatus` on every peer.
- **Real multi-process Host Migration and remote replication are unverified** ([section 7](#7-validation)).
- Parking/backoff is local to the Host and restarts after a migration (accepted, [section 2](#2-ownership-and-state-boundaries)).

Test seams (internal, same pattern as the existing `Test_RegisterParticipant`): `EncounterRegistry.Test_BeginRestoreAdoption`, `NetworkSpawnManager.Test_RegisterAvatar`, `Test_AdmitProfile`, `Test_ReactivationSpawnLimit`, `Test_BeforeReactivationCommit`, `Test_SetHostMigrationRecoveryState`. `Test_BeginRestoreAdoption` exists because `IsResume` is impossible on a single peer.

## 6. Authoring guide

Design/level owns rooms. A raid scene gets none until they author them.

1. **Create the room.** Add an `EncounterRoom` component to a GameObject of the raid scene (one per room; `DisallowMultipleComponent`).
2. **Id.** Set `_encounterId` to a positive integer, unique in the scene.
3. **Generation zone.** Assign a `Collider2D` covering the room. A living or downed `Raiding` player inside it blocks reactivation. The code only calls `OverlapPoint`; a disabled collider never blocks.
4. **Spawn points.** Reference the **existing** `EnemySpawnMarkers` transforms (do not reparent). Each marker may belong to one room and must be in the same scene as the `NetworkSpawnSceneConfiguration`. Referenced markers stop being used by the global Enemies group.
5. **Composition** (`_composition`): `EncounterChance` (0..1, initial weighted validation), `MinEnemies` (>= 1, <= markers), `MaxEnemies` (>= Min, <= 32), `ThreatBudget` (0 = only `MaxEnemies` limits), `AllowedEnemyIds` (empty = every eligible archetype), optional `Profile`.
6. **Profile.** Optional: create via *Grimhold > Gameplay > Encounter Profile*; each modifier is `EnemyId` (as in `EnemySpawnMetadata`) plus a weight multiplier. Archetypes without a modifier keep their weight; multiplier 0 removes the archetype; no profile keeps base weights. Concrete profiles are a Game Design decision.
7. **Balance fields:** `_cooldownSeconds` and `_maxReactivations` (defaults are placeholders, [section 1](#1-source-of-truth-and-open-game-design)). Leave `_maxReactivations` at 0 for no reactivation.
8. **Register the room:** add it to `NetworkSpawnSceneConfiguration._encounterRooms`. Its position in that array is its slot index. The scene's `SceneSpawnPointPolicy` must not be `NotRequired`.

Validation errors the designer can see (room errors are prefixed `Encounter room '<name>' `):

| Error | Cause |
| --- | --- |
| `has an invalid EncounterId (N); it must be positive.` | id <= 0 |
| `has no generation zone collider assigned.` | missing zone |
| `has no composition spec.` | null spec |
| `has inconsistent enemy counts: MinEnemies (a) must be at least 1 and not greater than MaxEnemies (b).` | counts |
| `has a MaxEnemies (N) above the replicated member cap (32).` | > 32 |
| `has an EncounterChance (x) outside 0..1.` | chance |
| `has an invalid cooldown (x); it must be zero or positive.` / `has an invalid MaxReactivations (N); it must be zero or positive.` / `has a MaxReactivations (N) above what a member key can carry (16777215).` | Balance values |
| `has no spawn points.` / `has a null spawn point at index i.` / `has a duplicate spawn point '<m>' at index i.` | markers |
| `requires MinEnemies (a) but has only n spawn points.` | capacity |
| `N encounter rooms exceed the replicated capacity of 32.` | > 32 rooms |
| `Encounter room at index i is null.` | empty slot in the array |
| `Duplicate EncounterId N on encounter rooms '<a>' and '<b>'.` | repeated id |
| `Spawn marker '<m>' is owned by two encounter rooms: '<a>' and '<b>'.` | shared marker |
| `Encounter rooms are not allowed when SceneSpawnPointPolicy is NotRequired.` | policy |
| `Encounter room '<r>' spawn point '<m>' does not belong to scene '<s>'. Spawning aborted.` | marker in another scene (runtime) |

## 7. Validation

### Automated (as recorded in the Log)

| Suite | Covers | Last recorded result |
| --- | --- | --- |
| EditMode `Encounter` filter (`Assets/Tests/EditMode/Editor/Encounters/`) | Lifecycle, snapshot/slot/key codecs, registry model, reconciler, planners, seeds, assignment, marker ownership, zone occupancy, phase gate, reactivation rules/attempts/dependencies, room and scene-config validation | 269/269 (L17) |
| PlayMode `Encounter` (`Assets/Tests/PlayMode/Encounters/`) | Real single-peer runner: resolution from deaths (and not from despawn), cooldown on the authority tick, restore adoption through `Test_BeginRestoreAdoption`, held deaths, discarded restores, closure/scene reset, reactivation conditions, cycles, partial spawn, backoff, HM-recovery skip | 37/37 (L17) |
| Regression: HostMigration 78, Reinforcement 28, PvePopulation 10, DungeonPressure 76, NetworkSpawnSceneConfiguration 14, EnemySpawn 7; PlayMode KillExperienceSource 16, SpecterLeash 1, CollapseHazardAuthority 5 | Shared code touched by T3-T5 | Passing (L12, L15) |

Not green and not caused by this task (L10-L12): `CatacombsSceneComposition` 8/10 (two scene-content failures), `RaidLootOriginWordCountTests` NetworkEnemy ([section 5](#5-limits-invariants-and-known-limitations)). PlayMode runs modify `DungeonGrayboxMinimapLayout.asset` as a side effect (L14); do not commit it with this work.

Not covered automatically: the real `IsRestoreSpawn`/`CopyStateFrom` path, remote replication, and the Fusion spawn path. A single runner is not evidence of Host Migration behavior (see `HostMigrationRecoveryArchitecture.md`, "Validation boundary").

### Manual multi-process Host Migration checklist (human)

**Prerequisites**
- Build with Host + at least 2 Clients (three processes).
- A raid scene with at least one authored room (local, uncommitted if design has not authored one): >= 3 markers, `MinEnemies` = `MaxEnemies` = 3, `_maxReactivations` = 1, short cooldown (for example 30 s), a generation zone, `EncounterChance` 1. Dungeon pressure running (default phases allow generating).
- Client-side status has no shipped display: read it with a temporary local diagnostic of your choice that calls `EncounterRegistry.TryGetStatus(new EncounterId(id), out status)` and prints `State`, `ReactivationCount`, `MemberCount`, `AliveMemberCount` (do not commit it).
- Fusion's automatic snapshot is every 10 s: wait more than 10 s after the last change before killing the Host, or record the expected snapshot lag.

| # | Step | Expected (state and logs) |
| --- | --- | --- |
| M1 | Start the raid. | `[NetworkSpawnManager] Initial PvPvE bootstrap completed successfully.`; no error mentioning `EncounterRegistry` or "Encounter bootstrap requires". Status of the room on Host and both Clients: `Active`, `ReactivationCount` 0, `MemberCount` 3, `AliveMemberCount` 3. |
| M2 | Kill 1 member of the room (not all). | Status on all peers: `Active`, alive 2. No "resolved" log. |
| M3 | Wait > 10 s, then abruptly kill the Host process. A Client is promoted. | Recovery completes (`[HM-MULTI] Snapshot restoration finished successfully.`). |
| M4 | On the new Host check state and logs. | `[EncounterRegistry] Host Migration encounter reconciliation successful. Encounters: 1, living members: 2.` Status unchanged (`Active`, alive 2) on the new Host and the other Client. **No** `Restored encounter state could not be adopted`, `Restored encounter state discarded`, or `reconciliation mismatch`. |
| M5 | On the new Host kill the 2 remaining members. | `[EncounterRegistry] Encounter#<id> resolved at registry tick <T>; its cooldown starts now.` Status on all peers: `Resolved` then `Cooldown`, alive 0. |
| M6 | Keep all players outside the zone; wait the cooldown. | `[EncounterRegistry] Encounter#<id> reactivated. Cycle 1: 3 enemies spawned.` Status on all peers: `Active`, `ReactivationCount` 1, members 3, alive 3. |
| M7 | Repeat: stand inside the zone while the room is `Reactivable`. | No reactivation while a player is inside; it happens after leaving. After the room resolves again, no further reactivation (`MaxReactivations` 1). |
| M8 | New raid scenario: resolve the room, then kill the Host **during the cooldown** (wait > 10 s after resolution). | After migration the room is still `Resolved`/`Cooldown` (not `NotActivated`), the cooldown is not restarted from zero, and it becomes `Reactivable` no sooner than the remaining cooldown **plus the recovery window** (frozen clock). Record the measured time. |
| M9 | Cycle-aware keys: after M6, kill a member of cycle 1 and check an earlier-cycle corpse. | Only the cycle-1 member changes the alive count; earlier corpses change nothing. |
| M10 | End the raid and start another from the Town flow. | No encounter state from the previous raid; rooms `NotActivated` until bootstrap. |

Record any `reconciliation mismatch` warning verbatim (unrecorded/missing/unexpected counts) and whether it matches the snapshot lag.

**Result log template**

```text
Date / build / commit: 
Scene and authored room(s) (id, markers, cooldown, MaxReactivations): 
Peers (Host / Client A / Client B), promoted peer: 

| # | Result (PASS/FAIL/N/A) | Observed status (state, react., members, alive) Host / A / B | Observed logs (verbatim) | Notes (snapshot age, timing) |
| M1 |
| M2 |
| M3 |
| M4 |
| M5 |
| M6 |
| M7 |
| M8 |
| M9 |
| M10 |

Unexpected errors/warnings: 
Verdict (S5 multi-process): 
Executed by: 
```

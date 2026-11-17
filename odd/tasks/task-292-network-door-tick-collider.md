# TASK-292 — NetworkDoor blocker on fixed simulation ticks and host-authoritative close prompt

HacknPlan: TASK-292 (#799, Sprint 18). Execution tracker only; scope and acceptance live in HacknPlan.
Owning docs: `Project Grimhold/Docs/Architecture/PlayerInteractionArchitecture.md` (TASK-276 door section), `SessionLifecycleAndSpawning.md`.

## Specs

- **S1** (Alcance 1): "Eliminar la activación/desactivación de _blocker.enabled dentro de Render() en NetworkDoor.cs."
- **S2** (Alcance 1): "Asegurar que tanto el host como los clientes remotos sincronicen el estado del Collider2D en ticks fijos de simulación a partir de [Networked] DungeonDoorState, garantizando que la física 2D coincida con el tick de red."
- **S3** (Alcance 2): "Sincronizar la disponibilidad de interacción entre peers para que el prompt de \"Cerrar puerta\" no se muestre basándose únicamente en ocupación local cuando la autoridad del host va a rechazar el cierre."
- **S4** (Criterios de aceptación, verbatim):
  - "El Collider2D físico de paso nunca es modificado dentro de Render(), operando únicamente en ticks de simulación de red tanto en el host como en clientes remotos."
  - "Los clientes que atraviesan el umbral no experimentan desincronizaciones de colisión 2D durante ticks de resimulación."
  - "El prompt de interacción refleja la viabilidad de la acción según el estado autoritativo del host."
- **S5** (Validación, verbatim): "Tests en EditMode/PlayMode verificando que _blocker.enabled cambie solo durante ticks de red." / "Verificación de paso e interacción en escenario Host/Client."

Out of scope (verbatim): "Arte de tilesets o nuevos prefabs de puertas." / "Puertas secretas o destructibles."

## Tasks

| ID | Specs | Route | Status | Commit |
| --- | --- | --- | --- | --- |
| T1 | S1, S2, S4a, S5 | inline TDD (PlayMode Single) — Render never mutates `_blocker`; every simulated tick re-applies it from `State` | done (RED L6, GREEN L7) | fix(doors) TASK-292 commit |
| T2 | S2, S4a, S4b, S5 | code — remote clients simulate the door (`Runner.SetIsSimulated`) so the blocker follows `State` per tick, incl. resimulation; proof = manual Host/Client (L8) | code done, Host/Client proof pending | fix(doors) TASK-292 commit |
| T3 | S3, S4c, S5 | inline TDD — host replicates doorway occupancy; non-authority close availability reads it; host still validates live | done (RED L10, GREEN L11); client prompt proof = manual Host/Client | fix(doors) TASK-292 commit |
| T4 | S1-S3 | worker docs — update `PlayerInteractionArchitecture.md` door section (prompt availability, blocker apply point) | done (L12) | fix(doors) TASK-292 commit |
| T5 | S5 | Unity CLI — compile + door EditMode/PlayMode suites; independent verify (Risk 4, 6); list manual Host/Client checks | done (L14-L17); manual Host/Client pending | fix(doors) TASK-292 commit |

## Log

- **L1** (user, verbatim): "revisa TASK-292 — Corregir collider 2D en simulación fija y prompt de interacción en Puertas de Red y empeza la implementacion siguiendo ODD"
- **L2** (evidence): `NetworkDoor.Render` calls `ApplyBlockerState()` on every peer (`NetworkDoor.cs` Render); `FixedUpdateNetwork` returns early without authority, so remote clients only update the blocker in Render.
- **L3** (evidence, Fusion 2 docs "What's new 2.0" / migration from v1): "`FixedUpdateNetwork` no longer executes on proxies by default." Enable with `Runner.SetIsSimulated(Object, true)` in `Spawned` (Client-Server modes only).
- **L4** (evidence): close availability uses a local `Physics2D.OverlapBox` in `TryGetAvailableTransition`; `LocalInteractionCandidateSource` selects the prompt through `CanInteract`, so a client advertises "Cerrar puerta" from its own (interpolated) physics world.
- **L5** (decision): `Spawned` keeps the one-time initialization of the blocker from replicated `State` (spawn/restore, not presentation); after spawn only simulation ticks and the authoritative `Interact` (itself run from a tick) mutate it. No commits/branch: repo policy requires explicit authorization.
- **L6** (RED): `Render_NeverMutatesBlocker_NextSimulationTickReappliesReplicatedState` failed — "Render is presentation: it must never toggle the physics blocker. Expected: False But was: True" (16/17 passed).
- **L7** (GREEN): `DungeonDoorPlayModeTests` 17/17 passed after moving the apply into `FixedUpdateNetwork` (before the authority guard) and removing `Render`.
- **L8** (decision): the only in-repo Host/Client precedent (`ProtectiveOrbsAbilityPlayModeTests`) is `[Explicit]`, needs Photon cloud, and in Multiple peer mode `NetworkDoor`'s global `Physics2D.OverlapBox` cannot separate host/client occupancy worlds. Remote-client simulation and client prompt are proven by manual Host/Client (MPPM) checks, not an automated cloud test.
- **L9** (incident): a test run started while `NetworkDoor.cs` failed to compile (CS0102 name clash `IsDoorwayOccupied`) left the Editor pipeline stuck in `running`; fixed by renaming the private probe to `ProbeDoorwayOccupancy`, `unity command test cancel`, and a compile gate in the local runner script.
- **L10** (RED): `DoorwayOccupancy_IsReplicatedFromAuthoritativeTicks_OnlyWhileOpen` failed — "The host must replicate that it will reject closing. Expected: True But was: False" (17/18).
- **L11** (GREEN): `DungeonDoor` PlayMode 18/18 passed; `DungeonDoor` EditMode 72/72 passed. API: `[Networked] private NetworkBool DoorwayOccupiedVerdict` exposed as `public bool IsDoorwayOccupied` (avoids bool/NetworkBool CS0172 ambiguity).
- **L12** (docs): `PlayerInteractionArchitecture.md` door section updated (prompt availability, tick-only blocker, `SetIsSimulated`, validation list).
- **L14** (verify, gentle-ai-verify): S1/S4a pass; S2/S3 pass in code, Host/Client unproven; defect S4b — `NetworkDoor` (order 0) simulated after `PlayerMovementNetworkController` (`[DefaultExecutionOrder(-10)]`), so the first resimulated tick moved against the previous tick's blocker. Fix: `[DefaultExecutionOrder(-20)]` on `NetworkDoor` + doc note.
- **L15** (RED): `MovementOrderSimulation_CollidesWithBlockerOfTheSameTick` failed — "Player movement simulates after the door, so it must collide with this tick's blocker. Expected: False But was: True" (18/19).
- **L16** (GREEN): after `[DefaultExecutionOrder(-20)]`, `DungeonDoor` PlayMode 19/19 and EditMode 72/72 passed.
- **L17** (pre-existing, out of scope): EditMode `CatacombsSceneCompositionTests.Sanctuaries_AreUnmodifiedPrefabInstancesAtTheEightAuthoredPoints` fails — "Unexpected sanctuary override: m_IsActive" (7/8); scene and test untouched by this task.
- **L13** (out of scope, not touched): Editor regenerated `Assets/Data/Minimap/DungeonGrayboxMinimapLayout.asset` (new texture fileID/hash) and line-ending-only changes in `ProjectSettings/ProjectAuditorSettings.asset`, `ProjectSettings/QualitySettings.asset` during test runs.

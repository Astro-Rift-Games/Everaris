# ODD Execution Tracker — US-87 (#845): Navegación y comportamiento de enemigos en Catacumbas

- **HackNPlan Story:** US-87 (#845) — Sprint 19 (`Everaris`, projectId `239834`)
- **Branch:** `feat/us-87-enemy-navigation-behavior` (parallel to `New-Testing`)
- **Authoritative Sources:**
  - HackNPlan US-87 (#845) and child tasks TASK-311 (#846) .. TASK-320 (#855), plus TASK-41 (#84)
  - Architecture: `Project Grimhold/Docs/Architecture/PathfindingArchitecture.md`, `EnemyCombatArchitecture.md`
  - Live Game Design (`grimhold-docs`): `10 - Reglas de Sesión de Dungeon` (§10.1, §13), `Espectro de Catacumbas` (§1, §5, §9, §11)

## Acceptance Criteria Checklist (from US-87 & Child Tasks)

- [x] **TASK-311 (#846):** Línea de visión de enemigos bloqueada por paredes (`EnemyVisionRules`, `_obstacleLayer = 2112` en `NetworkEnemy.prefab` y variantes, exigencia de LOS antes de atacar).
- [x] **TASK-312 (#847):** Grid de navegación con margen acorde al collider real (`OverlapCircle` con `0.42 u`, alineación a múltiplos de `0.5 u`, `PathfindingGridConfig.asset` y `PathfindingArchitecture.md` actualizados).
- [x] **TASK-320 (#855):** Rendimiento y calidad del solver A* (`PathfindingGrid.SharedSolver` O(1) en memoria, `BinaryMinHeap` indexado con `PushOrDecreaseKey`, warning exacto de límite, `SmoothPath` O(n) validando tramo final, `_maxPathIterations = 8000`).
- [x] **TASK-313 (#848):** Robustez del navegador ante caminos inexistentes y cambios del mapa (fallback parcial al nodo alcanzable más cercano, respeto de cadencia y rate-limit de warning, llegada real `HasReachedDestination`, timeout `IsStuck` para patrulla/persecución, `PathfindingGrid.TopologyVersion` para invalidación por `NetworkDoor`, escalonado inicial determinista con autoridad de estado).
- [x] **TASK-314 (#849):** Leash y retorno a Home Area de Slimes y Ranged (`EnemyLeashRules`, `EnemyReturnHomeState` en `EnemyFSM`, `[Networked] HomeAreaPosition`, prioridad de Leash sobre `AggroAlertTimer`, lockout configurable anti-oscilación).
- [x] **TASK-315 (#850):** Specter: persecución navegable rodeando paredes (`EnemyPathfindingNavigator` habilitado en `NetworkEnemySpecter.prefab` y `EnemyCharacter.cs`) y retorno a Home Area mediante desvanecimiento sin consumir `ReactiveDisplacementTimer` (conserva CA-01..CA-13).
- [x] **TASK-316 (#851):** Ataque enemigo con timing autoritativo (`EnemyAttackAnticipationRules`, `TickTimer` en `EnemyCombatAIController` desacoplado de `Animator`/`EnemyAttackAnimationListener`), histéresis de rango (`EnemyRangeHysteresisRules` + `MinAttackStayTimer`) y anticipación legible en Host/Cliente/Proxies (`EnemyCombatPresenter`, `EnemyAnimatorView`).
- [x] **TASK-317 (#852):** Locomoción enemiga con aceleración/frenado configurables y giro limitado (`EnemyLocomotionRules`), histéresis de 6 sectores visuales (`CharacterVisualDirectionResolver.ResolveWithHysteresis` en `CharacterAnimatorView`) y `_enablePathSmoothing = 1` en `NetworkEnemy.prefab`.
- [x] **TASK-318 (#853):** Separación entre enemigos sin trabarse en pasillos de 1 tile y reparto determinista en slots navegables alrededor del objetivo (`EnemyCrowdRules`, `EnemyCrowdCoordinator` owned by `PathfindingGrid`).
- [x] **TASK-319 (#854):** Enemigo Ranged: franja de distancia preferida, retroceso con verificación de espacio/Leash y búsqueda de punto de tiro con LOS dentro de Leash (`EnemyRangedPositioningRules`, configurado en `NetworkEnemyRanged.prefab`).

## Execution Log & Evidence

- **TDD RED → GREEN verified via Unity CLI (`6000.6.8f1`)**:
  - `EnemyVisionAndLosTests`: 6 FAIL (RED) → **10/10 PASS** (GREEN)
  - `PathfindingGridAndSolverTests`: 4 FAIL (RED) → **9/9 PASS** (GREEN)
  - `PathfindingGridRefreshRegionTests` (regression): **4/4 PASS**
  - `EnemyPathfindingNavigatorRobustnessTests`: 3 FAIL (RED) → **5/5 PASS** (GREEN)
  - `EnemyLeashAndSpecterPrefabTests`: 3 FAIL (RED) → **5/5 PASS** (GREEN)
  - `SpecterLeashPlayModeTests` (PlayMode, real Fusion runner): 1 FAIL (RED) → **1/1 PASS** (GREEN)
  - `EnemyAuthoritativeAttackTests`: 3 FAIL (RED) → **6/6 PASS** (GREEN)
  - `EnemyLocomotionAndFacingTests`: 4 FAIL (RED) → **6/6 PASS** (GREEN)
  - `EnemyCrowdAndRangedTests`: 2 FAIL (RED) → **7/7 PASS** (GREEN)
- **Additional Regression Suites**:
  - EditMode: `SpecterRulesAndCombatTests` (39/39), `EnemyTargetReferenceTests` (7/7), `CharacterVisualDirectionResolverTests` (35/35), `Kinematic2DMovementMotorTests` (4/4) — **Total EditMode verified: 137/137 PASS**.
  - PlayMode: `SpecterAudioPresenterPlayModeTests` (7/7), `DungeonDoorPlayModeTests` (19/19), `SpecterLeashPlayModeTests` (1/1) — **Total PlayMode verified: 27/27 PASS**.
- **CatacombsMap Editor Measurement**:
  - `grid=583x411 (239,613 nodes)`, `walkable=221,460 (92.4%)`, `clearance=0.42`, `maxIter=8000`, `sharedSolver=True`, `failed=0`, `smoothWallClips=0`, Unity Console errors: `0`.
- **Remaining Manual Validation**:
  - Solo and Duo (Host + Client) visual/multiplayer playthrough in `CatacombsMap` (see final delivery checklist).

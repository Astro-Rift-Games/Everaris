# TASK-307 — Integrar Sprites y Animaciones de Grietas (Pared y Suelo) en el Sistema de Colapso

**Objective:**
Integrar los sprites y animaciones de grietas de suelo y pared en el sistema de Colapso de la Dungeon (`CatacombsMap.unity`), con generación aleatoria determinista respetando márgenes estrictos sobre superficies válidas (suelo o pared), iluminación emisiva y luz local turquesa legible en fase Colapso, ordenamiento coherente sin bloquear navegación ni tapar personajes, sincronización determinista Host/Cliente/late-join y suite completa de tests EditMode (TDD).

**Baseline:** `New-Testing` (`d1362606`).

**Sources:**
- HacknPlan `TASK-307` (`#839`): *Integrar Sprites y Animaciones de Grietas (Pared y Suelo) en el Sistema de Colapso*
- Design Docs: `20 - Sistema de Colapso de Dungeon` (§3, §4, §5, §6), `Catacumbas — Tileset Modular de Paredes`, `Dirección Visual de Dungeon - Catacumbas`
- Architecture: `Project Grimhold/Docs/Architecture/DungeonPressureArchitecture.md`, `RaidGenerationLifecycleArchitecture.md`
- User-confirmed rules:
  - Suelo: Etapa 1 (`Normal`) sin grietas; Etapa 2 (`Reinforcements`) lote 1 sin activación (`Stage0-Spawn` -> `Stage1`); Etapa 3 (`CriticalPressure`) lote 1 con activación + lote 2 sin activación; Etapa 4 (`Collapse`) lotes 1 y 2 con activación.
  - Pared: Etapa 1 (`Normal`) sin grietas; Etapa 2 (`Reinforcements`) `Stage0-Spawn` -> `Stage1`; Etapa 3 (`CriticalPressure`) loop `Stage1 + Stage2`; Etapa 4 (`Collapse`) loop `Stage2 + Stage3 + Stage4`, con tiempo entre frames configurable desde el Inspector.
  - Ubicaciones aleatorias por raid respetando margen estricto para no sobresalir de su superficie; suelo solo en suelo, pared solo en pared.
  - Escena objetivo exclusiva: `CatacombsMap.unity`.

**Acceptance Criteria (quoted from `#839` + user rules):**
- Cuando surge una grieta nueva en E1 o E2 se reproduce la animación progresiva `Stage0-Spawn` desde un punto y finaliza en `Stage1` sin cortes ni pop visual.
- Durante E2 y E3, al activarse una grieta de suelo ejecuta en orden `Stage2` (telegraph en loop) -> `Stage3` (erupción) -> `Stage4` (enfriamiento) -> `Stage1` en Host y Clientes.
- En paredes: Etapa 1 sin grietas; Etapa 2 `Stage1`; Etapa 3 `Stage1 + Stage2`; Etapa 4 `Stage2 + Stage3 + Stage4`, con intervalo entre frames configurable en Inspector.
- En fase Colapso (luz global rojo oscuro y antorchas apagadas), el brillo turquesa de las grietas activas y su luz local permanecen claramente legibles.
- Las grietas de suelo no tapan a los jugadores ni al cursor y respetan margen para no sobresalir del suelo; las de pared encajan sobre la cara frontal (`32 px`) sin desbordar ni bloquear colisiones/rutas.
- En clientes remotos o tras reconexión, las grietas reflejan su estado visual vigente sin reproducir spawns ni erupciones pasadas.

**Exclusions:**
- Cambios en reglas de daño, knockback, inestabilidad u ondas autoritativas (`US-74` / `TASK-255..258`).
- Producción o edición artística de nuevos `.png` o SFX finales (`SFX-03` / `#835`).
- Escena `Gameplay.unity` (antigua).

**Work Breakdown:**
- [x] Phase 1: Normalizar rects de `Stage1.png.meta` (Floor y Wall) a `48x48` preservando GUIDs/internalIDs.
- [x] Phase 2 (TDD): Reglas de fase, semilla determinista y máquina de estados de grieta de suelo (`CollapseRiftTypes`, `CollapseRiftPhaseRules`, `CollapseRiftStateMachine`).
- [x] Phase 3 (TDD): Secuenciador progresivo de grietas de pared por etapa (`CollapseWallRiftStepper`).
- [x] Phase 4 (TDD): Servicio puro de colocación aleatoria con márgenes estrictos en suelo y pared (`ICollapseSurfaceGrid`, `CollapseRiftPlacementService`, `TilemapCollapseSurfaceGrid`).
- [x] Phase 5: Shader emisivo URP 2D (`CollapseRiftEmissive.shader`) + Material (`CollapseRiftEmissive.mat`) + `CollapseRiftConfig` + `DefaultCollapseRiftConfig.asset`.
- [x] Phase 6: Componentes visuales (`CollapseFloorRiftVisual`, `CollapseWallRiftVisual`), `CollapseRiftPresenter`, prefabs (`FloorRift.prefab`, `WallRift.prefab`) e integración en `CatacombsMap.unity` + Editor preview (`DungeonPressureAmbientPresenterEditor`).
- [x] Phase 7: Tests EditMode de integración (`CollapseRiftPresenterTests`), compilación, consola Unity sin errores y revisión de diff.

**Validation & Status:**
- TDD RED -> GREEN -> REFACTOR executed via Unity CLI (`unity command script recompile` + `unity command test run`).
- Compilation: Passed (`status: completed, failed: false, compilationFailed: false`, `consoleErrors: 0`).
- EditMode Tests:
  - Filter `Collapse`: **44/44 Passed** (0 failed, including 24 new tests across `CollapseRiftStateMachineTests`, `CollapseWallRiftStepperTests`, `CollapseRiftPlacementServiceTests`, `CollapseRiftPresenterTests`, covering `CatacombsMap.unity` serialized wiring and same-frame raid-seed regeneration).
  - Filter `DungeonPressure`: **66/66 Passed** (0 failed — zero regressions).
- Dry-run placement verified on live `CatacombsMap.unity` tilemaps: `180/180` floor rifts (`90 + 90`) and `80/80` wall rifts placed cleanly respecting margins and all 8 `ExtractionSanctuary` exclusion zones.
- PlayMode verification: Confirmed by user in `CatacombsMap.unity` that floor and wall rifts spawn, position cleanly within surfaces, and animate across all pressure stages.
- Remaining manual checks: Multiplayer Host + Client parity and late-join visual check in a two-client session.

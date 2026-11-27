# TASK-330 — Ciclo de vida de encuentros de la Dungeon
HacknPlan item 885, Sprint 19. Branch `feat/task-330-encounter-lifecycle` from New-Testing. Execution tracker only; HacknPlan owns scope.
Live GD: 10 - Reglas de Sesión de Dungeon §9.1, §9.2, §10.2, §10.3 (read 2026-11-27, modified 2026-11-27T04:58Z).

## Specs
S1. AC: “Cada encuentro de una raid generada tiene un identificador único y un estado consultable.” Doc 10 §9.2: “Cada Encounter posee un EncounterID y sus enemigos continúan perteneciendo a dicho encuentro aunque abandonen físicamente la sala durante una persecución.”
S2. Doc 10 §9.2: “El ciclo básico es: No activada → Activa → Resuelta → Cooldown → Reactivable.” “Un Encounter se considera resuelto cuando todos sus enemigos fueron eliminados válidamente. El cooldown comienza en ese momento, no cuando el jugador abandona la sala.”
S3. AC: “Un encuentro resuelto sólo se reactiva en las condiciones del Doc 10.” Doc 10 §9.2: “Una sala sólo puede reactivarse si el Encounter anterior está resuelto, el cooldown finalizó, no hay jugadores dentro de la zona de generación, existe capacidad global disponible, la fase de Dungeon permite generar y no se alcanzó MaxReactivations.” “Los valores concretos de cooldown y número máximo de reactivaciones quedan para Balance; el sistema debe soportarlos como parámetros configurables.”
S4. AC: “Cambiar un perfil de encuentro cambia su composición sin tocar código.” Doc 10 §9.1: “Las salas capaces de generar PvE utilizan una primera validación ponderada para determinar si existe Encounter. Si la validación resulta positiva, la composición se construye mediante un Threat Budget.” “La sala puede definir MinEnemies, MaxEnemies, AllowedEnemies y un Encounter Profile que modifique pesos de composición, por ejemplo enjambre, melee, ranged, mixto o especial.” Task: “Decisión pendiente: los perfiles concretos y sus valores salen de Game Design si el doc no los fija.”
S5. AC: “El estado de los encuentros es coherente en todos los peers y tras Host Migration.” Doc 10 §10.2: “Desactivar o simplificar la IA por distancia no elimina ni resetea al enemigo. Debe conservar vida, posición y estados relevantes del Encounter.”
S6. Validación: “Tests automatizados del ciclo de vida: RED observado antes del cambio y GREEN después.” “Validación manual multi-proceso con Host Migration, con logs registrados.”
S7. Fuera de alcance: “Nuevos tipos de enemigo.” “Cambios de IA o de pathfinding (US-87).” Leash/Home Area (US-87, per enemy) is not encounter reactivation.

## Tasks
- [x] T1 [S1-S3,S6] Pure encounter domain: EncounterId, EncounterState, lifecycle (membership, valid elimination, resolution, cooldown) and reactivation rules with configurable cooldown/MaxReactivations. Parent inline, EditMode TDD. Commit 09430aa7.
- [x] T2 [S4,S6] Encounter Profile ScriptableObject and room composition (initial weighted validation, Min/Max/AllowedEnemies, profile weight modifiers, Threat Budget); neutral default preserving current composition. Parent inline, EditMode TDD.
- [ ] T3 [S1,S4,S7] Authored `EncounterRoom` scene component (generation zone, spawn markers, profile, stable id); bootstrap spawn per room; enemies carry networked EncounterId; CatacombsMap rooms authored. Parent, Unity CLI.
- [ ] T4 [S1,S2,S5,S6] Replicated encounter state on State Authority, resolution from valid enemy deaths, Host Migration restore. Parent, EditMode + PlayMode.
- [ ] T5 [S3,S6] Runtime reactivation: players in generation zone, global capacity, Dungeon phase gate, MaxReactivations; respawn composition. Parent.
- [ ] T6 [S5,S6] Architecture doc update and manual multi-process Host Migration validation log. Parent + human.

## Log
L1. revisa la tarea con el titulo TASK-330 — Ciclo de vida de encuentros de la Dungeon y empeza la implementacion siguiendo ODD
L2. Exploration: no room/encounter concept exists. `SpawnGroupDefinition` groups by type map-wide (one enemy group with all EnemySpawnMarkers, `CatacombsSceneCompositionTests.cs:104-121`); no EncounterID, state or respawn; enemies get new NetworkIds on Host Migration and the population tracker reconciles by count (`NetworkSpawnManager.cs:4000-4014`). Assessed as three dependent deliverables (rooms+identity/state+HM, reactivation, profiles).
L3. Owner decisions (2026-11-27): “Una sola TASK, en commits por slice”; rooms: “Componente autoral en la escena”; values: “Defaults neutros marcados” (default profile preserves current composition; cooldown/MaxReactivations defaults documented as non-GD, pending Balance). Local work-unit commits on the feature branch authorized by that choice; push not authorized.
L4. Open for T5: which Dungeon phase(s) allow generation (“la fase de Dungeon permite generar”) is not fixed in §9-§10; resolve before T5. “Eliminados válidamente” interpreted as death on State Authority (raid cleanup/despawn does not resolve); confirm in T4.
L5. T1 RED: EncounterLifecycleTests 29/29 failed (NotImplementedException skeleton, after confirmed recompile). GREEN: 29/29 passed. Defaults decision applies at T2/T5 config; domain has no defaults.
L6. T2 RED: EncounterCompositionPlannerTests 21/21 failed (NotImplementedException). GREEN: 21/21; filter "Encounter" 53/53 (incl. existing WeightedEnemySelector GenerateEncounter tests). Spatial validation = room spawn-point capacity (navigation checks not covered). Neutral default = no profile or an EncounterProfile without modifiers; the default asset is created with the rooms in T3. WeightedEnemySelector's UnityEngine.Random fallback is untouched; the planner requires a System.Random.

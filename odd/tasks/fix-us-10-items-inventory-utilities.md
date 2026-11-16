# FIX-US-10 — Coherencia de items, inventario, utilidades y tesoros del Sprint 15 (Docs 07 y 14)

HacknPlan Story: FIX-US-10 (ID 744). Tasks: FIX-67 (ID 756), FIX-68 (ID 757), FIX-69 (ID 758), FIX-74 (ID 763, already Testing), FIX-75 (ID 764), FIX-76 (ID 765), FIX-77 (ID 766), FIX-78 (ID 767).
Branch: `fix/us-10-items-inventory-utilities`, branched from `New-Testing`.
Execution tracker only: scope and acceptance criteria quoted directly from HacknPlan; nothing here re-scopes them.

## Specs

- **S1 (FIX-67) — Quick Slots: eliminar el gate de depleción muerto y probar el HUD agregado (TASK-230).**
  - Scope: Depuración y remoción de validaciones redundantes de agotamiento en la lógica de las ranuras de acceso rápido (`TryReleaseDepleted`, `_canReleaseDepleted`, `QuickSlotReleaseGate`), manteniendo una comprobación limpia y directa de disponibilidad de consumibles.
  - Scope: Verificación de agregación y cómputo correcto en la interfaz de usuario (`RaidQuickSlotHudPresenter`) cuando existan múltiples conjuntos de consumibles del mismo tipo con orígenes diversos.
  - AC: grep de `TryReleaseDepleted` sin resultados; suite de QuickSlots en verde.
  - AC: El nuevo test falla si se rompe la suma o el estado deshabilitado.
- **S2 (FIX-68) — NetworkLootPickup: rechazo sin excepción para receptor no-raid y prefijo canónico por origen (TASK-228).**
  - Scope: Rechazo explícito y seguro ante solicitudes de recogida de botín en el escenario provenientes de entidades que no formen parte de la expedición activa, evitando excepciones en el procesamiento.
  - Scope: Preservación del origen legítimo del botín al incorporarlo al inventario del participante.
  - AC: Un receptor no-raid recibe Rejected y el pickup queda intacto.
  - AC: El escenario mixto produce el resultado acordado y está cubierto por un test.
- **S3 (FIX-69) — Items legacy duplicados (healthpotion, Bone, Coins) y arquetipos visuales compartidos del catálogo MVP (TASK-226).**
  - Scope: Determinación de la identidad canónica única para la poción de vitalidad y migración homogénea de sus referencias en catálogos, tiendas, ranuras rápidas y perfiles persistidos.
  - Scope: Depuración de entradas legacy en el catálogo de objetos y definición del mapeo de representaciones visuales provisorias para items en producción.
  - AC: Un único ID de poción de vida en Unity y backend; perfiles existentes migran sin pérdida.
  - AC: El catálogo no contiene items con ObjectValue 0 y familia vacía salvo decisión registrada.
- **S4 (FIX-75) — Consumibles de maná: tests de red de interrupción y maná lleno, y audio de consumo sin asignar (TASK-231).**
  - Scope: Evaluación estricta del estado de maná del jugador previo al consumo de la poción de maná, rechazando la acción si la reserva ya se encuentra colmada.
  - Scope: Interrupción inmediata del consumo ante la recepción de daño durante la canalización sin consumo del item ni restauración parcial de recursos.
  - Scope: Asignación y reproducción de retroalimentación auditiva para el consumo de maná o confirmación explícita de silencio según el diseño de audio.
  - AC: Ambos casos de red tienen test en verde.
  - AC: Completar un consumo reproduce el audio asignado (verificación en Play Mode).
- **S5 (FIX-76) — Detector Arcano sin interrupción por movimiento ni filtro de contenedores; Brújula con búsqueda en escena (TASK-234).**
  - Scope: Interrupción automática de la canalización del Detector Arcano ante cualquier desplazamiento del personaje, garantizando coherencia con el resto de habilidades canalizadas sin activar cooldown.
  - Scope: Filtrado selectivo de recipientes y elementos del entorno para detectar únicamente aquellos que contengan propiedades arcanas relevantes según la especificación de diseño.
  - Scope: Optimización del rastreo de puntos de interés en la Brújula mediante registro reactivo de elementos (`EntityRegistry`) en lugar de búsquedas globales en el escenario (`FindObjectsByType`), implementando tiempos de recarga adecuados.
  - AC: Moverse durante los 2.0 s cancela sin cooldown.
  - AC: Sólo se resaltan elementos según la decisión registrada.
  - AC: La Brújula no hace búsquedas de escena durante gameplay.
- **S6 (FIX-77) — Ganzúa iniciada reportada como Rejected y SmokeZoneRegistry con estado estático global (TASK-235, TASK-233).**
  - Scope: Notificación y estado de progreso 'Iniciado/En Curso' para la acción de forzado de cerraduras con ganzúa, reemplazando el reporte erróneo de rechazo inicial.
  - Scope: Aislamiento e independización del registro de zonas de humo por cada sesión de juego o instancia de mazmorra (`SmokeZoneRegistry`), evitando estados estáticos globales compartidos entre runners.
  - Scope: Cobertura PlayMode del consumo de proyectil arrojadizo al impactar un muro.
  - AC: Ningún consumidor recibe Rejected en un inicio exitoso.
  - AC: Dos runners en el mismo proceso no comparten zonas de humo.
  - AC: Test de impacto en muro en verde.
- **S7 (FIX-78) — Auto-reanimador: auto-activación tras el handoff y salud restaurada provisional (TASK-236).**
  - Scope: Definición formal de diseño sobre el modo de activación del auto-reanimador transferido entre compañeros: activación contextual mediante comando del jugador vs. activación automática inmediata tras la transferencia.
  - Scope: Establecimiento del valor definitivo de vitalidad restaurada al completarse la auto-reanimación y actualización de la documentación de arquitectura correspondiente.
  - AC: El comportamiento tras el handoff y la salud restaurada coinciden con la decisión y tienen test.

## Tasks

- [x] T1 (S1 - FIX-67): Eliminar `TryReleaseDepleted`, `_canReleaseDepleted` y `QuickSlotReleaseGate`; agregar suite EditMode para `RaidQuickSlotHudPresenter` probando agregación multi-origen y desactivación en 0 o abatido.
- [x] T2 (S2 - FIX-68): Modificar `NetworkLootPickup.Interact` para retornar `Rejected` sin lanzar `InvalidOperationException` ante receptores no-raid; resolver y probar el comportamiento de recepción mixta.
- [x] T3 (S4 - FIX-75): Asignar clip de audio de consumo en los assets de pociones; agregar tests PlayMode de red para interrupción por daño y rechazo con maná lleno en `CON_MANA_POTION`.
- [x] T4 (S5 - FIX-76): Cancelar canalización de Detector Arcano ante movimiento del jugador sin activar cooldown; filtrar `NetworkLootContainer` por contenido arcano; eliminar `FindObjectsByType` en `ExecuteRuneCompass`.
- [x] T5 (S6 - FIX-77): Ajustar `TryBeginLockpicking` para no reportar `Rejected` en inicio exitoso; aislar `SmokeZoneRegistry` por `NetworkRunner`; agregar test PlayMode de impacto en muro para consumibles arrojadizos.
- [x] T6 (S3 - FIX-69 & S7 - FIX-78): Resolver decisiones de diseño pendientes y formalizar según corresponda. Identidad canónica 'healthpotion' unificada en Unity/Backend con alias transparente y migración en perfiles/quickslots; Bone y Coins formalizados con _family: Legacy; auto-reanimador formalizado a 25 HP definitivos y auto-activación inmediata tras handoff con suite en verde.
- [x] T7: Validación integral (EditMode, PlayMode, backend npm test, 0 errores de compilación) y reporte al usuario.
- [x] T8: Sorting Layers (items en suelo, partículas de explosión y humo, breakables a layer 'Characters' / 'VFX') y feedback de forzado de cofres (diferir apertura de panel de cofre en RaidInventoryPresenter hasta terminar canalizado de ganzúa + barra de progreso dinámica y porcentaje en InteractionHudPresenter).

## Log

- L1 (2026-11-16): FIX-US-10 activada en HacknPlan. Historias y tareas 744, 756, 757, 758, 764, 765, 766, 767 movidas a `In progress`. Rama paralela `fix/us-10-items-inventory-utilities` creada desde `New-Testing`. Tracker ODD inicializado.
- L2 (2026-11-16): T1, T2, T3, T4 y T5 completadas y verificadas con tests automatizados (RaidQuickSlotHudBehaviorTests en EditMode, NetworkLootPickupPartialPickupPlayModeTests, TimedConsumablePlayModeTests, UtilityExpeditionPlayModeTests, LockpickInteractionPlayModeTests y ThrowableConsumablesPlayModeTests en PlayMode). 100% verde.
- L3 (2026-11-16): T6 y T7 completadas. Decisiones de diseño acordadas con el usuario aplicadas formalmente. Tests RED -> GREEN observados en LegacyItemUnificationTests (3/3 pass), MvpItemCatalogTests (10/10 pass), ConsumableUtilitySpriteAssetsTests (10/10 pass), PlayerSelfRevivePlayModeTests (10/10 pass), y suite completa de backend (780/780 pass). 100% verde sin regresiones.
- L4 (2026-11-16): T8 completada tras feedback de usuario. Resuelto problema de capas: pickups y breakables reasignados a Sorting Layer 'Characters', partículas de bombas (explosión y humo) reasignadas a Sorting Layer 'VFX'. Resuelto feedback de cofres con cerradura: `RaidInventoryPresenter` ignora `LockpickingStarted` y no abre el panel de contenedor hasta que el cofre está desbloqueado; `InteractionHudPresenter` presenta barra de progreso horizontal dorada y porcentaje en tiempo real durante la canalización, manejando interrupción por movimiento o daño. Tests verificados al 100%.

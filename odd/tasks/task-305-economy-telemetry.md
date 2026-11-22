# TASK-305 — Telemetría Económica Interna en Backend para Playtests (Docs 22 y 23)

HacknPlan #823 (Sprint 18). Branch `feat/task-305-economy-telemetry` from `New-Testing`.
Execution tracker only: HacknPlan owns scope; this file quotes it and records decisions and evidence.

## Specs

- S1 (AC): "Cada transacción económica confirmada (venta, compra, recompra, mejora, respec, recompensa de misión) y cierre de raid emite el registro estructurado con sus categorías y montos netos sin afectar la atomicidad de la operación principal."
- S2 (AC): "Una operación rechazada nunca registra gasto ni generación efectiva como completada."
- S3 (AC): "Los registros permiten calcular Oro generado/gastado por categoría, tasa de extracción y valor perdido sin exponer tokens ni datos personales."
- S4 (Alcance 1): "Registrar métricas agregables por origen/categoría: Oro generado por fuente (ventas NPC, misiones reclamadas), Oro gastado por categoría (compras por tipo, recompra, mejoras de arma/armadura, respec), demanda bonificada utilizada, frecuencia de mejoras/materiales consumidos, y tasa de bloqueos (capacidad / MaxTransportable)."
- S5 (Alcance 1): "Registrar resumen por expedición para análisis interno: resultado (extracción/derrota/abandono), perfil aproximado de entrada (Naked/Budget/Geared según equipamiento llevado) y pérdidas materiales en derrota."
- S6 (Alcance 2): "La telemetría es estrictamente interna/analítica: nunca altera precios ni otorga ventajas automáticas, y no expone PII ni credenciales en logs."
- S7 (Validación): "Tests automatizados en backend verificando la emisión de eventos ante operaciones exitosas y ausencia de emisión ante rechazos (RED → GREEN)." / "npm test completo en verde sin regresiones sobre latencia ni atomicidad de commits."
- S8 (Doc 22 §5, live GD): "Naked | Arma gratuita de entrenamiento; entrada de recuperación con riesgo material mínimo." / "Budget | Equipo básico y consumibles; busca continuidad con inversión moderada." / "Geared | Equipo avanzado; mayor capacidad potencial de supervivencia, pero mayor pérdida material al morir."
- S9 (Doc 22 §14 / Doc 23 §12, live GD): "Telemetría para playtests: Oro creado por fuente, Oro gastado por categoría, saldo acumulado, patrimonio material estimado, pérdidas de equipo, tasa de extracción por perfil y expediciones necesarias para reconstruir equipamiento básico. El análisis es interno; no se aplican precios dinámicos ni ventajas automáticas por bancarrota." / "Telemetría sugerida: Oro ganado por venta/misiones; Oro gastado por categoría; saldo por jugador; demanda bonificada; distribución de compras por oferta; frecuencia y nivel de mejoras; materiales consumidos; recompra; tasa de bloqueo por capacidad y MaxTransportable; ganancias netas de ciclos comerciales."

Out of scope (HacknPlan): "Dashboards externos complejos, herramientas de BI de pago o paneles dentro del cliente de juego." / "Precios dinámicos, impuestos o subsidios automáticos basados en telemetría."

## Tasks

- [x] T1 — Core: `EconomyTelemetryEvent` model + `EconomyTelemetry.record()` best-effort post-commit sink (never throws, no PII) (S1-S3, S6). Route: inline TDD. Risk: none. Commit: 3f3caedf. Evidence: L5.
- [x] T2 — Commerce emitters: shopSell, shopBuy, shopTrade, shopBuyback; demand bonus flag; purchase offer type; no emit on rejection/replay (S1, S2, S4, S9). Route: inline TDD. Commit: 3f3caedf. Evidence: L5.
- [x] T3 — Upgrade + respec emitters (gold, materials, operation/level) (S1, S2, S4). Route: gentle-ai-worker (context backstop), TDD. Commit: 3f3caedf. Evidence: L6.
- [x] T4 — Raid close summaries: extraction / defeat / abandon with entry profile and lost value; INVENTORY_FULL block events (S1, S2, S4, S5, S8). Route: gentle-ai-worker after T3 (shares InventoryService.js), TDD. Commit: 3f3caedf. Evidence: L8.
- [x] T5 — Architecture doc for the telemetry contract + aggregation examples; full `npm test` (S3, S7). Route: inline. Commit: docs commit (this tracker). Evidence: L9, L10.

## Log

- L1 (user, verbatim): "revisa TASK-305 — Telemetría Económica Interna en Backend para Playtests (Docs 22 y 23) y empeza la implementacion siguiendo ODD"
- L2 (decision, user): storage → "Colección Mongo EconomyTelemetryEvent (Recomendado)": post-commit insert with try/catch, aggregatable; at-most-once (no transactions exist in backend; emit after guarded write).
- L3 (decision, user): mission rewards and MaxTransportable → "Follow-up: categoría lista, sin emisor (Recomendado)": category `mission_reward` defined without emitter (no backend mission claim path exists); backend block telemetry covers `INVENTORY_FULL` only; MaxTransportable lives in Unity.
- L4 (evidence, explorer): no `withTransaction` in `backend/src`; atomicity = single revision-guarded `findOneAndUpdate`. Replay paths return `alreadySecured` and must not emit. `shopSell` (single) does not pass weekly demand items (only `shopTrade` does) — pre-existing, reported, not changed here.
- L5 (evidence): RED observed (module missing; commerce 5/7 failing on absent events). GREEN: `node --require ./tests/setup.js --test tests/economy.telemetry.test.js tests/economy.telemetry.commerce.test.js` → pass 12, fail 0. Files: `backend/src/models/EconomyTelemetryEvent.js`, `backend/src/services/EconomyTelemetry.js`, `backend/src/services/InventoryService.js` (applyShopSale returns `sale`; recordCommerce after guarded write in shopSell/shopBuy/shopBuyback/shopTrade).
- L6 (evidence, worker T3): `recordUpgrade`/`recordRespec` after guarded write in `InventoryService.upgradeItem` and `ProgressionService.respec`; replays skip. RED 7 fail → GREEN `tests/economy.telemetry*.test.js` 26/26; full `npm test` 966 pass, 0 fail. New test `backend/tests/economy.telemetry.services.test.js`.
- L7 (decision, agent, technical): entry profile is approximate (S5 "perfil aproximado"); classified from carried equipment replacement value with provisional thresholds taken from Doc 22 §10 simulation assumptions (Naked 0, Budget ~200, Geared ~800 Oro reemplazo); raw carried values are stored so analysis can reclassify. Thresholds are not balance values.
- L8 (evidence, worker T4): `recordRaidClose` after guarded writes (extraction; defeat only when it cleared the reservation; non-rollback `clearPendingReservation` with items = abandon); `trackCapacityBlock` around `receiveItem` in shopBuy/shopBuyback/shopTrade/extraction, rethrows unchanged. RED 17/20 fail → GREEN `tests/economy.telemetry*.test.js` 45/45; full `npm test` 985 pass, 0 fail. `ENTRY_PROFILE_GEARED_MIN_GOLD = 800` (Budget = >0 and <800).
- L9 (T5): contract doc `Project Grimhold/Docs/Architecture/EconomyTelemetryArchitecture.md` (event shape, categories, emission/privacy rules, mongosh aggregations, known gaps).
- L10 (evidence, parent): final full `npm test` from `backend/` → tests 985, pass 985, fail 0, cancelled 0, skipped 0. Pending: mission_reward emitter (no backend claim path), MaxTransportable blocks (Unity-only), commits made on user authorization (feat 3f3caedf + docs).

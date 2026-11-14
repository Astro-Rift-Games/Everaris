# FIX-19 — Inventory mutation lock with a pending reservation and preservation of replaced reservations

HacknPlan FIX-19 (ID 689), parent FIX-US-04 (ID 685). Branch `fix/fix-19-reservation-inventory-guard`, stacked on `fix/fix-18-authoritative-result-validation` (FIX-18 is not merged yet; the defeat fix builds on its reservation binding).
Execution tracker only: scope and acceptance come from the HacknPlan item; nothing here re-scopes it.

## Specs

- **S1 — Mutation lock.** HacknPlan scope 1: "Bloqueo estricto de operaciones de mutación de inventario (movimiento entre alijo y equipamiento o reconfiguración) mientras exista una reserva de expedición activa y pendiente de resolución." Observable: "respuesta de rechazo explícita ante intentos de modificación concurrente." AC: "Con reserva pendiente, las tres mutaciones devuelven error y el documento queda sin cambios (revisión incluida)." The three mutations are `moveToLoadout`, `moveToStash` and `updatePreparedEquipment` (every schema version), rejected with the existing `409 RESERVATION_PENDING`, enforced atomically in the write filter.
- **S2 — Preservation.** HacknPlan scope 2: "Preservación y reintegración consistente del equipamiento e items reservados ante cancelaciones, reemplazos o reversiones de reserva, evitando descartes accidentales de objetos." Observable: "restitución completa de objetos al alijo o equipamiento activo sin pérdidas." AC: "Reemplazar o revertir una reserva con loadout/equipo no vacíos conserva todos los items: la suma total por lootId antes y después es idéntica." Covers replacement and rollback of an unadmitted reservation in every schema version, and a defeat commit for an older reservation must not clear a newer pending reservation (FIX-18 L14).
- **S3 — Validation.** "Test de backend que reproduzca el defecto: RED observado antes del cambio y GREEN después." "npm test completo y, si se tocan rutas de persistencia concurrente, npm run test:concurrency y test:integration (MongoDB local de test)." "Prueba contra el entorno desplegado sólo con autorización explícita; no se cierra sin evidencia registrada."

Out of scope: "Cambios de UI en Unity (FIX-26)."

## Tasks

- T1 — S1: guard the three mutations (v1/v2/v3) with `RESERVATION_PENDING`, in memory and in the atomic filter; update the tests that encode the opposite (`inventory.slots.test.js:170-176`, `concurrency/inventory.real.test.js:118-161`). Route: inline. Commit: pending.
- T2 — S2: `savePendingReservation` and `clearPendingReservation` restore the previous reservation regardless of loadout/equipment contents in v1; per-lootId sum invariant tests for replacement and rollback (v1/v2/v3). Route: inline. Commit: pending.
- T3 — S2: `DefeatCommitService` clears the pending reservation only when it is the one the result is bound to; check the `ExtractionCommitService` preparedEquipment overwrite. Route: inline. Commit: pending.
- T4 — S3: update `LocalPlayerPersistenceArchitecture.md`; run npm test, test:concurrency, test:integration; independent verify (Risk item 1: stored inventory loss). Route: inline + gentle-ai-verify. Commit: pending.

## Log

- L1 (user, 2026-11-14): "revisa FIX-19 — Bloqueo de mutaciones de inventario con reserva pendiente y preservación de reservas reemplazadas y empeza con la implementacion siguiendo ODD"
- L2 (exploration mwh0k5k9-1-rx83): current lines `moveToLoadout` :476, `moveToStash` :546, `updatePreparedEquipment` :689 in `backend/src/services/InventoryService.js`; none guarded, `:71` comment says legacy transfers stay usable during reservations on purpose. `savePendingReservation` :885-976 restores the previous unadmitted reservation unconditionally in v2+, but in v1 only when loadout and equipment are empty (:926-928), else the old reservation is overwritten and its items lost; rollback v1 branch :1047-1050 restores nothing. `DefeatCommitService.js:194` unsets `pendingReservation` without matching its reservationId. Architecture L478 ("a different id is rejected while pending") describes Unity's `LocalProfileStore`; the backend replaces an unadmitted reservation by restore-then-relaunch and Unity orphan recovery depends on that (`ApplicationStashServiceBootstrapper.cs:139`).
- L3 (interpretation): AC1 names "las tres mutaciones", so the `:71` exemption is reversed. AC2 keeps the backend restore-then-replace path (rejecting it would break Unity orphan recovery) and fixes its v1 loss. Replacing an admitted reservation stays "Lost" per Architecture L507 (a raid that started risks its items); it is not a revert and AC2 does not cover it. The defeat `$unset` is in scope per FIX-18 L14.

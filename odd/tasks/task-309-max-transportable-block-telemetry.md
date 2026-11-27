# TASK-309 — MaxTransportable block telemetry reported from Unity

HacknPlan: TASK-309 (work item 842, Sprint 19, no parent). This is an execution tracker only: scope and acceptance live in HacknPlan.
Sources: Doc 23 §8.2 and §12, Doc 22 §14, `Docs/Architecture/EconomyTelemetryArchitecture.md`, `CommerceArchitecture.md`, and the TASK-305 telemetry.

## Specs

- **S1 (scope, decision L2).** Per Doc 23 §8.2, a MaxTransportable block happens only when an expedition starts: "Antes de iniciar expedición se valida que el inventario de salida no exceda MaxTransportable; si excede, se bloquea el inicio hasta reorganizarlo." Town purchases, buybacks and transfers are not limited by MaxTransportable ("restringe lo llevado a Dungeon, no la posesión total en Pueblo"), so only the expedition-start block is recorded. No MaxTransportable rule or value changes.
- **S2 (AC).** "Un bloqueo por MaxTransportable genera exactamente un registro con su origen; una operación permitida no genera ninguno."
- **S3 (AC).** "Un reporte malformado o excesivo se rechaza sin afectar el juego ni otros datos."
- **S4 (AC).** "Los registros permiten calcular la tasa de bloqueo por MaxTransportable separada de la de capacidad."
- **S5 (scope).** "El registro es estrictamente analítico: No altera la operación ni el estado del jugador. Los datos reportados por el cliente se tratan como no autoritativos y se acotan en volumen. No expone datos personales ni credenciales."
- **S6 (authority, decision L2).** The client reports the block and the backend does not validate MaxTransportable. The report goes to an authenticated endpoint, is validated against a whitelist, and is bounded in body size and per-character frequency. Rejection is 4xx with nothing written.
- **S7 (scope).** "Actualizar el contrato en EconomyTelemetryArchitecture.md."
- **S8 (validation).** "Tests de backend RED → GREEN del registro y de los rechazos." "Tests EditMode de Unity del reporte ante un bloqueo y su ausencia ante operaciones permitidas." "npm test completo y la suite EditMode relevante en verde."
- **Out of scope.** "Cambiar las reglas o los valores de MaxTransportable." "Dashboards o paneles dentro del cliente." Raid loot pickup blocks (Dungeon).

## Tasks

- [x] **T1:** Backend `transport_block` event and bounded client-report endpoint, with tests. Covers S2-S6, S8. Route: inline TDD. Risk: item 2 (client input / guards), gets an independent verify. Commit: 6b6b9dd5.
- [x] **T2:** Unity reporter on the client's own admission payload before submission (decision L5), reporting only MaxTransportable blocks, with EditMode tests. Covers S1, S2, S5, S8. Route: explore the seam, then inline TDD. Commit: bb5b570c.
- [x] **T3:** Update `EconomyTelemetryArchitecture.md` (event, non-authoritative contract, aggregation example, closed known gap). Covers S4, S7. Route: inline. Commit: bb5b570c.
- [x] **T4:** Full `npm test` and relevant EditMode suite, diff review against S1-S8. Covers S8. Route: inline + verify for T1.

## Log

- **L1 (user request, verbatim):** "vamos a realizar la tarea con el titulo TASK-309 — Telemetría de bloqueos por MaxTransportable reportados desde Unity (Docs 22 y 23) del sprint 19, analizala y empeza con la planificacion de la implementacion"
- **L2 (decisions, 2026-11-27):** Scope = "Solo el bloqueo de inicio de expedición (Recomendado)". Authority = "Reporte del cliente, no autoritativo (Recomendado)".
- **L6 (T1 independent verify):**
  - gentle-ai-verify verdict: S2-S6 met; the backend part of S8 met. No blockers.
  - Adversarial probes (proto keys, operators, oversized body, tokens, XFF, query params, sink failure) wrote 0 events and left the character unchanged.
  - Advisories applied:
    - doc status codes corrected (`BAD_REQUEST`, 413, `TOKEN_EXPIRED`);
    - limiter documented as per-account and counting rejected requests;
    - 3 regression tests added (`__proto__`, 413, sink failure). Focused suite now 16/16.
  - Not applied: a malformed-`sub` 500, which predates this change and is out of scope.
- **L5 (T2 seam evidence + decision, 2026-11-27):**
  - The MaxTransportable check (`RaidAdmissionData.TryProjectLoadout`, `:269`) runs only on the Raid host (`NetworkSpawnManager.cs:3458`, `:3626`), possibly more than once per player.
  - The host lacks the refused player's token, and the refused client receives only a generic `BootstrapFailure`.
  - Possible gameplay bug, out of scope: the refusal surfaces as "La incursión no pudo iniciarse. Perdiste el equipamiento que llevabas:", whereas Doc 23 §8.2 says "se bloquea el inicio hasta reorganizarlo". Follow-up FIX, not verified.
  - User decision: "Solo detectar y reportar (Recomendado)". Before submitting its admission, the client runs the same MaxTransportable check on its own loadout and reports once when it is exceeded. No behavior change. Add a typed reason to `TryProjectLoadout`.
- **L4 (T1 evidence):**
  - Files: `backend/src/routes/telemetry.routes.js` and `backend/src/validators/telemetry.validators.js` (new); `EconomyTelemetry.recordTransportBlock`; `transport_block` added to the model; route mounted in `app.js`.
  - RED: `Cannot find module '../src/routes/telemetry.routes'`. GREEN: `tests/economy.telemetry.transport-block.test.js` passes 13/13.
  - Full `npm test`: 1004/1004 pass, exit 0. The independent verify is still pending (T4).
- **L3 (evidence):**
  - Town commerce and stash have no MaxTransportable check (`Stash/`, `Commerce/`); `MaxTransportableAdmissionAndStashTests.cs` asserts the stash is exempt.
  - The only Town block is `Networking/RaidAdmissionData.cs:269` ("Loadout exceeds item transport limits.").
  - The backend records only `capacity_block` / `limit:'capacity'` on `INVENTORY_FULL` (`backend/src/services/EconomyTelemetry.js:230-243`).
  - Unity has no existing telemetry client; HTTP goes through `Backend/AuthorizedRequest.cs` (Bearer).
- **L7 (T2 evidence, 2026-11-27):**
  - Files: `Networking/RaidAdmissionTransportBlock.cs` (pure classifier over `TryProjectLoadout`, true only for the exact `RaidAdmissionData.TransportLimitExceededError`), `Networking/RaidAdmissionTransportBlockReporter.cs` (injected `Func<Task<bool>>`, fire-and-forget, failures caught and logged), `Backend/TelemetryClient.cs` (`POST /character/me/telemetry/transport-block`, body exactly `{"source":"raid_admission"}`), `RaidAdmissionData.cs` (literal extracted to a const; `TryProjectLoadout` signature and check order unchanged), `FusionSessionLauncher.cs` (hook), `Tests/EditMode/Editor/Networking/RaidAdmissionTransportBlockDetectionTests.cs` (22 tests).
  - Hook: `FusionSessionLauncher.StartSessionInternalAsync`, right after a successful `_runner.StartGame` and before the Host bootstrap and the local admission wait. It uses the final `admissionData` handed to the runner (after the ticket branch). It sits there, not before the runner start, because `SessionConnectionCoordinator` re-runs the method while a Client waits for its Host session (`GameNotFound`/`GameClosed`), which would report once per retry. Aborted attempts (ticket abort, `_isStarting` guard, failed runner creation or `StartGame`) never report.
  - Config and token come from `ApplicationAuthContext.BackendConfig`/`Token` via `RaidLaunchBackend.RequiresConfirmation` (the launch path's own gate), not `LoginFlowController`. Without an authenticated session the reporter is disabled and does nothing.
  - RED (Unity Editor, `unity command test run`): `RaidAdmissionTransportBlockDetectionTests` 22 run, 12 passed, 10 failed (stub classifier/reporter/body: flag-above-limit x3, host-order, send-once, throwing/faulted/rejected send, pending send, body). The passing ones pin the host messages and the no-send cases.
  - GREEN: 22/22. `MaxTransportableAdmissionAndStashTests` 18/18 (filter `MaxTransportable`), `BackendConfirmedLaunch` 26/26, `BackendV3AdmissionShape` 2/2, `NetworkSpawnManager` 16/16. Filter `RaidAdmission`: 181 run, 179 passed, 2 failed (`PhysicalProfileSlotTests.RaidAdmissionCapacity_ExtractionThenReservation_KeepsEquipmentOutsideBackpack(1|30)`, a `StashItem` collection-equality assertion in Stash code this change does not touch; not checked against the base).
- **L8 (T4 close, 2026-11-27):**
  - Final `npm test`: 1007/1007, exit 0. Unity EditMode (worker run): `RaidAdmissionTransportBlockDetectionTests` 22/22, `MaxTransportable` 18/18, `BackendConfirmedLaunch` 26/26, `NetworkSpawnManager` 16/16.
  - `RaidAdmission` filter: 179/181. The 2 failures are in `PhysicalProfileSlotTests` (StashItem collection equality); they are not covered by this diff and were not checked against the base.
  - Not done: an end-to-end HTTP test from Unity and a multiplayer manual check.
  - Commits (authorized): 6b6b9dd5 (backend), bb5b570c (Unity + docs), this tracker commit.

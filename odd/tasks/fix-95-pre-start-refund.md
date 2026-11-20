# FIX-95 — Pre-start equipment refund

HacknPlan FIX-95 (ID 807), parent FIX-US-01 (ID 665). Branch `fix/fix-61-dungeon-entry-checks` (owner choice).
Refund an admitted reservation to Stash/Loadout when raid entry fails before the expedition effectively starts.

## Specs

- **S1** (FIX-95 AC1) "Con N definido y arquitectura aprobada, si la transición falla después de admitir la reserva y antes de entrar a Dungeon dentro de la ventana N, los items reservados regresan íntegros al Stash/Loadout sin duplicación."
- **S2** (FIX-95 AC2) "Pasada la ventana N o con resultado de raid ya liquidado, el endpoint rechaza el reembolso sin alterar el inventario."
- **S3** (FIX-95 AC3) "La respuesta del backend y su consumo en Unity manejan reintentos de forma idempotente ante fallos transitorios de red."
- **S4** (FIX-95 scope 2) "invocación del reembolso sólo ante fallo confirmado previo al inicio efectivo y preservación de reservas cuando corresponda recuperación normal."
- **S5** (owner decision L3) Effective start is a server-side marker reported by the Host: once the raid is marked started, refund is rejected.
- **S6** (owner decision L2) N is not defined by Game Design (01 §14 has no value): use an easily editable placeholder.
- **S7** (FIX-95 scope / arch prerequisite) `LocalPlayerPersistenceArchitecture.md` documents the new contract.
- **S8** (FIX-95 out of scope) "Reembolsos una vez iniciada la partida dentro de la Dungeon (derrota, abandono o desconexión en raid)" stay out: they remain losses.

### Contract (derived from S1-S8; architecture is the authority once T1 lands)

Backend, all under the authenticated `/character` router with `inventoryProtocol`:

- `POST /character/me/inventory/reservation/raid-started`, body `{ raidId }`.
  Caller's own `pendingReservation` must be admitted and bound to `raidId` (Bounded Player-Host Authority), else `403 HOST_NOT_IN_RAID`.
  Sets `pendingReservation.startedAt = now` on every character whose admitted `pendingReservation.raidId === raidId` and `startedAt` is null. Idempotent; returns `{ raidId, marked: <count> }`.
- `POST /character/me/inventory/reservation/refund`, body `{ reservationId }`.
  - No active reservation → `200` no-op `{ pendingReservation: null, revision }` (retry after a lost response).
  - Different active reservation → `409 RESERVATION_MISMATCH`.
  - `startedAt` set → `409 RAID_ALREADY_STARTED`.
  - Admitted and `now - admittedAt > RESERVATION_REFUND_WINDOW_MS` → `409 REFUND_WINDOW_EXPIRED`.
  - Unconsumed authoritative result for the character → `409 EXTRACTION_RESULT_PENDING`; an applied extraction receipt for the reservation's `raidId` → `409 RESULT_ALREADY_CONSUMED`.
  - Otherwise restores reserved items exactly as pre-admission rollback does, clears the reservation, `$inc revision`, atomically filtered by revision, `reservationId` and `startedAt: null`. Rejections never write.
- `env.reservationRefundWindowMs = parseInt(process.env.RESERVATION_REFUND_WINDOW_MS || '120000', 10)` — placeholder (S6).

Unity:
- The Host (State Authority), on the raid reaching `InProgress`, calls `raid-started` with the raid id, retried idempotently.
- After admission, a failure confirmed before the local raid reached `InProgress` calls refund (retried idempotently) instead of the loss clear; on success the local store restores the reservation to Loadout/Equipment. Terminal rejection falls back to the existing loss-clear path. In-raid endings keep the loss path (S8).

## Tasks

| ID | Specs | Route | Commit |
| --- | --- | --- | --- |
| T1 | S5-S8 | inline: architecture contract | — |
| T2 | S1-S3, S5, S6 | writer: backend endpoints + tests | — |
| T3 | S1, S3, S4, S5, S8 | writer: Unity client + coordinator + EditMode tests | — |
| T4 | S1-S8 | gentle-ai-verify (Risk items 1, 3, 4) | — |
| T5 | S1, S4 | manual multi-process validation (owner) | — |

## Log

- **L1** (user): "vamos a implementar FIX-95 — Reembolso de equipamiento cuando la raid falla antes de empezar siguiendo ODD"
- **L2** (user): "si no esta definido el valor de N en un documento de game design usa un valor de placeholder facilmente editable, un valor acorde a lo que se esta implementando."
- **L3** (user decisions): start signal "(a) Marcador server-side del Host (Recomendado)"; branch "Rama actual fix-61"; commits "Sí, commits por unidad".
- **L4** (evidence): Game Design 01 §14 read live: "Una cancelación previa a Dungeon no genera Loot, XP, Derrota definitiva ni Abandono voluntario, porque el riesgo de expedición todavía no había comenzado." No N value. Placeholder 120 s chosen to cover Conectando plus recovery to Town.
- **L5** (evidence): `raidId` bound to the reservation by `backend/src/services/SessionTicketService.js:244-247`; host-membership pattern in `RaidResultService.js:263-283`.
- **L6** (limitation): in Solo the Host is the player, so the start marker stays self-attested there; N bounds it. Removing it needs the dedicated server (ID 400, out of scope).

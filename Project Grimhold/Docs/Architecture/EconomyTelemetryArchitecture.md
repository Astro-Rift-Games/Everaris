# Economy Telemetry Architecture

Status: current (TASK-305, updated in TASK-310 and TASK-309). Owners: backend. Game Design sources: Doc 12 §14, Doc 22 §§5, 6, 10, 14 and Doc 23 §§8.2, 12.

Internal, analytics-only record of confirmed economic operations for playtest balance. It is never read by gameplay, pricing, rewards or the Unity client: no dynamic prices, taxes, subsidies or automatic bankruptcy advantages are derived from it (Doc 22 §14).

## Storage and emission contract

- Collection `economytelemetryevents` (model `backend/src/models/EconomyTelemetryEvent.js`), written by `backend/src/services/EconomyTelemetry.js`.
- **Post-commit only.** Each emitter is called after its revision-guarded `findOneAndUpdate` returned a document. Rejected operations, idempotent replays (`alreadySecured`) and concurrent-retry losers never emit, so a rejected operation never records gold as generated or spent.
- **Never part of the commit.** Recording is fire-and-forget: it adds no awaited write to the request and a sink failure is logged as `[EconomyTelemetry] dropped <operation> event: <reason>` without failing or rolling back the operation. Delivery is at-most-once (a crash between commit and insert loses the event). `EconomyTelemetry.flush()` waits for in-flight inserts (tests, graceful shutdown).
- **Privacy.** Events carry only the pseudonymous `characterId` (Character `_id`). Account ids, usernames, emails, tokens and client-declared values are never stored; the schema is `strict: 'throw'` and `buildEvent` whitelists top-level fields. The only client input ever stored is the whitelisted `source` of a `transport_block` report (below).

## Client-reported transport blocks (TASK-309)

MaxTransportable is enforced only by Unity, and Doc 23 §8.2 limits only what is carried into the Dungeon: "restringe lo llevado a Dungeon, no la posesión total en Pueblo". So Town purchases, buybacks and transfers are never MaxTransportable blocks. The one Town block is the refused expedition start (`RaidAdmissionData`, "Loadout exceeds item transport limits."). The backend does not validate MaxTransportable. The Unity client reports each refusal.

- **Endpoint.** `POST /character/me/telemetry/transport-block` (`backend/src/routes/telemetry.routes.js`). It needs a Bearer token and accepts exactly `{ "source": "raid_admission" }`. A successful report returns `202 { accepted: true }` and records one `transport_block` via `EconomyTelemetry.recordTransportBlock`.
- **Non-authoritative.** A report is analytics only. It never reads the character beyond resolving its `_id` and never writes it. Nothing reads it back. `details.reported: 'client'` marks it as client-declared.
- **Rejection without effects.** No event is written and the character is unchanged in all of these cases:
  - unknown fields (including `__proto__` / `constructor`), a client-declared `limit`, an unknown or non-string `source`, or an array body: `400 VALIDATION_FAILED`;
  - malformed JSON or a JSON primitive: `400 BAD_REQUEST`, from the body parser;
  - a body over the app-wide `express.json()` limit (100 KB): `413`;
  - a missing, invalid or revoked token: `401 UNAUTHORIZED`;
  - an expired token: `401 TOKEN_EXPIRED`;
  - an account without a character: `404 CHARACTER_NOT_FOUND`.
- **Volume bound.** At most `TRANSPORT_BLOCK_REPORT_MAX` (20) authenticated requests per account per 10 minutes, rejected ones included (in-memory `MemoryRateLimiter` keyed by account; one character per account, so this is also per character). The limiter runs before validation, so an account sending malformed reports throttles only itself. Neither `X-Forwarded-For` nor a new token bypasses it. Beyond it the endpoint returns `429 TOO_MANY_REQUESTS` with `Retry-After`. The window is per process and resets on restart.
- **Unity reporter.** The Raid host enforces MaxTransportable in `RaidAdmissionData.TryProjectLoadout`, but the refused client never learns the reason. So the client classifies its own final admission:
  - `RaidAdmissionTransportBlock.IsTransportLimitBlock` runs the host's `TryProjectLoadout` and matches exactly `RaidAdmissionData.TransportLimitExceededError`. The rule is not duplicated, and capacity or other refusals are not counted.
  - `RaidAdmissionTransportBlockReporter` sends through `Backend/TelemetryClient.cs`. It is hooked in `FusionSessionLauncher.StartSessionInternalAsync` right after a successful `StartGame`, so it runs once per connected attempt and never on the Client's wait-for-Host retries or on aborted launches.
  - Without an authenticated backend session it does nothing.
- **Delivery.** Delivery is at-most-once. The client sends a report fire-and-forget and never retries. A lost report never affects the expedition start.

## Event shape

| Field | Meaning |
| --- | --- |
| `characterId` | Character `_id` as string |
| `operation` | `sell`, `buy`, `buyback`, `trade`, `upgrade`, `respec`, `mission_reward`, `raid_close`, `capacity_block`, `transport_block` |
| `gold[]` | `{ direction: 'generated' \| 'spent', category, offerType?, amount > 0 }`; zero lines are omitted |
| `goldGenerated`, `goldSpent`, `netGold` | Totals of `gold[]` for this operation |
| `balanceAfter` | Persistent gold after the commit ("saldo acumulado") |
| `details` | Operation-specific, below |
| `occurredAt` | Server time |

Gold categories (the schema rejects a category in the wrong direction):

| Direction | Category | Emitted by |
| --- | --- | --- |
| generated | `npc_sale` | `shopSell`, `shopTrade` |
| generated | `mission_reward` | `MissionClaimService.claim` (`POST /character/me/missions/claim`, TASK-310) |
| spent | `purchase` (`offerType`: `consumable`, `utility`, `weapon`, `armor`, `other`) | `shopBuy`, `shopTrade` |
| spent | `buyback` | `shopBuyback`, `shopTrade` |
| spent | `weapon_upgrade` (`primary_scaling`, `first_socket`) / `armor_upgrade` (`armor_level`) | `InventoryService.upgradeItem` |
| spent | `respec` | `ProgressionService.respec` |

`details` per operation:

- Commerce (`sell`, `buy`, `buyback`, `trade`): `sales[] { lootId, amount, revenue, demandBonus }`, `purchases[] { lootId, amount, cost, offerType }`, `buybacks[] { lootId, amount, cost }`, `demandBonusUnits`, `demandBonusGold` (extra gold paid by the weekly +20% demand), `merchant` when the cart named one.
- `upgrade`: `merchant`, `upgradeOperation`, `lootId`, `levelAfter` (primary grade, socket count or armor level), `materials { lootId: amount }`, `objectValueAfter`.
- `respec`: `pointsReturned`.
- `mission_reward`: `missionId`, `claimKey`.
- `raid_close`: `outcome` (`extraction`, `defeat`, `abandon`), `raidId`, `entryProfile`, `carriedEquipmentReplacementGold`, `carriedItemsValue`, `extractedItemsValue` (extraction), `lostValue` and `lostItems[] { lootId, amount }` (defeat/abandon), `experienceGranted`. No gold lines: extraction never grants gold (Doc 22 §2).
- `capacity_block`: `source` (`purchase`, `buyback`, `trade`, `extraction`), `limit: 'capacity'`. Recorded when a new operation is refused with `INVENTORY_FULL`; the original error is rethrown unchanged.
- `transport_block`: `source` (`raid_admission`), `limit: 'max_transportable'`, `reported: 'client'`. A client-reported refused expedition start (above). It is kept as its own operation so the MaxTransportable block rate never mixes with `capacity_block`.

### Raid close rules

- The outcome is the committing service: `ExtractionCommitService.commit` → `extraction`; `DefeatCommitService.commit` → `defeat` only when that commit cleared the reservation; a non-rollback `InventoryService.clearPendingReservation` that held items → `abandon`. A pre-admission rollback/refund is not an expedition and emits nothing.
- Values: equipment by ObjectValue (`ItemUpgrades.objectValue`), other items by NPC sell value, training weapons 0.
- **Entry profile is approximate** (Doc 22 §5): from the buy-price replacement gold of non-training prepared equipment carried in — `0` → `naked`, below `ENTRY_PROFILE_GEARED_MIN_GOLD` (800) → `budget`, otherwise `geared`. The threshold is a provisional analytics bucket taken from the Doc 22 §10 simulation assumptions, not a balance value; raw values are stored so analysis can reclassify.

## Aggregation examples (mongosh)

```js
// Gold generated / spent by category
db.economytelemetryevents.aggregate([
  { $unwind: '$gold' },
  { $group: { _id: { direction: '$gold.direction', category: '$gold.category', offerType: '$gold.offerType' }, gold: { $sum: '$gold.amount' }, operations: { $sum: 1 } } }
]);

// Extraction rate and value lost by entry profile
db.economytelemetryevents.aggregate([
  { $match: { operation: 'raid_close' } },
  { $group: { _id: '$details.entryProfile', expeditions: { $sum: 1 },
      extractions: { $sum: { $cond: [{ $eq: ['$details.outcome', 'extraction'] }, 1, 0] } },
      lostValue: { $sum: '$details.lostValue' } } },
  { $addFields: { extractionRate: { $divide: ['$extractions', '$expeditions'] } } }
]);

// Block counts by limit type: capacity vs MaxTransportable (Doc 23 §12)
db.economytelemetryevents.aggregate([
  { $match: { operation: { $in: ['capacity_block', 'transport_block'] } } },
  { $group: { _id: { limit: '$details.limit', source: '$details.source' }, blocks: { $sum: 1 } } }
]);
```

## Known gaps (out of this contract)

- MaxTransportable blocks are client-reported (`transport_block`), not validated by the backend. They cover only the expedition start, which is the only place Doc 23 §8.2 applies the limit in Town. Raid loot pickups refused by MaxTransportable inside the Dungeon are not recorded.
- Gem installation (`installGem`) is not recorded; it moves no gold.
- Single sales (`shopSell`) do not apply weekly demand (only `shopTrade` does); telemetry reports what the commit applied.

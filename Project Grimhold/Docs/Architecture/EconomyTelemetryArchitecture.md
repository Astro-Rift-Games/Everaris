# Economy Telemetry Architecture

Status: current (TASK-305, updated in TASK-310). Owners: backend. Game Design sources: Doc 12 §14, Doc 22 §§5, 6, 10, 14 and Doc 23 §12.

Internal, analytics-only record of confirmed economic operations for playtest balance. It is never read by gameplay, pricing, rewards or the Unity client: no dynamic prices, taxes, subsidies or automatic bankruptcy advantages are derived from it (Doc 22 §14).

## Storage and emission contract

- Collection `economytelemetryevents` (model `backend/src/models/EconomyTelemetryEvent.js`), written by `backend/src/services/EconomyTelemetry.js`.
- **Post-commit only.** Each emitter is called after its revision-guarded `findOneAndUpdate` returned a document. Rejected operations, idempotent replays (`alreadySecured`) and concurrent-retry losers never emit, so a rejected operation never records gold as generated or spent.
- **Never part of the commit.** Recording is fire-and-forget: it adds no awaited write to the request and a sink failure is logged as `[EconomyTelemetry] dropped <operation> event: <reason>` without failing or rolling back the operation. Delivery is at-most-once (a crash between commit and insert loses the event). `EconomyTelemetry.flush()` waits for in-flight inserts (tests, graceful shutdown).
- **Privacy.** Events carry only the pseudonymous `characterId` (Character `_id`). Account ids, usernames, emails, tokens and client-declared values are never stored; the schema is `strict: 'throw'` and `buildEvent` whitelists top-level fields.

## Event shape

| Field | Meaning |
| --- | --- |
| `characterId` | Character `_id` as string |
| `operation` | `sell`, `buy`, `buyback`, `trade`, `upgrade`, `respec`, `mission_reward`, `raid_close`, `capacity_block` |
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
```

## Known gaps (out of this contract)

- MaxTransportable blocks are enforced in Unity (`CommerceArchitecture.md`); the backend records only `INVENTORY_FULL` capacity blocks.
- Gem installation (`installGem`) is not recorded; it moves no gold.
- Single sales (`shopSell`) do not apply weekly demand (only `shopTrade` does); telemetry reports what the commit applied.

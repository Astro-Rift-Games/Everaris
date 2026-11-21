# Commerce Architecture

## Scope

This document owns the technical contract for Town Merchant commerce across Unity and the Node.js backend:
pending trade state, presentation, merchant specialization, authoritative individual stock, weekly rotation,
shared buyback (FIFO 16), request identity and the persistence boundary. `LocalPlayerPersistenceArchitecture.md`
owns the character aggregate and its transactions; this document defines what Commerce submits to it.

Intended gameplay behavior is authored authoritatively in:
- **Doc 23 — Sistema de Comercio y Mercaderes del Pueblo** (`1Bn10tvbGTYuD2lCcDK_8No9nvRrdSOMTCyQ3c_J-3qY`).
- **Doc 22 — Macroeconomía de Extracción** (`1xlZtvaIDDOvXez2MgqmFpjmSyExGTvBWwGjztEpBoLw`).

### Three Specialized Town Merchants (Doc 23 §3)

Town commerce is distributed across three specialized merchants and one combat trainer:
1. **Mercader General:**
   - **Vende:** Oferta permanente ilimitada (3 consumibles básicos) + 2 utilidades rotativas semanales con cupo personal.
   - **Compra:** Todos los objetos comerciables (consumibles especiales comerciables, tesoros, recursos, etc.).
   - **Servicios:** Comercio regular, demanda semanal bonificada (+20%) y acceso a la recompra compartida.
2. **Herrero:**
   - **Vende:** Armas físicas rotativas (del pool de 7 armas físicas), armaduras rotativas (del pool de 24 piezas) y arma especial física semanal (con 1 modificación inicial).
   - **Compra:** Armas físicas y todas las armaduras.
   - **Servicios:** Comercio y acceso a la recompra compartida (servicios de mejora de armas físicas y de todas las armaduras en US-84; contrato backend en `ItemInstanceArchitecture.md` §5.1, TASK-302; pestaña «Mejoras» en §Mejoras tab, TASK-303).
3. **Arcanista:**
   - **Vende:** Armas mágicas rotativas (del pool de 5 armas mágicas) y arma especial mágica semanal (con 1 modificación inicial).
   - **Compra:** Armas mágicas.
   - **Servicios:** Comercio y acceso a la recompra compartida (mejora de armas mágicas en US-84; contrato backend en `ItemInstanceArchitecture.md` §5.1, TASK-302; pestaña «Mejoras» en §Mejoras tab, TASK-303).
4. **Entrenador:**
   - No compra ni vende objetos. Provee armas de entrenamiento gratuitas (`Tradable = false`, `ObjectValue = 0`).

### Commercial Pricing Formulas (Doc 23 §7 & §10)

Prices are calculated per single unit before multiplying by stack quantity, with a minimum price of 1 Gold for eligible tradable items:
- **Compra de NPC:** `ceil(ObjectValue * 2.0)`
- **Venta regular a NPC:** `floor(ObjectValue * 1.0)`
- **Venta con Demanda semanal bonificada (+20%):** `floor(ObjectValue * 1.2)`
- **Recompra compartida (FIFO 16):** `ceil(1.5 * Oro efectivamente recibido por el jugador)`

Training items (`Tradable = false`, `ObjectValue = 0`) are completely excluded from buy, sell, and buyback.
Consumables especiales (Bomba explosiva, Bomba de humo, Daga arrojadiza, Auto-reanimador), tesoros y recursos no forman parte de la venta regular de NPC en el MVP (solo se pueden vender al NPC o recomprar si fueron vendidos).

### Backend Authoritative Contract (US-83 / Docs 22 y 23)

The backend is the sole authority for commerce catalog generation, individual stock, weekly rotation, and economic mutations:
- **Global Weekly Rotation (Doc 23 §5):** Rotates every Monday at 00:00 UTC based on server UTC time using a deterministic seed per Unix week.
  - Generates: 3 distinct normal weapons (from pool of 12), 1 special weapon (from remaining 9) with exactly 1 initial modifier (Scaling E 30% / +20 OV, Scaling D 25% / +35 OV, Scaling C 15% / +50 OV, or 1 empty socket 30% / +60 OV), 3 distinct armor pieces (from pool of 24), 2 distinct utilities (from the 3 available: Lockpick stock 3, Rune Compass stock 1, Arcane Detector stock 1), and 3 distinct eligible resources/treasures with weekly demand bonus (+20%).
- **Individual Player Stock (Doc 23 §5.2 & §8.2):** Stock is tracked and consumed individually per player per weekly cycle in the backend (resetting automatically at Monday 00:00 UTC), not shared in a competitive pool across players in the Town instance.
- **Atomic Mixed Cart Transaction:** `POST /character/me/inventory/shop/trade` (and `/api/commerce/transaction`) accepts a unified cart containing purchases, sales (with demand awareness) and buybacks. Validates final Gold >= 0, final slot capacity (accounting for sold and bought items), current player stock, and catalog freshness (rejecting with `ROTATION_EXPIRED` if the weekly cycle rotated while the cart was open, requiring client re-confirmation).
- **Shared Buyback (FIFO 16, Doc 23 §8.3):** A single 16-entry FIFO list per player shared between General Merchant, Blacksmith, and Arcanist. Retains exact instance attributes (InstanceID, scalings, sockets, gems, armor `upgradeLevel`) and special consumable stacks. Cleared on raid start (`TownRaidQueue`) or logout.
- **MaxTransportable = 1 Enforcement (Doc 23 §8.2):** The player is allowed to store multiple units of restricted utilities (Rune Compass, Arcane Detector, Self-Revive) in their Town Stash. The raid admission system (`TownRaidQueue` / `RaidAdmissionValidator`) strictly blocks entry if the outbound inventory exceeds `MaxTransportable = 1`.
- **Supersedes FIX-24 (#694):** The legacy universal BUY_CATALOG and Fusion-replicated stock reservation are replaced by authoritative backend individual weekly stock and weekly rotation.

## Decision

A trade is one pending, local draft of buy and sell lines that is confirmed as a single atomic
operation. Only State Authority decides shared stock; only the confirmed profile holds persistent
Inventory and Currency.

```text
Merchant interaction
  -> TownMerchantPresenter (lifecycle, binding, input)
  -> MerchantTradeDraft (local pending lines, no side effects)
  -> local preview (confirmed profile + draft, pure rules)
  -> Confirm intention
  -> Fusion State Authority (stock, concurrency, request identity, dedup, stock reservation)
  -> authority-issued trade ticket
  -> IShopTransactionService (persistence boundary, one atomic profile transaction)
  -> ProfileCommitted (confirmed state)
  -> outcome reported to State Authority (commit or release reservation)
  -> UI refresh from confirmed profile and replicated stock
```

## Ownership

### MerchantTradeDraft

- Plain C# local state owned by the Input Authority client for one open merchant session.
- Holds ordered buy lines and sell lines (`LootId + Amount`), or one entry-keyed buy-back line
  with its frozen ledger price and amount. Mixed normal/buy-back drafts cannot confirm.
  It holds the client request identity once
  Confirm is pressed, and an in-flight flag.
- Never mutates the profile, Currency, Inventory, merchant stock or any `[Networked]` value.
  Adding, removing or changing a line has no side effect outside the draft.
- Cancel or close discards the draft; the confirmed state is unchanged by construction.
- After a successful confirmation the draft is cleared. After a rejection it is kept so the
  player can edit and retry, and a new request identity is issued for the edited content.
- It is not replicated and not persisted.

### Local preview

- A pure, deterministic projection: confirmed Inventory/Currency + draft lines + replicated stock
  view -> resulting Inventory, Currency balance, buy total, sell total and blocking reasons
  (insufficient Gold, capacity exceeded, stock unavailable, missing owned units).
- Capacity is evaluated on the final state, counting sold and bought units together, with the same
  capacity/merge rules the aggregate transaction uses. The preview reuses those pure rules; it does
  not reimplement them.
- Unit prices come from the authoritative static economy configuration available to the client for
  preview. The preview may display them but they are never trusted as authoritative transaction
  input.
- That configuration is currently owned by `LootDefinition`: `BuyValuePerUnit` is the buy price and
  `SellValuePerUnit` the template sell value, resolved through the merchant's `LootDefinitionCatalog`.
  Equipment instance sell price is the template sell value plus the sell value of each installed
  gem (*provisional*); backend pricing stays authoritative
  ([Item Instances §4.3](ItemInstanceArchitecture.md#43-operations)).
  `ExtractionValuePerUnit` is extraction progress only and is never a Commerce price or fallback.
  Every merchant stock item must reference a valid definition with a positive buy value.
- The preview is advisory. The persistent transaction revalidates everything.

Current representation: `MerchantTradePreview.Calculate` projects confirmed Inventory entries, slot
capacity, confirmed Currency, the draft and an available-stock lookup into an immutable result with
buy/sell totals, balance, projected Currency, projected Inventory and typed
`MerchantTradeBlock`s; `CanConfirm` means no block. It also reports the confirmed and projected
occupied slots next to the slot capacity. Sale lines are checked against confirmed owned
units and purchase lines against current available stock; units sold in the same trade are not
purchasable in it, because sold units enter stock only on a persisted outcome. Stack merge, removal
and slot capacity come from `ProfileInventoryRules`, which `LocalProfileStore` also uses.

### MerchantShopUI

- Presentation and intentions only: renders stock, confirmed Inventory, draft lines and preview
  results; raises intentions (add/remove line, change quantity, confirm, cancel, close).
- Does not hold `TownMerchantNetworkController`, `ApplicationStashContext` or persistence
  services, does not compute prices or totals, and does not decide whether Confirm is allowed.
  It receives an already computed view model from the presenter.
- Disables Confirm while the preview reports a blocking reason or a request is in flight.

Current representation: `MerchantShopUI` renders a `MerchantShopViewModel` (confirmed and projected
Gold, Currency shortfall, preview totals and balance, occupied and projected slots against capacity,
blocks, `CanConfirm`/`CanClear`/`CanEdit`, priced merchant and Inventory rows with their draft
amounts) and raises `IMerchantShopIntentions` (add/set/remove buy or sell line, clear, confirm)
through one flow: select a row (stock for purchase, Inventory for sale), choose a quantity, add the
line or update the one already in the draft, review, then Confirm or Clear. A separate action
removes the selected line; Close keeps discarding the session. The selection, the chosen quantity
and which action applies live in the pure `MerchantShopInteraction`, which reads limits and
permissions from the view model and sends Confirm only when `CanConfirm` holds, once per view model.
While a request is in flight every edit and Confirm are disabled. `MerchantTradeFeedback` maps the
main `MerchantTradeBlockReason` and each `MerchantTransactionResult` to presentation messages;
those strings are never domain rules. Item details reuse `EquipmentTooltipPresentationBuilder`.
The prefab lays out a header (title and Close only), three columns (Inventory grid on the left, the
selected-item panel in the center, merchant stock grid on the right) and an independent footer. Both
grids use five fixed columns aligned from the top inside a vertical `ScrollRect` that only scrolls
when the content exceeds it, so slots keep their size. The Inventory grid always renders
`SlotCapacity` slots (or more when rows exceed it): empty slots show only their background and raise
no selection or intention; the merchant grid shows authored offering rows followed by eligible
account-local buy-back rows. Equipment buy-back rows are hidden under the rule below. The center panel separates
identity (icon, name, category, rarity), a scrollable block with properties/requirements and
description, and the line editor (unit price, quantity, line total, line state, Add/Update, Remove)
that stays in place. The line total is `MerchantShopInteraction.LineTotal`, read from the row view
model. The footer has a summary row (Gold and occupied slots as current -> projected, purchase and
sale totals, balance with its sign and a "a favor"/"a pagar"/"sin cambio" label besides its color)
above a row with feedback on the left and Clear and Confirm on the right. `StoreItemUI` is one
compact grid slot: icon, confirmed amount (owned units, or finite unreserved stock) bottom-right,
unit price top-left on merchant slots, a separate "+N"/"-N" badge top-right, colored by side, while
the draft holds a line for that item, and selection; name, description and properties appear only
in the center panel. It raises only its selection; slots have no immediate buy or sell action. It is
not `RaidInventorySlotView`, whose drag, context and tooltip responsibilities do not apply here.
None of them holds services, the network controller or economy rules.

### Mejoras tab (TASK-303)

The Blacksmith and the Arcanist show a "Comerciar | Mejoras" tab bar inside `MerchantShopUI` (a direct
child of the root, never in the header or footer; hidden for the General Merchant). It is internal to the
merchant panel, not a `TownMenuTabRegistration`.

- `MerchantUpgradePanel` only renders `MerchantUpgradeViewModel` and forwards intentions
  (`IMerchantUpgradeIntentions`: select target, select operation, confirm, cancel); it holds no services.
- `TownUpgradePresentation` (presenter-owned) wraps `TownUpgradeSession`: targets are equipped pieces and
  Inventory instances eligible for that merchant (never the Stash); blocked operations stay visible and
  selectable with their reason (Doc 23 §9.4); only Confirm is gated. Preview lines show current →
  projected state, gold and Inventory materials with an "en Stash, no se usan" hint (Doc 23 §11).
- `TownMerchantPresenter` creates the session and presentation for Blacksmith/Arcanist when
  `TownMerchantProfileSource.SupportsUpgrades` (store, transfer coordinator, loot catalog supplied by
  `TownInventoryBinder`) and disposes them on close. Upgrades respect the Ready lock (`CanMutateBuild`)
  and wait for a trade in flight; trades wait for an upgrade in flight.
- The shop font has no arrow glyph: upgrade copy uses `->`.

### TownMerchantPresenter

- Owns lifecycle, binding and input for the local player's merchant session: resolves the target
  merchant from the interaction, creates/binds the view, owns the `MerchantTradeDraft`, builds
  the preview view model, acquires/releases input suppression and closes on invalid runner/NPC.
- Receives its dependencies (confirmed-profile read sources, shop transaction boundary) through
  explicit composition, not scene-wide searches.
- Forwards normal trade Confirm to the merchant network endpoint; buy-back Confirm goes directly
  to `IBuybackService`. Routes the awaited outcome back to the view.
- Never mutates the profile or stock directly.

Current representation: each open merchant owns one pure `TownMerchantTradeSession` (draft, preview
recomputed on edit, on `ProfileCommitted` through the Inventory read source and on replicated stock
changes, Confirm only when `CanConfirm`, ticket persistence, outcome report, view model). Closing
disposes it and discards the draft. `TownInventoryBinder`, the existing Town composition of the
confirmed local profile, supplies a `TownMerchantProfileSource` (`ProfileId`, the shared
`LocalLoadoutInventoryReadSource`, Currency service, `IShopTransactionService`) through a
serialized reference on `SocialPlayer`. A submission unanswered for 35 s, or a merchant authority
change, only unlocks the draft with the same request id; it never releases stock. The session asks
State Authority for its profile's pending trades when it opens, when the merchant authority
changes and when an edit discards a request it already sent, and reports each one it no longer
uses from the confirmed profile: persisted if the profile holds its receipt
(`IShopTransactionService.IsTradeApplied`), otherwise not persisted. Within one live session,
its current request is excluded from this recovery report. **Remaining cross-session gap:** a
new session may query an old pending trade while the disposed session is still awaiting backend
acknowledgement; the local receipt is not yet available. Recovery needs pending-operation awareness
or an authoritative outcome query before treating receipt absence as final.

### Fusion State Authority (merchant NetworkObject)

The Town runner is Shared mode; State Authority of the merchant object is the single decision
point for commerce requests.

- **Shared stock**: owns the merchant stock as replicated `[Networked]` state so every client
  observes the same quantities and the state survives a Master Client change. Serialized prefab
  data is only the initial configuration.
- **Concurrency**: requests are evaluated in arrival order. A buy line is accepted only against
  available, unreserved stock. Two players competing for the last unit get one acceptance and one
  rejection.
- **Stock reservation**: accepting a request reserves its buy quantities. The reservation becomes
  a stock change only when the requester reports persistence success, and is released only when
  the requester reports that the ticket is not persisted, a report that excludes a later success.
  Time, requester departure and authority changes never release it, because a persisted outcome
  may still arrive: a persisted trade can never leave stock as if it had not happened. Sold units
  enter stock only on success.
- **Request identity and deduplication**: the key is `ProfileId + client request id`, never
  `PlayerRef` or a per-open sequence. A duplicate of an accepted request returns the same ticket;
  a different payload under the same key is rejected. Authority mints exactly one
  `ShopTransactionId` per accepted request.
- **Ticket**: the accepted response carries the transaction id and the priced lines resolved by
  authority from the authoritative static economy configuration. Clients do not declare prices.
- Only State Authority runs validation. Request RPCs target State Authority; responses target only
  the requesting player.
- Dedup/reservation bookkeeping is bounded. Reservations end only through the requester's outcome,
  never through departure or elapsed time.

Current representation on `TownMerchantNetworkController` (Fusion 2.1.1):

- Protocol: `Rpc_RequestTrade(profileId, requestId, purchases[], sales[])` to State Authority,
  with lines as catalog index + amount and no client prices; `Rpc_TradeResponse` to the requester
  only, with approval, `ShopTransactionId` and the authority-priced lines;
  `Rpc_ReportTradeOutcome(requestId, transactionId, persisted)` to State Authority. The requester's
  `ProfileId` is resolved from its replicated `SocialPlayerIdentity` and must match the declared
  one; `PlayerRef` is only the reply target. `Rpc_RequestPendingTrades(profileId)` asks for the
  sender profile's pending trades, answered to the requester with one `Rpc_PendingTrade(requestId,
  transactionId)` each.
- `StockQuantities`: `NetworkArray<int>` with capacity `MaxStockSlots`, index-aligned with the
  serialized `MerchantStockItem` offering, seeded once by State Authority (`IsStockInitialized`).
  `MerchantStockItem.UnlimitedQuantity` marks unlimited stock; an item outside the offering is
  not purchasable.
- `TradeRecords`: `NetworkArray<MerchantTradeRecord>` with capacity `MaxTradeRecords`, keyed by a
  deterministic 64-bit hash of the `ProfileId` plus the request id. Each record keeps its status
  (`Pending`, `Committed`, `Released`), a hash of the requested lines and the transaction id.
  Priced lines are not stored: a retry with identical lines is answered by pricing
  them again from static configuration under the stored transaction id. The same key with other
  lines is rejected. `Pending` and `Committed` retries get the same ticket without another
  reservation; a `Released` retry reserves again under the same transaction id. Completed records
  are evicted oldest first; pending ones never are, and a table full of pending trades rejects new
  requests.
- `TradeStockLines`: `NetworkArray<MerchantTradeStockLine>` with capacity `MaxTradeStockLines`:
  the per-slot effects of pending trades on finite offered stock. Purchase lines are the
  reservations (available stock is the quantity minus them); sale lines add units on a persisted
  outcome. A trade needing more free lines than remain is rejected whole.
- A persisted outcome for a pending trade consumes its reservations and restocks sold offered
  units; a not-persisted outcome only releases them. Outcomes for trades that are not pending are
  ignored, so a released ticket is never persisted without reserving again. An approved response
  that no local session handles is reported as not persisted.
- `MerchantRequestValidator` holds these rules as pure C# over `IMerchantAuthorityState`, which
  the controller backs with the replicated arrays and refuses to mutate without State Authority.

### IShopTransactionService

- The persistence boundary for normal Commerce trades (buy-back uses `IBuybackService`). It receives the complete ticket for one profile and
  applies all its lines as one atomic `LocalProfileStore` transaction: funds, final capacity and
  owned-quantity validation, receipt idempotency keyed by the authority `ShopTransactionId`.
- Returns a task containing the final persistence outcome (`Success`, `AlreadyApplied`, rejection
  reason, persistence failure). The requester reports persistence only after awaiting that outcome.
  `AlreadyApplied` is a success for reservation purposes and publishes nothing twice.
- Backend submission, retries, revision conflicts and reconciliation are implementation details
  behind this boundary and are owned by the backend contract, not by Commerce.
- Its behavior never depends on scene names or UI state.

Current representation: `ExecuteTradeAsync(ProfileId, MerchantTradeTicket)`. The remote service
first uses `LocalProfileStore.EvaluateTrade` to plan without mutation or publication. It awaits the
atomic backend trade endpoint with every ticket line, the ticket transaction id and current remote
revision, then commits locally and saves the acknowledged revision. Backend success (including an existing
receipt / `alreadySecured`) is final success. If the local commit unexpectedly fails after backend
success, it logs an error and reconciles, but still reports success: the durable backend trade must
consume/restock merchant stock even when the local cache cannot be saved. There are no automatic
retries. Missing `BackendConfiguration` logs an error and makes the remote service unavailable.

The session keeps Confirm and editing disabled through the await, not merely until the authority
ticket arrives. Its response timeout and authority-change handling cannot unlock a backend call
in progress. Duplicate responses do not submit the ticket twice. It reports the final outcome even
if closed while awaiting, without updating disposed presentation; exceptions count as failure.

Backend error mapping: `RESERVATION_PENDING` -> `ReservationPending`;
`INSUFFICIENT_FUNDS`, `INSUFFICIENT_ITEMS`, `ITEM_NOT_SELLABLE`, `ITEM_NOT_OFFERED`,
`REVISION_CONFLICT`, `VALIDATION_FAILED` -> `InvalidInventory`; unauthorized, transport and
unknown failures -> `PersistenceFailed`. Rejections/unknown outcomes request reconciliation;
`UNAUTHORIZED` instead invokes the existing unauthorized-session handler. Local dry-run rejection
or receipt replay makes no backend call. Presentation currently groups `ReservationPending` with
other profile rejections because `MerchantTransactionResult` has no reservation-specific value.

The ticket is immutable:
`ShopTransactionId` plus read-only `Purchases` and `Sales` of `MerchantPricedTradeLine`
(`LootId`, `Amount`, authority `UnitPrice`). `LocalProfileStore.TryCommitTrade` rejects malformed
tickets (empty, invalid id/loot, non-positive amount or buy price, negative sell price, repeated
loot on one side), returns `AlreadyApplied` for a known receipt before the reservation gate and
trade-content validation, then applies the shared Town mutation gate. New trades are rejected
while a Raid loadout reservation is pending (`ReservationPending` in Unity, backend HTTP
`409 RESERVATION_PENDING`); receipt replays remain `AlreadyApplied`. After the gate it
builds one candidate snapshot: sales against the confirmed Inventory, overflow-safe totals, final
Currency and final slot capacity through `ProfileInventoryRules`, one `Commit`. Buy and sell lines
of the same loot stay independent, matching `MerchantTradePreview`. Content validation failures
return `InvalidInventory`; reservation rejection returns `ReservationPending`. Neither mutates the
profile nor publishes `ProfileCommitted`.

### Buy-back (Doc 23 §8.3)

Shared buyback is active across all three Town merchants (Mercader General, Herrero, Arcanista) backed by a
single authoritative 16-entry FIFO list per player:

- **Ownership:** The backend owns the per-player buyback history and validates buybacks as part of the atomic cart
  endpoint `POST /character/me/inventory/shop/trade` (as well as dedicated `POST /character/me/inventory/shop/buyback`).
  Unity's application-owned `BuybackLedger` is a memory read model hydrated from backend state.
- **Eligibility:** Normal equipment (weapons and armor, including upgraded instances with modifiers, gem slots,
  installed gems, and armor upgrade levels; the backend part is TASK-302, see `ItemInstanceArchitecture.md` §4.3/§5.1) and special tradable consumables (e.g. explosive/smoke bombs, throwing daggers,
  self-revive) sold to the General Merchant enter the shared buyback list.
  Sold equipment and items do NOT enter regular NPC stock or weekly rotation.
- **Price:** `ceil(1.5 * goldReceived)`, frozen per unit at sale time based on the gold actually paid to the player.
  The frozen unit price drives both preview and backend execution. For equipment, the gold received is based on
  the instance ObjectValue including upgrade bonuses (`ItemInstanceArchitecture.md` §4.3; sale-price part in TASK-302 T3).
- **Capacity:** At most **16 entries**, FIFO eviction (the 17th entry displaces the oldest). Partial buyback
  consumes units of the selected entry; purchasing all units removes the entry.
- **Lifetime:** The buyback history persists between opening and closing the merchant UI. It is explicitly cleared
  upon launching an expedition into a Dungeon (`TownRaidQueue`) or upon logging out / session termination.
- **Unified Mixed Cart Integration:** Buyback lines can be submitted alongside normal purchases and sales in a
  single atomic transaction. Sales within the cart generate gold that immediately funds purchases or buybacks in the
  same ticket, provided final gold >= 0 and final slot capacity is sufficient.
- **Presentation:** Rendered in a dedicated "Recompra" section or ledger rows accessible across all three merchants,
  showing the frozen buyback unit price and original instance details.
  a "(Recompra)" suffix. The existing row/center text fields are reused, without prefab changes.
  The session refreshes on `BuybackLedger.Changed` and matching confirmed profile commits.
  The coherent `MerchantTradePreview` projects buy-back cost and Inventory capacity through the
  same `ProfileInventoryRules` as purchases. A removed/expired or insufficient ledger entry
  blocks confirmation.

### Confirmed profile

- The character aggregate (`LocalProfileStore` via `IPlayerLoadoutService` and
  `IPlayerCurrencyService`) is the only source of persistent Inventory and Currency.
- `ProfileCommitted` for the matching `ProfileId` is the only refresh signal for Inventory and
  Currency in Commerce presentation.
- Neither Fusion nor the draft holds a copy of persistent Inventory or Currency.

## Relation to shared Inventory/Equipment infrastructure

Commerce does not introduce its own inventory model.

- Commerce reads the confirmed Inventory through the existing Town read path
  (`LocalLoadoutInventoryReadSource` / `IInventoryReadSource`) rather than keeping a separate
  list of owned items.
- Tradeable ownership is the Inventory location only. Prepared Equipment is a separate exclusive
  ownership location (see `LocalPlayerPersistenceArchitecture.md`) and is not a sell source; units
  must be unequipped through the existing Town Equipment endpoint first.
- The provisional placement of bought items in the Inventory screen, allowed by Game Design, is a
  presentation overlay derived from the preview. It is never written to the aggregate or to a
  Commerce-owned inventory.
- Capacity and stack merge rules are the aggregate's pure rules, shared by preview and commit.
- Stash is not a Commerce source or destination.

## Failure policy

- No response or authority loss: the draft unlocks with the same request id; the player may retry
  it, and dedup guarantees at most one acceptance. An accepted reservation stays held.
- Ticket accepted but backend rejects: the local profile is unchanged by Commerce, reconciliation
  is requested, the reservation is released and the draft stays editable.
- Transport loss / unknown backend outcome: return `PersistenceFailed` and request reconciliation.
  **Remaining durability gap:** the backend may already have applied the trade despite the lost
  response. Reporting not persisted can release merchant stock for that durable trade. Backend
  receipts make a later retry with the same transaction id safe only while that receipt is among the
  newest 64 (`appliedShopReceipts`). A timestamped id (`ShopTransactionId.ToString()`,
  `{unix ms}_{32 hex}`) whose receipt was evicted is rejected with `409 TRANSACTION_RETENTION_EXCEEDED`
  and never re-applied (see the receipt-retention rule at the top of this document); Commerce maps it to `InvalidInventory` (trade, buyback) or
  `AbilityPurchaseResult.TransactionExpired` (ability), reconciles, and the draft needs a new request
  identity. Reconciliation alone does not repair shared merchant stock. Closing this gap needs
  authoritative outcome lookup/reporting.
- Outcome report lost: the reservation stays held until the requester's next session resolves it
  from the confirmed profile's receipts.
- Requester leaves after acceptance and does not return: the reservation stays held for the rest of
  the merchant session. Availability is sacrificed so a unit is never sold twice.
- Duplicate responses or retried tickets are absorbed by transaction-id idempotency.

## Out of scope and open design points

- Backend endpoints, DTOs, server-side price/stock validation and durable merchant stock.
- Stock replenishment and rotation cadence, and whether stock persists across Town sessions, are
  not specified by current Game Design and must be resolved there before implementation.
- Game Design does not state whether one trade may buy and sell the same loot. The preview does not
  forbid or net it: each line is evaluated independently under the rules above.
- Item information and equipped-item comparison (Game Design UI.1.4, section 7.3) follow shared
  tooltip presentation and are not a Commerce authority or persistence contract.

## Current implementation incompatibilities (follow-ups COM-02+)

Observed on `New-Testing`. These are documented, not resolved, by this architecture; items marked
resolved record the follow-up task that closed them.

1. **Resolved in COM-07:** per-item transactions; the running flow edits one draft, previews it
   and confirms it as one multi-line trade.
2. **Resolved in COM-04:** State Authority gate, shared replicated stock, reservation until the
   persistence outcome, and authority-resolved prices returned in the response.
3. **Equipment buy-back disabled (TASK-283).** Sold equipment outside the offering enters neither
   the selling account's buy-back ledger nor shared stock; see **Buy-back** and
   [Item Instances §4.3](ItemInstanceArchitecture.md#43-operations).
   Offered items still restock the existing shared stock on persisted sales. Non-eligible items
   outside the offering do not become purchasable. Dynamic shared slots, rotation and replenishment
   remain out of scope pending Game Design.
4. **Stock is session-scoped.** Shared stock lives only for the merchant `NetworkObject`; it is
   reseeded from configuration when a new Town session spawns the merchant.
5. **Resolved in COM-07:** request identity is `ProfileId + RequestId`, deduplicated also after
   the outcome. Deduplication is bounded by `MaxTradeRecords`: a retry arriving after its record
   was evicted is evaluated as a new request.
6. **Resolved in COM-07:** State Authority issues the priced multi-line ticket; the client never
   rebuilds prices.
7. **Resolved in COM-07:** `MerchantShopUI` is presentation and intentions only.
8. **Resolved in COM-07 for the merchant:** the presenter no longer locates
   `ApplicationStashContext`; it receives the profile from `TownInventoryBinder`, which still
   finds the context with `FindAnyObjectByType` like the other Town presenters.
9. **Resolved in COM-06:** scene-name (`isLobby`) coupling in persistence; every trade reads and
   writes the Inventory (Loadout).
10. **Resolved.** `IShopTransactionService` persists one atomic multi-line ticket locally (COM-06),
    and `RemoteShopTransactionService` submits every normal ticket, mixed or single-line, through
    the atomic `POST /character/me/inventory/shop/trade` endpoint. `TownMerchantTradeSession` no
    longer rejects multi-line drafts. Rejections are mapped as described above.
11. **Backend acknowledgement gate resolved (H5/E1).** Remote trades are dry-run locally, submitted
    remotely and awaited before committing the local aggregate or reporting persistence to State
    Authority. Confirm stays locked until acknowledgement, serializing trades in one session.
    Transport loss still has the unknown-outcome durability gap described in Failure policy; no
    automatic mutation retry is performed. Close/reopen during acknowledgement also retains the
    cross-session recovery gap described under TownMerchantPresenter.
12. **Partially resolved in COM-07.** `ShopTransactionResponse`, `IMasterClientRpcSender` and the
    orchestrator were removed and `MerchantTransactionResult` now names trade outcomes.
    `LocalShopTransactionService` is still not composed.
13. **Test gaps.** EditMode covers the validator, wire codec, trade session (draft, preview,
    confirm, outcome), shop interaction and feedback mapping, UI boundaries, ticket and store trade
    transactions. Remote-service tests cover backend success, business/transport failures, dry-run
    rejection, receipt replay and pending acknowledgement. Session tests cover delayed outcomes,
    serialization and disposal during persistence. The Fusion RPC execution path and presenter
    lifecycle still require runtime validation.
14. **Held reservations of absent requesters.** A requester who leaves between ticket and outcome
    and never reopens the merchant keeps that trade's stock reserved, and its record pending, until
    the merchant `NetworkObject` ends. Enough such records would fill `MaxTradeRecords` and reject
    new trades. Resolving them without the requester needs an authoritative record of persisted
    tickets (backend).

### Ability Vendor Persistence Contract (TASK-224)

Town Ability Vendor purchases are authoritative and durable in the backend:
1. **Catalog Parity:** The backend defines `ABILITY_CATALOG` (`backend/src/config/abilityCatalog.js`), mirroring all abilities offered in Unity's `AbilityVendorCatalog.asset` with authoritative server-side prices (enforced by parity test `backend/tests/ability.catalog.parity.test.js`).
2. **Endpoint:** `POST /character/me/abilities/purchase` accepts `{ transactionId, abilityId, expectedRevision }`.
3. **Invariants:**
   - Single-document atomic update in MongoDB (`Character.findOneAndUpdate`).
   - Deduplication: Idempotent transaction receipt window (`appliedShopReceipts` with `type: 'ability_purchase'`, `abilityId`, `totalAmount`). Replaying the same `transactionId` returns 200 with `alreadySecured: true` while the receipt is among the newest 64; an evicted timestamped id returns 409 `TRANSACTION_RETENTION_EXCEEDED` without mutation, and a legacy id without timestamp keeps the window-only behavior. Unity creates the id as `ShopTransactionId.ToString()`.
   - Reservation Pending Guard: Blocked with 409 `RESERVATION_PENDING` if `inventory.pendingReservation` is active.
   - Already Unlocked Guard: Blocked with 409 `ALREADY_UNLOCKED` if the ability is already in `unlockedAbilities`.
   - Revision Guard: Optimistic concurrency control via `revision`. Blocked with 409 `REVISION_CONFLICT` if client revision does not match.
   - Authoritative Price & Balance: Debits `price` from `inventory.currency`, atomic check `$gte: price`, returns 422 `INSUFFICIENT_FUNDS` if balance is insufficient.
4. **Client Lifecycle:**
   - `TownAbilityVendorPresenter` delegates purchases to `IAbilityPurchaseService` (`RemoteAbilityPurchaseService`).
   - Preliminary dry run evaluates validity and balance locally via `LocalProfileStore.EvaluateAbilityPurchase`.
   - Awaits backend acknowledgement; on success, commits locally via `ApplyConfirmedAbilityPurchase` updating currency balance, `UnlockedAbilities`, and `RemoteRevision`.
   - On conflict or error, triggers safe reconciliation (`ProfileReconciliationService.ReconcileAsync`).

## Validation strategy

EditMode owns draft editing/cancel, preview totals and final-state capacity, authority request
dedup/reservation/release rules and atomic multi-line persistence idempotency. PlayMode or manual
multi-client validation must cover shared stock visibility, last-unit contention between two
clients, Master Client change during an open trade, requester departure with a reservation, and
UI refresh only from confirmed profile commits.

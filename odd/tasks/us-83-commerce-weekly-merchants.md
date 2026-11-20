# ODD Task Tracker: US-83 — Sistema de Comercio, Catálogo Semanal y Mercaderes del Pueblo (Docs 22 y 23)

- **Work Item:** HacknPlan #820 (US-83)
  - TASK-298 (#824): Actualizar Arquitectura de Comercio, Catálogo/ObjectValue y Elegibilidad (Docs 22 y 23)
  - TASK-299 (#825): Rotación Semanal Global, Demanda (+20%) y Stock Individual en Backend
  - TASK-300 (#826): Recompra Compartida (FIFO 16) y Contratos por Mercader en Backend
  - TASK-301 (#827): Tres Mercaderes e Interfaz Bidireccional de Comercio en Pueblo (Unity)
- **Branch:** `feat/us-83-commerce-weekly-merchants`
- **Design Sources:**
  - GD Doc 23: Sistema de Comercio y Mercaderes del Pueblo (§§1–8, 10–16)
  - GD Doc 22: Macroeconomía de Extracción (§§1–7, 11, 14)
  - `Project Grimhold/Docs/Architecture/CommerceArchitecture.md`

## Acceptance Criteria (from HacknPlan #820, #824, #825, #826, #827)

1. **Commerce Architecture & Catalog Parity (TASK-298):**
   - Catálogo permanente ilimitado exclusivo: Poción de Vida (40), Poción de Maná (40), Fragmento de Maná (16).
   - Consumibles especiales (bomba explosiva, humo, daga arrojadiza, auto-reanimador), tesoros y recursos no se venden en tienda regular. Se pueden vender al NPC por floor(ObjectValue * 1.0) (mínimo 1 oro si Tradable=true).
   - Precios: Compra = ceil(ObjectValue * 2.0). Venta regular = floor(ObjectValue * 1.0).
   - 12 armas (ObjectValue 45-125) y 24 armaduras (ObjectValue 40-160) alineadas en Unity y Backend, incluyendo Rapier, MagicSword y MagicCinquedea.
   - Objetos de entrenamiento: Tradable=false, ObjectValue=0. No comprables, vendibles ni recomprables.
   - MaxTransportable=1: Stash de Pueblo permite acumular múltiples unidades de Brújula, Detector y Auto-reanimador; salida a Dungeon bloquea si el inventario de salida excede MaxTransportable=1.

2. **Weekly Rotation, Demand (+20%) & Player Stock (TASK-299):**
   - Rotación semanal global autoritativa en Backend cada lunes 00:00 UTC con seed determinístico por semana Unix.
   - Oferta semanal: 3 armas normales, 1 arma especial modificada (E/D/C o 1 slot vacío) con ObjectValue incrementado, 3 piezas de armadura, 2 utilidades distintas (stock: Ganzúa 3, Brújula 1, Detector 1), 3 recursos/tesoros en demanda bonificada (+20% precio venta = floor(ObjectValue * 1.2)).
   - Stock semanal individual persistido por jugador.
   - Endpoint de transacción atómica de carrito con verificación de precios, stock, frescura de rotación (reconfirmación si cambió) y capacidad de inventario.

3. **Shared Buyback (FIFO 16) & Merchant Filtering (TASK-300):**
   - Historial FIFO de recompra de 16 elementos por jugador, compartido entre los 3 mercaderes.
   - Recompra conserva instancia exacta (ID, ranuras, gemas, modificadores, nivel de armadura).
   - Precio de recompra = ceil(1.5 * Oro efectivamente recibido por el jugador).
   - Filtros por tipo de mercader (General, Herrero, Arcanista) tanto para venta al NPC como para catálogo.
   - Recompra se limpia al iniciar incursión o cerrar sesión.

4. **Unity Integration & Dual Cart UI (TASK-301):**
   - 3 mercaderes diferenciados en Pueblo con interacción contextual y filtros correspondientes.
   - UI de tienda con secciones: Oferta Permanente, Rotativa Semanal (con temporizador hasta próximo lunes 00:00 UTC y stock personal), Demanda (+20%), y Recompra (FIFO 16).
   - Carrito bidireccional atómico que recalcula balances según reglas oficiales y detecta rotación expirada requiriendo confirmación.

## Work Units (TDD Cycle)

- [x] S1: TASK-298 — Update CommerceArchitecture.md & Technical Contracts
  - Updated `CommerceArchitecture.md` with:
    - 3 specialized merchants: General Merchant (permanents + utilities + buyback + all buys), Blacksmith (physical weapons + armors), Arcanist (magic weapons), Trainer (free training weapons).
    - Commercial pricing formulas: buy `ceil(OV * 2.0)`, regular sell `floor(OV * 1.0)`, demand `floor(OV * 1.2)`, buyback `ceil(1.5 * gold received)`.
    - Autoritativo weekly rotation (Monday 00:00 UTC) with deterministic seed and personal player stock.
    - Unified mixed cart transaction with rotation expiration guard.
    - Shared buyback (FIFO 16) preserving exact instances and special consumable stacks, cleared on raid start or logout.
    - MaxTransportable = 1: Stash hoarding allowed, raid admission blocked on excess.
- [x] S2: TASK-298 — Item Catalog Parity & ObjectValue Alignment (Backend + Unity)
  - Aligned all 12 normal weapons with Doc 23 §6.1 ObjectValues (45–125), buyPrice (ceil * 2), sellValue (floor * 1).
  - Aligned all 24 armors with Doc 23 §6.2 ObjectValues (40–160), buyPrice (ceil * 2), sellValue (floor * 1).
  - Restricted regular NPC purchase: special consumables (bombs, dagger, self-revive), treasures, and resources have `buyPrice = 0` / not in BUY_CATALOG, preserving `sellValue = ObjectValue`.
  - Excluded training items from buy and sell (`Tradable = false`, OV = 0, buy/sell = 0).
  - Automated tests: `backend/tests/commerce.catalog.doc23.test.js` (6/6 PASS), `shop.catalog.parity.test.js` (PASS), `item.catalog.parity.test.js` (PASS), `ArmorCommerceParityTests.cs`, `WeaponCatalogTests.cs`.
- [x] S3: TASK-298 — MaxTransportable Validation: Stash Hoarding vs Raid Admission Block
  - Verified `RaidAdmissionData.TryValidateLoadout` strictly blocks entry to Dungeon when inventory contains > 1 unit of `UTL_RUNE_COMPASS`, `UTL_ARCANE_DETECTOR`, or `CON_SELF_REVIVE`.
  - Verified Town Stash permits hoarding multiple units without transport limits.
  - Automated tests: `MaxTransportableAdmissionAndStashTests.cs`.
- [x] S4: TASK-299 — Backend Weekly Rotation Generator, Deterministic Seed & Weekly Demand Pool
  - Implemented `CommerceRotationService.js`:
    - Deterministic Mulberry32 RNG seeded by Monday 00:00 UTC `weekId`.
    - Generates 3 normal weapons, 1 special weapon (E/D/C modifier or 1 socket with OV increment), 3 armors, 2 distinct utilities (with individual stocks: lockpick 3, compass 1, detector 1), 3 demand items (+20% bonus), and permanent catalog.
    - Automated tests: `backend/tests/commerce.rotation.test.js` (4/4 PASS).
- [x] S5: TASK-299 — Player Weekly Stock & Atomic Mixed Cart Transaction Backend Endpoint
  - Implemented mixed cart atomic execution in `InventoryService.shopTrade`:
    - Purchases, sales, and buybacks unified into a single atomic transaction.
    - Validates `weekId` against current rotation (rejects with 409 `ROTATION_EXPIRED` on mismatch).
    - Tracks and enforces individual player weekly stock quotas (`weeklyStock` field on Character).
    - Applies +20% price bonus (`floor(ObjectValue * 1.2)`) on active weekly demand items.
    - Automated tests: `backend/tests/commerce.cart.test.js` (5/5 PASS).
- [x] S6: TASK-300 — Shared Buyback (FIFO 16) & Merchant Categorization / Filtering
  - Implemented shared FIFO buyback pool of up to 16 entries per player, shared across all merchants.
  - Buybacks preserve exact item instance attributes and calculate price as `ceil(1.5 * goldReceived)`.
  - Clears buyback ledger upon raid reservation (`savePendingReservation`).
  - Added merchant specialization filtering (`isAllowedByMerchant` in backend, `MerchantSpecializationPolicy` in Unity).
  - Automated tests: `backend/tests/inventory.buyback.test.js`, `backend/tests/inventory.shop.test.js`.
- [x] S7: TASK-301 — Unity Merchant Presenters, Cart Preview, Merchant Shop UI & Countdown
  - Implemented `MerchantSpecializationPolicy` (`TownMerchantType`: General, Blacksmith, Arcanist).
  - Updated `MerchantTradePreview` to support mixed cart (purchases, sales, buybacks) without `BuybackMustBeAlone`, supporting demand +20% pricing and merchant filtering.
  - Added `TownMerchantType`, merchant title, weekly countdown calculation (until next Monday 00:00 UTC), and demand tag badge `[Demanda +20%]` to `MerchantShopViewModel` and `MerchantShopUI`.
  - Updated `TownMerchantTradeSession` to coordinate bidirectional trade with specialized filtering, demand pricing, buyback ledger integration, and rotation expiration handling.
  - Updated `RemoteShopTransactionService` and DTOs to serialize `buybacks`, `weekId`, and `merchantId` and map `ROTATION_EXPIRED`.
  - Automated tests: `TownMerchantTradeSessionTests.cs` (42/42 PASS), `MerchantTradePreviewTests.cs` (30/30 PASS), `RemoteShopTransactionServiceTests.cs` (36/36 PASS).
- [x] S8: Full Test Suite Verification (Backend + Unity EditMode/PlayMode) & Validation Report
  - Backend test suite: 796/796 tests PASS across 34 suites (0 failures).
  - Unity EditMode test suites:
    - `ArmorCommerceParityTests`: 4/4 PASS.
    - `MaxTransportableAdmissionAndStashTests`: 10/10 PASS.
    - `TownMerchantTradeSessionTests`: 42/42 PASS.
    - `MerchantTradePreviewTests`: 30/30 PASS.
    - `WeaponCatalogTests`: 13/13 PASS.
    - `RemoteShopTransactionServiceTests`: 36/36 PASS.
- [x] S9: 3 Specialized Town Merchant NPCs in Lobby-Town Scene (Doc 23 §3)
  - Configured 3 distinct merchant NPCs in `Project Grimhold/Assets/Scenes/Lobby-Town.unity`:
    - **Mercader General** (`TownMerchantType.General` = 0): Posición `(0.53, 0.76, 0)`, prompt "Comerciar (Mercader General)", catálogo permanente y utilidades rotativas.
    - **Herrero** (`TownMerchantType.Blacksmith` = 1): Posición `(-2.2, 1.5, 0)`, prompt "Comerciar (Herrero)", diálogo temático `TownBlacksmithNpcDialogue.asset`, oferta de armas físicas y armaduras.
    - **Arcanista** (`TownMerchantType.Arcanist` = 2): Posición `(2.8, -1.8, 0)`, prompt "Comerciar (Arcanista)", diálogo temático `TownArcanistNpcDialogue.asset`, oferta de armas mágicas.
  - Verified no collider/trigger overlap with existing town NPCs (`TownTrainerNpc`, `TownAbilityVendorNPC`, etc.).
- [x] S10: Game Designer Unity Configuration & Commerce Parity Pipeline (Docs 22 y 23)
  - Implemented `TownCommerceConfiguration.cs` ScriptableObject in Unity (`Grimhold/Commerce/Town Commerce Configuration`):
    - Multipliers: Buy (2.0), Regular Sell (1.0), Demand Bonus (1.2), Buyback (1.5), FIFO capacity (16).
    - Rotation counts: normal weapons (3), special weapons (1), armors (3), utilities (2), demand items (3).
    - Permanent offers: Health Potion, Mana Potion, Mana Fragment.
    - Rotating pools: Physical weapons (7), Magical weapons (5), Armors (24), Utilities with individual stock quotas (3).
    - Special modifier probability table (Scaling E/D/C, 1 slot) with ObjectValue increments.
    - Demand candidates pool (29 resources and treasures).
  - Implemented `TownCommerceConfigurationEditor.cs` custom Inspector with one-click export button and menu item (`Grimhold > Commerce > Export Commerce Config to Backend`).
  - Created authoritative ScriptableObject asset `TownCommerceConfiguration.asset` in `Assets/Scriptable Objects/Commerce/`.
  - Configured backend (`CommerceRotationService.js` and `shopCatalog.js`) to load directly from `commerceConfig.json`.
  - Automated parity tests: `TownCommerceConfigurationTests.cs` (Unity EditMode) and `commerce.config.parity.test.js` (Backend, PASS).
  - UI countdown feedback: Bound `_titleText` and dedicated `_rotationCountdownText` in `MerchantShopUI.prefab` and `MerchantShopUI.cs`. Verified in `MerchantShopUIRenderTests.cs` (PASS).
  - Dynamic Stock Binding: Connected `TownMerchantNetworkController` to `TownCommerceConfiguration` (`BuildInitialStockForMerchant`) so General Merchant (permanents + utilities), Blacksmith (physical weapons + armors) and Arcanist (magical weapons) dynamically seed their inventories from the ScriptableObject configuration instead of sharing the legacy prefab test stock.
- [x] S11: Merchant Specialization Stock Initialization Bugfix (Herrero & Arcanista)
  - Cause: `TownMerchantNetworkController` used a narrow `IsLegacyDefaultStock` heuristic checking for `"ArcaneMage"`. Once the prefab `_stock` was populated with General Merchant items, Blacksmith and Arcanist scene instances inherited those general items; because they contained no ArcaneMage pieces, re-initialization was skipped and `MerchantSpecializationPolicy` filtered out all non-weapon/non-armor items, leaving their shops empty.
  - Fix: Replaced `IsLegacyDefaultStock` with authoritative `NeedsStockInitialization()`: if `_stock` is empty or contains any item disallowed by `MerchantSpecializationPolicy.IsAllowedToBuyFromMerchant(_merchantType, item)`, the controller automatically rebuilds stock from `_commerceConfig.BuildInitialStockForMerchant(_merchantType, MaxStockSlots)`.
  - Added `PrefabUtility.IsPartOfPrefabAsset(this)` guard in `OnValidate` to prevent inadvertent mutation of prefab assets in the Project browser.
  - Automated tests: Added `MerchantNetworkController_InitializesAppropriateStockForEveryMerchantType` in `TownCommerceConfigurationTests.cs`.
  - Verification: Backend test suite 797/797 PASS. Parity tests PASS.

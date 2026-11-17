# Raid admission fails on legacy item ids (healthpotion -> CON_HEALTH_POTION)

Bug fix on `New-Testing` (no HacknPlan item named). After `1380206d` turned `healthpotion` into an alias, local raid admission fails when the reserved loadout carries the legacy id.
No commits, pushes, deploys or runs of the migration against real data without explicit authorization.

## Specs

- S1 (user report, verbatim): "no estoy pudiendo iniciar una raid [FusionSessionLauncher] Local raid admission did not complete with the exact reserved loadout." followed by "Coordinated raid launch failed. Role=Host; ... Result=ConnectionFailed." A loadout holding a health potion (legacy id `healthpotion` from backend data) must be admitted to the raid.
- S2 (user, verbatim): "igual te confirmo que si tenia health potion en el inventario".
- S3 (accepted scope, verbatim option): "Cliente + backend + migración" — "Lo anterior (cliente: canonicalizar ids legados al hidratar el stash desde el backend y en el matcher de admisión antes de comparar; backend: normalizar ids en tickets v3 y lecturas de inventario, con tests Node) más un script explícito que reescriba `healthpotion` → `CON_HEALTH_POTION` en documentos guardados. Reescribe datos persistidos (riesgo alto) y hay que correrlo manualmente."
- S4 (constraint): exact admission validation is preserved (same canonical item, same quantity, same per-unit modifiers); no bypass. Unknown ids keep failing. The user's profile is not modified by the fix itself.
- S5 (constraint): the migration is explicit (never run on read), dry-run by default, idempotent, and reports what it would change.

## Tasks

- [x] T1 — S1, S2, S4: Unity client. Repro test first: an EditMode test in `RaidAdmissionLoadoutMatcherTests` where the reserved inventory has `healthpotion` and the materialized side has `CON_HEALTH_POTION`, run for both ticket and local branches. Then canonicalize in `RaidAdmissionLoadoutMatcher` and in backend stash hydration (`ApplicationStashServiceBootstrapper`). Route: worker A. Commit: —
- [x] T2 — S3, S4 (v3 ticket normalized; inventory reads stay raw per contract, L7): backend normalization of legacy ids in ticket v3 serialization and inventory reads, with Node tests. Route: worker B. Commit: —
- [x] T3 — S3, S5: backend explicit migration script `healthpotion` → `CON_HEALTH_POTION`, dry-run default, idempotent, with Node tests (no real DB). Route: worker B. Risk: item 1. Commit: —
- [x] T4 — S1-S5: independent verification (item 1 data rewrite, item 3 ticket payload) + parent spot check. Commit: —

## Log

- L1 (user): "no estoy pudiendo iniciar una raid [FusionSessionLauncher] Local raid admission did not complete with the exact reserved loadout. ... [SessionConnectionCoordinator] Coordinated raid launch failed. Role=Host; Session=raid-658713; Result=ConnectionFailed."
- L2 (user): "igual te confirmo que si tenia health potion en el inventario"
- L3 (evidence, explorer mwkxmarf-5-r1gb):
  - The expected side keeps the raw id (`RaidAdmissionLoadoutMatcher.cs:44`; `RaidAdmissionData.cs:258/266`).
  - The materialized side uses the canonical id (`PlayerLootReceiver.cs:489`, alias resolution in `LootDefinitionCatalog.cs:71-73,98-100`).
  - Ids are compared ordinally (`LootId.cs:24`), so the exact match fails at `RaidAdmissionLoadoutMatcher.cs:57`.
  - The raw id enters through backend hydration (`ApplicationStashServiceBootstrapper.cs:459-467`) and through the v3 ticket (`backend/.../SessionTicketService.js:200-203`, raw `serializeItems` in `InventoryEntries.js:17-20`). Only v2 normalizes (`:205`).
  - The local profile codec and quick slots already normalize (`LocalProfileSaveCodec.cs:1115-1117`, `QuickSlotBindings.cs:127-129`).
  - Editor.log was unavailable.
- L4 (user decision): "Cliente + backend + migración".
- L5 (evidence, worker A mwkxxwk9-6-e72q):
  - RED: legacy-alias admission tests (ticket false and true) failed with `Expected: True But was: False`; hydration test failed with `Expected: CON_HEALTH_POTION But was: healthpotion`.
  - GREEN: matcher canonicalizes ids through the catalog before comparing; hydration stores `definition.LootId`.
  - Totals: RaidAdmissionLoadoutMatcherTests 34/34, ApplicationStashServiceBootstrapper 3/3, LegacyItemUnification 3/3, LocalProfile 77/77, RaidAdmission 149/151.
  - The 2 failures are `PhysicalProfileSlotTests.RaidAdmissionCapacity_ExtractionThenReservation_KeepsEquipmentOutsideBackpack(1)` and `(30)` at :212. They exercise only LocalProfileStore, outside this diff by code path; not re-run without the change.
  - Limitation: `RaidLoadoutRules.TryValidateShape` still checks duplicate raw ids (`RaidAdmissionData.cs`, outside this diff).
- L6 (evidence, worker B mwkxxwkf-7-bsw8):
  - Reused the existing `InventoryLootIdNormalizer.normalizeLootId` (healthpotion -> CON_HEALTH_POTION); no new alias table.
  - v3 ticket `admissionEquipment` now normalizes `items` and `preparedEquipment`.
  - New `backend/scripts/migrate_legacy_item_ids.js`: dry-run by default, `--apply` to write, revision-guarded `updateOne` with retries, merges within a list, BigInt total check, JSON summary. Never run against a database.
  - Tests: `inventory.legacy-ids.test.js` and `migrate.legacy.item.ids.test.js`. RED observed (raw `healthpotion` emitted; missing module). Totals: npm test 815/815, test:concurrency 35/35, test:integration 245/245. README section added.
  - Held back: canonicalizing inventory reads broke 23 tests and contradicts the documented contract `README.md:58` "Ninguna lectura migra aliases" (inventory.protocol "never repairs aliases", inventory.service "preserves legacy aliases"). Reverted; needs a user decision. The v1 ticket branch is still raw.
- L7 (user decision): "Respetar el contrato (recomendado)". Inventory reads keep the stored raw id (`README.md:58`). Coverage comes from Unity hydration, the v3 ticket and the migration.
- L8 (verify mwky8yxl-8-0ln4): no blockers. S1-S5 PASS. No backend mutation compares a client id with the stored raw id, and nothing compares ticket items with pendingReservation.
  - Totals: npm test 815/815, test:integration 245/245, RaidAdmissionLoadoutMatcherTests 34/34. PhysicalProfileSlotTests 16/19; the 3 failures are in LocalProfileStore.TryCommitExtraction, outside the diff.
  - Advisories: the migration and the v3 ticket apply the whole backend alias table (`InventoryLootIdNormalizer.js:5-18`: longsword, greatsword, staff, wand/spellbook, training_*, heavy_armor_legplate), not just healthpotion. Unity's catalog only knows the healthpotion alias (pre-existing). buyback.entries are not covered. One invalid entry aborts the whole character. v1 reorder still uses raw equality.
  - Suggested 3 extra migration regression tests (overflow rename, canonical overflow is not a merge target, case variants are not aliases). The verifier's scratch directory was already gone when the parent checked.
- L9 (user decision): "Todos los alias + 3 tests extra (recomendado)". Kept the full backend alias table and added the 3 regression tests to `migrate.legacy.item.ids.test.js`. Focused run of both new files: 13/13. Full `npm test`: 818/818, exit 0.
- L10 (verify mwkyp9gj-9-ppfa, delta): S3-S5 PASS. Production files unchanged since L8; npm test 818/818. Advisory: the overflow test was weak (a single entry, nothing to merge). The parent replaced it with the suggested two-stack version that asserts `merged == 0`. Focused run 13/13.
- L11 (verify mwkysrks-a-2kjk): S5 PASS. The replaced test would fail without the in-capacity guard (`migrate_legacy_item_ids.js:75`); `lists.stash.merged` is real (`:91`). No production change since L10. Totals: 13/13 and npm test 818/818. Script sha256 `458e11d5…ab31a`.

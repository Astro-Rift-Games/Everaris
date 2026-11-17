# TASK-296 — Tests de Split de Stash y validador independiente del generador de armas

HacknPlan: TASK-296 (work item #803, Sprint 18). Execution tracker only; scope and acceptance live in HacknPlan.
Sources: Doc 09 §8.1/§8.2/§22, Doc 14 §6 (grimhold-docs, read 2026-11-17). Commits require explicit authorization (repo AGENTS.md); authorized by the user ("listo commitea").

## Specs

- S1 — "Implementar tests unitarios EditMode para la operación de split de stacks en StashTransferCoordinator."
- S2 — "Corregir el test PlayMode para que divida efectivamente 13 en 5 + 8 (haciendo honor a su especificación)."
- S3 — "Crear un validador de pruebas independiente que audite 1.000 armas generadas directamente contra las reglas de combinaciones legales y coeficientes de Doc 09 §8.2, eliminando el test circular que usaba el validador de producción."
- S4 — "Mantener el soporte de gemas sobre los tipos de daño soportados por el core (Physical, Magical) conforme a Doc 09 §22, dejando los tipos elementales fuera de alcance hasta que Game Design defina un catálogo formal de elementos." / AC: "No se agregan tipos de daño no acordados al enum DamageType."
- Validation — "Tests EditMode y PlayMode de split aprobados al 100%." / "Ejecución de la suite de validación de 1.000 armas contra el validador independiente de tests."

Doc 09 §8.1 coefficients: S 1,00 · A 0,85 · B 0,70 · C 0,55 · D 0,40 · E 0,25.
Doc 09 §8.2: principal = competencia natural (FUE→FUE, DES→DES, INT→INT); secundario cualquier otro atributo (VIT, RES, FUE, DES, INT, SUE) distinto del principal; no repetir atributo.

## Tasks

- [x] T1 (S1) — Coordinator-level EditMode split tests in `StashTransferCoordinatorTests.cs` for untested paths (EvaluateSplit, schema 3, reservation pending, input-rule rejections, single unit). Inline. Commit: e2fdec00.
- [x] T2 (S2) — Per L7: move the real 13 → 5 + 8 split to the Town Stash PlayMode test (`LobbyStashPresenterPlayModeTests.cs`, `TownSplit_ConfirmsExactAmountInBothListsRemoteFirst`, today splits 2) and rename the raid test `SplitStack_ThirteenIntoFiveAndEight_...` in `PlayerLootReceiverSlotsPlayModeTests.cs` to what it really does (10 → 5 + 5), keeping its assertions. Worker. Commit: e2fdec00.
- [x] T3 (S3) — Independent test-side validator (`Doc09WeaponLegalityValidator`) + 1000-weapon audit replacing the `instance.TryValidate` circular assertion; validator self-tests on illegal combos. Inline. Commit: the test(equipment) commit after e2fdec00.
- [x] T4 (S4) — Verify gems stay damage-type agnostic and DamageType gains no values; no code change expected. Inline.
- [x] T5 — Run EditMode (Stash split, coordinator, equipment) and PlayMode split tests through Unity CLI; report.

## Log

- L1 (user, verbatim): "revisa TASK-296 — Tests de Split de Stash y validador independiente del generador de armas y empeza la implementacion siguiendo ODD"
- L2 explore: coordinator split `StashTransferCoordinator.cs:74-92`; existing coordinator split tests `StashTransferCoordinatorTests.cs:1051-1194`; PlayMode test `PlayerLootReceiverSlotsPlayModeTests.cs:840` splits slot of 10 into 5+5; circular test `WeaponInstanceGeneratorTests.cs:53`; `DamageType` = Physical, Magical, TrueDamage (pre-existing TrueDamage, not added by this task); gems carry no damage type (`GemDefinition.cs:5-12`).
- L3 T3 RED: stub validator → `Doc09WeaponLegalityValidatorTests` 20 total, 8 passed (legal cases), 12 failed (illegal cases). Validator implemented; circular `instance.TryValidate` removed from the 1000-weapon test.
- L4 T4: `git diff -- Assets/Scripts` empty; `DamageType` = Physical, Magical, TrueDamage unchanged; no `DamageType` reference in `Player/Equipment`.
- L5 T2 blocked: no LootDefinition asset has StackMax ≥ 13 (max 10: bone, coins, RES_*...); raid PlayMode fixture clones Bone at StackMax 10 and other tests depend on it. A single 13-unit raid slot is not a valid state. Owner decision needed.
- L7 (user decision, verbatim option): "Stash de Town PlayMode + renombrar el de raid (Recom.)" — "Town no aplica StackMax: subo el split 13→5+8 a `LobbyStashPresenterPlayModeTests` (hoy divide 2) y renombro el de raid a lo que hace de verdad (10→5+5). Coincide con «Split de Stash»."
- L8 T2 done (worker): new `TownSplit_ThirteenIntoFiveAndEight_ConservesTotalInEachListRemoteFirst` (`LobbyStashPresenterPlayModeTests.cs:716-758`); raid test renamed `SplitStack_TenIntoFiveAndFive_...` (`PlayerLootReceiverSlotsPlayModeTests.cs:840`). PlayMode (async): LobbyStashPresenterPlayModeTests 32/32, PlayerLootReceiverSlotsPlayModeTests 27/27. Mutation check (dialog 5→2) failed the new test, then restored. Side effect: Unity rewrote `alagard SDF.asset` and `LiberationSans SDF - Fallback.asset` (dynamic TMP atlases) during runs — not authored, pending user disposition.
- L6 GREEN (Unity CLI, open Editor 6000.6.8f1): Doc09WeaponLegalityValidatorTests 20/20, WeaponInstanceGeneratorTests 20/20 (3×1000 weapons audited), StashTransferCoordinatorTests 98/98 (24 new: InvalidInput 16, ReservationPending 2, SmallestStack 2, SchemaThree 1, EvaluateSplit 3), StashSplitTests 46/46. T1 tests are characterization of existing behavior: no meaningful RED.

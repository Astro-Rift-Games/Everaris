# TASK-333 — Recoger loot no suma Progreso de Expedición

HacknPlan TASK-333 (work item 888), Sprint 19. Execution tracker only: scope and acceptance live in HacknPlan.
Branch `feat/task-333-loot-no-expedition-progress` from `origin/New-Testing` (`de647a51`).

## Specs

Quoted verbatim from HacknPlan TASK-333 and live Game Design Doc 06 §6.

- **S1** — "Recoger un tesoro o cualquier loot no cambia el progreso del equipo."
- **S2** — "Transferir loot entre jugadores no cambia el progreso."
- **S3** — "Las 3 fuentes del Doc 06 siguen otorgando progreso y permiten completar la cuota Solo y Dúo."
- **S4** — "Tras Host Migration, el progreso restaurado no incluye aportes de loot."
- **S5** — "Revisión de la cuota y de los textos del HUD que mencionen el loot como fuente, si existen."
- **S6** — Dependency: "El origen del loot (loot provenance) lo usan también los contratos (FIX-104): retirar sólo su aporte al progreso, no el registro de origen."
- **S7** — Out of scope: "Cambiar los valores de cuota o de las otras fuentes." / "Cambiar el origen del loot que usan otros sistemas."
- **GD 06 §6** — "Recoger Loot normal, equiparlo, transferirlo o simplemente poseer tesoros no genera Progreso de Expedición dentro del MVP." Sources: Derrota de criatura, Primera apertura válida de cofre, Derrota definitiva de jugador enemigo.

## Tasks

- [x] **T1** (S1, S6) — Pickup grants no progress; provenance/eligibility, mission ledger and treasure instability preserved. Route: inline, TDD PlayMode `ExtractionProgressPlayModeTests`. Commit: `fix(extraction)` TASK-333 commit
- [x] **T2** (S2, S6) — Withdrawal through `PlayerLootTransferNetworkController` grants no progress; drop the now-unused `_progressReceiver` dependency. Route: inline, TDD PlayMode. Commit: `fix(extraction)` TASK-333 commit
- [x] **T3** (S3, S4) — Retire `ExtractionProgressSourceType.LootFirstAcquisition` (no renumbering); migrate test fillers to valid sources; Solo/Duo quota tests stay green; Host Migration snapshot carries only `CurrentProgress`. Route: inline. Commit: `fix(extraction)` TASK-333 commit
- [x] **T4** (S5) — Update `LootInteractionArchitecture.md` loot-as-source text; HUD/quota review. Route: inline. Commit: `fix(extraction)` TASK-333 commit
- [x] **T5** — Manual Editor validation Solo and Dúo multi-process with logs. Route: owner (manual). Commit: `fix(extraction)` TASK-333 commit

## Log

- **L1** — User request: "revisa TASK-333 — Recoger loot no suma Progreso de Expedición y empeza la implementacion con ODD"
- **L2** — Exploration: grants at `NetworkLootPickup.cs:400-411` and `PlayerLootTransferNetworkController.cs:588-598` (withdrawal into own receiver only; player→player deposits never granted). Enum `ExtractionProgressSourceType` is not serialized; `TeamProgressSlot` has no per-source breakdown. Sources in code: `Defeat` (creature + PvP definitive, `DamageResolver.cs:336`) and `ContainerFirstOpen` (`NetworkLootContainerInteractable.cs:146`) — matches GD 06 §6's three sources. No HUD/localization text names loot as a source. `LootDefinition.ExtractionValuePerUnit` becomes unused data (serialized in assets): kept, owner follow-up.
- **L3** — Repository policy: commits require explicit authorization; no work-unit commits made without it.
- **L4** — RED (production untouched): `Pickups_NaturalLootAndTreasureNeverContributeExpeditionProgress` 10≠0; `ContainerWithdrawal_ThroughTransferControllerNeverContributesExpeditionProgress` 25≠5. `FatalDamage_AwardsConfiguredMeleeRangedAndPlayerRewardsOnlyOnce` already failing ("Expected: True But was: False") before any production change — pre-existing, out of scope.
- **L5** — GREEN: ExtractionProgressPlayModeTests 12/13 (only pre-existing FatalDamage fails); RaidMinimapVisibilityPlayModeTests 7/7; PlayMode "Loot" filter 158/158; EditMode full 8505/8533 (28 failures in Buyback, Stash/PhysicalProfileSlot, Sanctuary prefab, Catacombs scene, attack validator, UI contract, word-count — none in the diff's types; not re-run on base). S4: `TeamProgressSlot` stores only summed `CurrentProgress`, so restored progress cannot contain loot once no loot contribution is applied.
- **L6** — Editor/test runs regenerated unrelated files (minimap layouts, 2 TMP font assets with content; ~90 .anim + 3 ProjectSettings EOL-only). Not task changes: exclude from commits; not reverted without owner ok.
- **L7** — User: "ok ya lo probe y funciona como se espera, commitea" — manual Editor validation reported passing by the owner; commit authorized. Regenerated unrelated assets excluded from the commit.

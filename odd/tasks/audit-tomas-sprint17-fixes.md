# Audit — Tomás Taboada's work items in Sprint 17 and Fixes

Read-only audit of every HacknPlan item assigned to Tomás Taboada (`Tabor`, user 233029) on board "Sprint 17 — Wraith, Network, Town Fixes & Art" (694792) and board "Fixes" (693803): 48 items in 7 groups.
Method: acceptance criteria vs. the delivered code and tests on `New-Testing` (HEAD `81bbc3201`). No code changes, no HacknPlan updates, no commits.
Verbatim HacknPlan snapshots: `odd/evidence/audit-tomas-sprint17-fixes/G1.md` … `G7.md`.

## Specs

- **S1 — Scope.** User: "quiero hacer una auditoria de todas las tareas de Tomas desde el 15/11, tanto del sprint 17 como del board fixes". Scope decision: "Todo lo de Tomás en S17 y Fixes".
- **S2 — Depth.** Decision: "Criterios vs código + tests (Recomendado)": for each item and its parent, check every acceptance criterion against the commit diff and current code; review test quality, Architecture contracts and Game Design where it applies. Read-only.
- **S3 — Deliverable.** Decision: "odd/tasks + resumen en chat (Recomendado)": this document with findings per item, an Engram mirror and a chat summary. No HacknPlan changes, no commits.
- **S4 — Method.** User: "usando ODD como metodologia".

### Audit groups (all commits are ancestors of `New-Testing` HEAD)

| Group | Items (HacknPlan ID) | Tomás commits | Tomás ODD tracker |
| --- | --- | --- | --- |
| G1 | US-78 (719), TASK-272..275 (720-723) | `1d8238334`, `baabc056f` | `odd/tasks/catacombs-specter-enemy.md` |
| G2 | TASK-285 (792), 286 (793), 287 (794), 288 (795), 289 (796), 290 (797), 291 (798) | 285 `1335698f9`; 286 `0cabe0c69`; 287 `e4a9c92eb`; 288 `758c3c021`; 289 `b56691444`; 290 `bbe706545`, `195c4605e`; 291 `8f76fa1a0` | `odd/tasks/task-285-…` to `task-291-…` |
| G3 | FIX-US-07 (711), FIX-39..44 (712-717), FIX-96 (808) | 39 `f10c68a9f`; 40 `2f8309252`; 41 `ebbadf320`; 42 `be6b97307`; 43 `6c6102153`; 44 `ddc80c215`; 96 `6dfb67bf7`; related `50bdc895e` | none found |
| G4 | FIX-US-09 (743), FIX-62..65 (747-750) | `9509d199b`, `2877fbfbe` | none found |
| G5 | FIX-US-10 (744), FIX-67/68/69/74/75/76/77/78 (756-758, 763-767) | `6d4f3b867`, `bcd961c55`, `f17de9ca2` | `odd/tasks/fix-us-10-items-inventory-utilities.md` |
| G6 | FIX-US-11 (745), FIX-79..82 (751-754), FIX-88..91 (782-785) | `5013e60e9`, `bccad7f7a` | `odd/tasks/fix-us-11-combat-pvp-attribution.md` |
| G7 | FIX-US-12 (746), FIX-83..86 (768-771) | `0c3e6db5e`, `072114110`, `3471098b9`, `af9e3f516` | `odd/tasks/fix-us-12-collapse-and-downed-presentation.md` |

Verdict scale per acceptance criterion: MET, PARTIAL, NOT MET, NOT VERIFIABLE (needs Editor, runtime or Game Design). Finding severity: BLOCKER, MAJOR, MINOR, NIT.

## Tasks

- [x] T1 (S1-S4, G1) — audit US-78 and TASK-272..275. Route: gentle-ai-verify (task mwk6b8l7-1-2wlp). Evidence: static audit, no runs.
- [x] T2 (S1-S4, G2) — audit TASK-285..291. Route: gentle-ai-verify (task mwk6b8sa-2-enud). Evidence: static audit, no runs.
- [x] T3 (S1-S4, G3) — audit FIX-US-07, FIX-39..44, FIX-96. Route: gentle-ai-verify (task mwk6b8ze-3-w1bu). Evidence: static audit, no runs.
- [x] T4 (S1-S4, G4) — audit FIX-US-09, FIX-62..65. Route: gentle-ai-verify (task mwk6b96z-4-dh5h). Evidence: static audit, no runs.
- [x] T5 (S1-S4, G5) — audit FIX-US-10 and its eight fixes. Route: gentle-ai-verify (task mwk6b9eh-5-1z22). Evidence: static audit, no runs.
- [x] T6 (S1-S4, G6) — audit FIX-US-11 and its eight fixes. Route: gentle-ai-verify (task mwk6b9mc-6-6z4t). Evidence: static audit, no runs.
- [x] T7 (S1-S4, G7) — audit FIX-US-12, FIX-83..86. Route: gentle-ai-verify (task mwk6b9uz-7-ivo3). Evidence: static audit, no runs.
- [x] T8 (S2, S3) — spot-check findings, consolidate the report and summarize in chat. Route: parent. Evidence: L4.

## Findings

### Consolidated summary

| Group | HacknPlan stage | Blockers | Majors | Headline |
| --- | --- | --- | --- | --- |
| G1 US-78 | Completed | 0 | 4 | Host Migration revives a dead Specter; criteria closed before being met |
| G2 TASK-285..291 | Completed | 2 | 9 | CON_E_002 cannot progress; local cache overrides backend progression |
| G3 FIX-US-07 | Testing | 1 | 8 | Closure-window criteria contradict architecture; grep-based tests |
| G4 FIX-US-09 | Testing | 0 | 2 | Fail-open scanner (fixed later by Dani); self-attested credential decision |
| G5 FIX-US-10 | Testing | 2 | 6 | SmokeZoneRegistry still global, wall test cannot fail |
| G6 FIX-US-11 | Testing | 1 | 5 | Old PlayMode tests contradict new projectile/orb code; Game Design decisions missing |
| G7 FIX-US-12 | Testing | 0 | 4 | Hook tests tautological or on a fake; timer reset not addressed |

Cross-cutting patterns: (1) tests that cannot fail (source greps, reflection-existence checks, fakes copying production logic, self-comparisons); (2) Game Design decisions HacknPlan required before implementation were chosen by the implementer and recorded as resolved; (3) tracker claims of RED/GREEN and suite counts with no recorded runner output; (4) unrequested work bundled into item commits (debug tools, persistence contract changes); (5) items closed or moved to Testing with criteria unmet or delivered later by others.

Parent spot-checks (L4) confirmed G2 blocker 1, G2 blocker 2 (blame `bbe706545`), G5 blocker 1 and G6 blocker 1. Every other finding is the verifier's static judgement, not yet re-checked.

### G1 — US-78 Specter (all items Completed)

Criteria: core combat rules MET (static); US-78 PARTIAL (no Return state, hybrid weapons impossible, no PlayMode/Host-Client tests); TASK-272 fade immunity and TASK-275 spawn frequency NOT MET at delivery, MET at HEAD only via TASK-285/289; TASK-274 destination validation and Hate List reset PARTIAL. `baabc056f` is a US-75 prefab repair, not G1.

- MAJOR — TASK-272 `SpecterAIController.cs:67-81`: `Spawned()` resets state to Reposo without the `HostMigrationRestoreUtility.IsRestoreSpawn` guard (pattern in `EnemyFSM.cs:44`); a dead Specter would revive with 0 HP after Host Migration.
- MAJOR — TASK-272/275 closed Completed with criteria unmet at delivery (fade immunity, spawn weight/threat cost); the tracker claims them.
- MAJOR — tests: `SpecterRulesAndCombatTests` `HybridDamage_*` is tautological; Hate List tests cover the `Func` overload the controller never calls; no test through `EnemyCharacter.ApplyDamage`; no RED recorded.
- MAJOR — TASK-274 teleport destination: no walkability or character-overlap check, deterministic angles, fallback unvalidated (`SpecterTeleportRules.cs:227-235,295`).
- MINOR — no Return/Home state, so leash breaks oscillate (`AI:254-259`); colliders toggled only on State Authority, loot collider found by name (`AI:139,570-591`); duplicate RedSlime in `Systems.prefab:93-94`; hot-path allocations; silent loot-init failure (`EnemyCharacter.cs:145-148`); Specter branches spread through `EnemyCharacter` and `EnemyFSMArchitecture.md` not updated.
- NIT — empty `[InitializeOnLoad]` in `AuthorSpecterPrefab.cs:17`; hand-made GUID; Coins in loot table not in spec; test file outside `Editor` until `0048b1892`.
- Not verifiable: proxy sync, Host Migration, physics layers, prefab/Inspector, real occlusion, drop rates, art approval, Game Design.

### G2 — TASK-285..291 (all items Completed)

Criteria: 285 PARTIAL (teleport validation, Home retreat); 286 PARTIAL (execution order); 287 and 291 word budgets NOT MET at HEAD (ceilings raised by FIX-39); 288 PARTIAL (provenance lost on backend hydration); 289 PARTIAL (fail-open filter, budgets 0); 290 NOT MET (CON_E_002), PARTIAL (dedup); 291 mostly MET.

- BLOCKER — TASK-290: CON_E_002 can never progress. `Mission_RankE_02.asset` requires `Catacombs_Main`, enemy `_zoneId` is empty (`NetworkEnemy.prefab:5863`), `MissionProgressEngine.cs:59-63` skips every kill; the test passes the zone by hand.
- BLOCKER — TASK-290, unrequested: `FusionSessionLauncher.cs:361-378` replaces backend-signed level/XP/watermark with higher local cache values; `TryCommitExtraction` uses `max(receipt, watermark+1)` against `LocalPlayerPersistenceArchitecture.md:399-402`; propagated at `NetworkSpawnManager.cs:3636-3644`. Breaks backend authority.
- MAJOR — TASK-288: ExpeditionId/Origin dropped on backend hydration, so the CON_E_006 claim fails after hydration; contradicts `LocalPlayerPersistenceArchitecture.md:166-172`, doc not updated for v12.
- MAJOR — TASK-287/291: word ceilings now 1474/1413 vs 1291/1230 in criteria and `ItemInstanceArchitecture.md:234`.
- MAJOR — TASK-286: aim decoupling depends on undefined LateUpdate order; decay test tautological.
- MAJOR — TASK-289: `DungeonEnvironmentResolver` fails open (null/"gameplay"/"*dungeon*" → catacombs) and `catch{}` returns an unrestricted candidate; integration tests re-implement the spawn loop; every CatacombsMap group has budget 0.
- MAJOR — TASK-290: dedup uses a fresh GUID per event, marks before commit, withdrawals inflate Obtención counters.
- MAJOR — TASK-285: no character/walkability check, no-target fallback searches around itself; LOS test models no wall.
- MAJOR — TASK-288: global static `RaidZoneTrigger.ActiveTriggers`, per-FixedUpdate overlaps, exploration credit untested.
- MINOR — 285 `AuthorSpecterPrefab` rewrite silently no-ops; collider by name; 287 `StripTestSpawners` test failed as written (fixed by Dani `008300818`) despite "4/4 PASS"; 286 PlayerPrefs test without cleanup; 288/290 unrequested DungeonPressure guards; 291 unrequested container cap; 289 `UnityEngine.Random` vs deterministic objective; tracker test counts inconsistent.
- Stage: all Completed although Editor/PlayMode/Release validation steps (285, 286, 287, 290) have no recorded evidence.
- Not verifiable: compile, all suites, word counts, PlayMode visuals, Release build, Host Migration, Game Design values.

### G3 — FIX-US-07 (all items in Testing)

Context: Dani's review `odd/tasks/review-findings-2026-11-15.md` (M1-M6) and later commits (`a0638e112`, `e610b845f`, `73e5d78be`, `2e6a76841`, `c2c4f1d37`, `fdc45375f`, `d62ddd04e`) rewrote much of FIX-40/41/42/43/44/96. Criteria: FIX-39 capacity MET, abandon PARTIAL; FIX-40 PARTIAL as delivered, migration re-binding NOT MET; FIX-41 PARTIAL; FIX-42 PARTIAL as delivered, MET at HEAD; FIX-43 avatarless closure MET, bounded wait NOT MET at HEAD; FIX-44 and FIX-96 MET at HEAD only after Dani's fixes.

- BLOCKER — FIX-40/43: the HacknPlan criteria (bounded closure wait) contradict `RaidGenerationLifecycleArchitecture.md:73-76` and `NetworkMatchController.cs:312-318` at HEAD (expiry ignored). Needs an owner decision.
- MAJOR — FIX-96: endpoint shipped without the required prior architecture approval; `ProgressionArchitecture.md` still lacks the defeat-commit contract.
- MAJOR — FIX-41 `LocalProfileRepository.cs:96-107,175`: after a transient main read failure the backup becomes the session and the next save overwrites newer main.
- MAJOR — FIX-40/43/44: delivered tests were source-text greps and reflection checks (green-hacking); leave/migration retention and despawn protection still untested.
- MAJOR — FIX-39 `RaidLootPanelView.cs:164-178`: runtime slot creation against `RaidInventoryUIArchitecture.md:180`, doc not updated to 38.
- MAJOR — FIX-39/FIX-US-07: "retorno garantizado a Town" conflicts with FIX-53 (stays Raiding); abandon PlayMode test missing.
- MAJOR — FIX-42: forced defeat destroys items; Game Design confirmation needed. FIX-40: migration re-binding not implemented.
- MAJOR — all G3: no tracker, no RED or manual multi-app evidence.
- MINOR — FIX-42 tautological `PlayerCorpseCompensationTests`, `LogAssert.ignoreFailingMessages`, seam outside the try; FIX-39 38-entry test bypasses the real conversion; FIX-40 unrelated exception swallow in `DungeonPressureController.cs:50-59`; FIX-43 avatarless defeat also runs with an avatar (`NetworkSpawnManager.cs:3812`); FIX-44 leftover helper `PlayerExtractionLootSaver.cs:1043`; `50bdc895e` weapon-set fallback out of scope.
- NIT — `ContainerLootPanel.prefab` +7387/−3115; duplicated block `PlayerCharacter.cs:518-530/559-571`.
- Not verifiable: 38-entry panel, Fusion word budget, Host/Client disconnect and migration, UI notices, live backend, Game Design 02/05/07, all test runs.

### G6 — FIX-US-11 (all items in Testing)

Criteria: FIX-79 MET (code), PARTIAL (tests); FIX-80 PARTIAL, decision missing; FIX-81 PARTIAL/NOT MET; FIX-82 MET (static); FIX-88 rules MET, spec update NOT MET; FIX-89 PARTIAL; FIX-90 PARTIAL/NOT MET (window rule); FIX-91 NOT MET; story NOT MET.

- BLOCKER — FIX-81: existing PlayMode tests contradict production. `ArcaneProjectileAbilityPlayModeTests.cs:66,160-171` still expect an unbounded projectile; `ProtectiveOrbsAbilityPlayModeTests.cs:223-240` expect pass-through that `ProtectiveOrbsNetworkEffect.cs:138-153` now intercepts. The "suite green" claim cannot hold.
- MAJOR — FIX-80/81/82/88/89/90: Game Design decisions HacknPlan required "before implementing" not recorded; Q9 (`DownedAndReviveArchitecture.md:634`) presents Tomás's choice as resolved.
- MAJOR — FIX-88/89/90: `DownedAndReviveArchitecture.md` §11.1 stale.
- MAJOR — FIX-91: Host Migration test tautological (`PvpCombatCreditPlayModeTests.cs:780`), "RealDartTrap" test synthetic (`:765`), no SpikesTrap path, no client parity, no counts.
- MAJOR — FIX-81: EnemyOnly projectiles now intercepted by enemy players' Orbs, unrequested; comment `NetworkProjectile.cs:238` contradicts `:240-243,481`.
- MAJOR — scope creep `5013e60e9`: ~320-line `AbilityPlayModeDebugger` (singleton, reflection, `FindObjectsByType` in `OnGUI`) plus `RpcSources.All` debug RPCs without input-authority check (`PlayerAbilityRuntimeNetworkController.cs:82-101,357-455`).
- MINOR — FIX-79 1 HP floor and no-knockback unasserted, test-only `AsPlayer` seam; FIX-82 `TrySetFriendlyFire` test-only, `_friendlyFireButton` unassigned in prefab, Ready not reset on change; FIX-89 DartTrap `_trajectoryCollider` unassigned, `_detectionCol` override of `BaseTrap.cs:96`; tracker cites backend 780/780 as Unity evidence.
- NIT — misnamed `ArcaneProjectileAbilityTests.cs:190`; 5th overlapping hazard overwrites slot 4 (`HazardExposureCausalityRules.cs:180-185`).
- Not verifiable: compile and test runs, prefab hierarchy, Host/Client replication and migration, Game Design Docs 05/11/18/19.

### G7 — FIX-US-12 (all items in Testing)

Criteria: FIX-83 main AC and threshold-0 MET, scope 2 (timer tied to total time) and agreed threshold semantics NOT MET; FIX-84 architecture MET, hook tests PARTIAL/NOT MET; FIX-85 MET (blend), revive bar/UI PARTIAL; FIX-86 MET (code), 5% threshold without decision. Commits stay in scope.

- MAJOR — FIX-84 `DungeonPressureInstabilityHooksTests.cs:122-133`: PvP-exclusion test only checks the method exists by reflection; removing the `IsPlayerEntity` guard (`DamageResolver.cs:471-474`) keeps it green.
- MAJOR — FIX-84 `Tests…:92-119,135-177`: saturation and Collapse rejection tested on a `FakeInstabilitySink` copying production logic.
- MAJOR — FIX-83 `DungeonPressureClock.cs:151,156`: advances still reset `remainingTicks`; with shipped values (240 s/60 s) the HUD can sit at 00:00 for 120 s before Collapse. Needs the Doc 20 §7 decision.
- MAJOR — FIX-83/84 in Testing with criteria unmet.
- MINOR — FIX-86 test writes the real PlayerPrefs key and leaves the static cache dirty; `InvalidateCacheForTests` unused; 5% without Game Design/Art sign-off, boundary untested. FIX-85 polling decision has no approver. Tracker claims EditMode suites passed but L6 records only compilation and backend 719. Warning checked via `logMessageReceived` instead of `LogAssert.Expect`.
- NIT — `ScreenDamageConfig.cs:10` range vs `:39` clamp; negative threshold untested; `DungeonPressureArchitecture.md` §6 mapping lacks FIX-83/84; `_suddenLossThreshold` missing from `ScreenDamageConfig.asset`.
- Not verifiable: all test runs, compile, Game Design Docs 03/13/20, HUD and visual checks, Host/Client hooks.

### G4 — FIX-US-09 (all items in Testing)

Criteria: FIX-63 MET at HEAD only after Dani's `026c4b203`; FIX-64 MET (service), PARTIAL (presenter); FIX-62, FIX-65 and FIX-US-09 PARTIAL.

- MAJOR — FIX-63 `tools/security/scan-secrets.js` @`9509d199b`: `scanGitHistory` swallowed spawn errors and non-zero exits (fail-open) and duplicated the parser, so `security.scan.test.js:255` never covered the real history path. Fixed later by `026c4b203` (Dani); don't credit the criterion to the original delivery.
- MAJOR — FIX-62 `docs/reporte_us61...md:105` vs `:9-11`: MongoDB decision self-attested by the auditor, claims both "descartada" and "rotada", no evidence of authorized rotation or login test, and the header says no rotation was done.
- MINOR — FIX-62 `:59,:110`: no `icacls` output recorded; OneDrive exclusion covers only Tomás's machine.
- MINOR — FIX-63/65 report header `:11` contradicts row `:111` (history scan not run vs. its result); no command output recorded.
- MINOR — FIX-64 `TownAbilityVendorPresenter.cs:273-291`: null-service path and error display untested.
- MINOR — FIX-64 `ability.purchase.integration.test.js`: concurrent tests don't assert a single debit or the 409 error code.
- MINOR — FIX-65: stale HMAC comment `InventoryClient.cs:296`; `RaidResultCanonicalizer.cs:20` only used by its test.
- MINOR — all G4: no ODD tracker or RED/GREEN evidence, while HacknPlan requires "No se cierra sin evidencia registrada".
- NIT — `2877fbfbe` has no effect (no node_modules in history); `:104` credits FIX-65 with TASK-223's work; `CommerceArchitecture.md:439-442` lacks fail-closed wording; `LocalProfileStore.TryPurchaseAbility` (`:340`) still public with no production caller.
- Not verifiable: Unity compile/EditMode, backend suites, real scanner output, Atlas rotation, ACL/OneDrive state.

### G5 — FIX-US-10 (all items in Testing)

Criteria: FIX-67 mostly MET; FIX-68 PARTIAL (scope 2 NOT MET); FIX-69 NOT MET (ObjectValue 0 items, visual archetypes) and PARTIAL (migration); FIX-74 MET but delivered by Dani's `a2f0f9cab`, not Tomás; FIX-75 MET (network tests), PARTIAL (audio); FIX-76 PARTIAL (random chests, None cooldown undecided); FIX-77 NOT MET (registry isolation, wall test); FIX-78 no code, decision not traceable.

- BLOCKER — FIX-77 `SmokeZoneRegistry.cs` + callers `VisionQuery.cs:79,124`, `EnemyMovementAIController.cs:610,724`, `SpecterAIController.cs:563`: production queries pass no runner; with 2+ runners smoke stops blocking LOS, with 1 it leaks to all; still static global state, `Clear(runner)` never called.
- BLOCKER — FIX-77 `ThrowableConsumablesPlayModeTests` `ThrowingDagger_WhenImpactingWall…`: only waits for despawn; lifetime/range despawn makes it pass without a wall.
- MAJOR — FIX-69 `Bone.asset`, `Coins.asset`: still `_objectValue: 0`, only labelled `Legacy` without a decision; `Catalog_HasNoItemsWithZeroObjectValueAndEmptyFamily` never checks ObjectValue.
- MAJOR — FIX-68 `MixedOrigin_WhenDungeonPrefixCannotFit_Rejects…` enshrines the reported defect; no Game Design decision.
- MAJOR — FIX-76 `PlayerConsumableNetworkController.cs:668-683`: unopened random chests (`Pending`) never highlighted; test uses fixed content.
- MAJOR — FIX-69 `LocalProfileSaveCodec.cs:1115-1124`: alias rewrite before duplicate check rejects old lists holding both IDs; no backend stored-inventory migration.
- MAJOR — FIX-69 scope 2 (provisional visual archetypes) not delivered: 13 and 11 definitions share icons.
- MAJOR — hygiene `6d4f3b867`: 9 items bundled with unrequested work (forced sorting layer `NetworkLootPickup.cs:624-630`, runtime-built lockpick bar with per-frame strings, ~280-line debug spawner); commit message lists tests not in the diff.
- MINOR — FIX-67 dead `_previousAmounts`/`_currentAmounts` (`QuickSlotController.cs:14-15,118,128-131`).
- MINOR — FIX-78 tracker claims RED→GREEN with no production change; `DownedAndReviveArchitecture.md` Q3 marked "Resolved" without a Game Design source.
- MINOR — FIX-75 audio test skips unresolved IDs, `Is.Not.Null` misses Unity fake-null; placeholder clips without Sound sign-off.
- MINOR — FIX-69 potion alias hard-coded in 4 places; FIX-77 `InteractionResult.InProgress` = `Success=true` + failure reason.
- NIT — FIX-74 credited to Tomás in commit refs. Context: Dani's `1380206db`/`e8239bcb6` later set `CON_HEALTH_POTION` as canonical, reversing Tomás's choice.
- Not verifiable: all test runs (tracker claims 780/780), audio playback, HUD visuals, client/proxy paths, Game Design Docs 07/13/14.

## Log

- L1 (2026-11-17): User request: "quiero hacer una auditoria de todas las tareas de Tomas desde el 15/11, tanto del sprint 17 como del board fixes", followed by "usando ODD como metodologia".
- L2 (2026-11-17): Decisions: scope "Todo lo de Tomás en S17 y Fixes"; depth "Criterios vs código + tests (Recomendado)"; deliverable "odd/tasks + resumen en chat (Recomendado)".
- L3 (2026-11-17): HacknPlan state: Sprint 17 board closed (2026-11-08 to 2026-11-15); every Sprint 17 item is Completed. Every Fixes item is in Testing. 48 items, snapshots written to `odd/evidence/audit-tomas-sprint17-fixes/`. All 29 Tomás commits listed above are merged into `New-Testing`.
- L4 (2026-11-17): T1-T7 returned (static only, no Unity/test runs). Parent spot-checks at HEAD: `Mission_RankE_02.asset:25` `ZoneId: Catacombs_Main` vs `NetworkEnemy.prefab:5863` `_zoneId:` empty; `FusionSessionLauncher.cs:365-378` max(local, ticket) progression, blamed to `bbe706545`; production `SmokeZoneRegistry.IsLineOccluded/CastOcclusion` calls without runner (`VisionQuery.cs:79,124`, `EnemyMovementAIController.cs:610,724`, `SpecterAIController.cs:563`); `ArcaneProjectileAbilityPlayModeTests.cs:66` asserts `IsUnbounded` true while `ArcaneProjectileAbilityBehaviour.cs:73` sends false. Verifiers left files outside the repo: `%TEMP%\a.txt`, `%TEMP%\b.txt` (G3), `/tmp/g6files.txt` (G6); not deleted pending user decision.

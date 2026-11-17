# TASK-294 — Charge Stun conditioned on effective damage

HacknPlan: Everaris #801 "TASK-294 — Condicionar Stun a la aplicación efectiva de daño en Embestida y aislar reglas en tests EditMode" (Sprint 18, In progress). Depends on TASK-284.
Execution tracker only: HacknPlan owns scope. Branch: `New-Testing` (no branch/commits: not authorized; index has unrelated unmerged font assets).

## Specs

Quoted verbatim from the HacknPlan item.

- S1 (Alcance 1): "Exponer el DamageResult devuelto por IDamageResolver.Resolve (mediante sobrecarga con parámetro out DamageResult o método dedicado), conservando intacta la firma y semántica actual de AbilityImpact.TryApply para no romper SeismicStrikeAbilityBehaviour ni otros llamadores."
- S2 (Alcance 2 / AC1): "En ChargeAbilityBehaviour.ApplyImpact, invocar EnemyStunNetworkEffect.TryApply únicamente si el impacto produjo daño efectivo (DamageResult.IsApplied && DamageResult.AppliedDamage > 0f)." / "El Stun se aplica únicamente tras un impacto que inflija daño efectivo (DamageResult.IsApplied && DamageResult.AppliedDamage > 0)."
- S3 (Alcance 2 / AC2): "Definir intencionalmente que un golpe de solo empuje (daño base 0 con knockback) o daño mitigado/bloqueado a 0 no produce aturdimiento." / "Si el daño es mitigado a 0, rechazado por el resolver, bloqueado por friendly fire, el objetivo es inmune o el ataque es un empuje sin daño (damage = 0), no se aplica ningún Stun."
- S4 (Alcance 3 / AC4): "Extraer las reglas de decisión de Stun a una clase pura desacoplada del ciclo de vida de Unity y cubrirla con tests EditMode exhaustivos." / "Las reglas de condicionamiento de Stun cuentan con cobertura de tests EditMode puros."
- S5 (AC3): "Golpe Sísmico (SeismicStrikeAbilityBehaviour) y los tests de AbilityImpact conservan su comportamiento existente sin alteraciones."
- S6 (Validación): "Suite de tests EditMode de reglas de Stun con 100% de aprobación." / "Tests de regresión verificando que SeismicStrikeAbilityBehaviour procese múltiples objetivos sin cortes espurios." / "Test de integración en PlayMode verificando la ausencia de aturdimiento ante daño bloqueado o mitigado a 0."
- S7 (user, L7): "corregi lo de empower" / "en esta misma branch". The Empower test must pass for the right reason: its legacy `AbilityImpact.TryApply` check (S5) has to run. Root-cause fix, no green-hacking.
- Out of scope: "Modificación del comportamiento o flujo de SeismicStrikeAbilityBehaviour u otras habilidades fuera de Embestida." / "Animaciones o VFX nuevos de aturdimiento."

## Tasks

- [x] T1 — S4, S2, S3: pure `ChargeStunRules` + EditMode `ChargeStunRulesTests` (inline, TDD). Commit: — (not authorized)
- [x] T2 — S1, S5: `AbilityImpact.TryApply(..., out DamageResult)` overload; legacy overload delegates; EditMode tests incl. legacy behavior (inline, TDD). Commit: — (not authorized)
- [x] T3 — S2, S3: wire `ChargeAbilityBehaviour.ApplyImpact` to the overload + rule; PlayMode zero-damage push does not stun (worker, TDD). Commit: — (not authorized)
- [x] T4 — S6: PlayMode mitigated-to-0 (Specter incorporeal + Physical) no-Stun integration test; Seismic multi-target regression check. Commit: — (not authorized)
- [x] T6 — S7: diagnose and fix `SupportAbilitiesPlayModeTests.Empower_CombatControllerMelee_ScalesWeaponDamageButNotDirectAbilityImpact` (melee hit recorded with Amount 5 but target health unchanged 175 vs 170 at line 252) at its root cause, on `New-Testing`. Commit: —
- [x] T5 — S5, S6: Unity compile + EditMode + Charge/Seismic/SupportAbilities PlayMode runs; diff review (L6, L8). Commit: —

## Log

- L1 (user, 2026-11-17): "revisa TASK-294 — Condicionar Stun a la aplicación efectiva de daño en Embestida y aislar reglas en tests EditMode y empeza a implementarlo siguiendo ODD"
- L2 (evidence): resolver outcomes — friendly fire: false/0/FriendlyFireBlocked (`DamageResolver.cs:126`); CanReceiveDamage false: false/0/TargetUnavailable (`DamageResolver.cs:137`); zero amount + knockback: false/0/InvalidAmount, knockback still applied (`CharacterBase.cs:140`, `DamageResolver.cs:160`); mitigated to 0: **true/0/None** (`CharacterBase.cs:144-193`), real case Specter incorporeal vs Physical (`SpecterRules.cs:91`). Current Charge stuns on zero-damage push and on true/0 results (`ChargeAbilityBehaviour.cs:169-173`).
- L4 (incident, 2026-11-17 12:48): GitHub Desktop stash `stash@{0}` (On New-Testing) swept AbilityImpact.cs, both EditMode test files and this tracker together with unrelated work. User chose "Restaurar solo mis archivos de TASK-294": restored worktree-only via `git restore --source=stash@{0}`; stash left intact, index untouched.
- L5 (evidence): RED observed for T1/T2 (compile: CS0103 `ChargeStunRules` missing, CS1501 no 12-arg `TryApply`). Existing `ChargeRulesTests` 30/30 and `SeismicStrikeRulesTests` 23/23 passed after GREEN code; new suites not yet run (pipeline reload + stash).
- L6 (evidence, worker): recompile 0 errors. EditMode: ChargeStunRulesTests 23/23, AbilityImpactTests 9/9, ChargeRulesTests 30/30, SeismicStrikeRulesTests 23/23. PlayMode RED: ChargeAbilityPlayModeTests 12/14, both new tests failed on the stun assertion ("Expected: False But was: True"). GREEN: ChargeAbilityPlayModeTests 14/14, SeismicStrike PlayMode 13/13, SupportAbilitiesPlayModeTests 15/16: Empower test fails at line 252 (first melee hit, 170 expected vs 175) before the AbilityImpact call at line 266. AbilityImpact production callers are only Charge and Seismic, so the failure is outside this diff by caller analysis; not re-run on base.
- L7 (user): "corregi lo de empower" then "en esta misma branch".
- L8 (evidence, worker): root cause of the Empower failure was a stale fixture, not a production regression. Commit `5013e60e9` ("fix(combat): resolve Sprint 15 combat rules and PvP credit attribution", FIX-79 / Doc 19) makes an unresolved initial affiliation count as ally, so friendly fire off returns FriendlyFireBlocked. The test has no roster. Fix: `resolver.AreInitialTeammates = (_, _) => false;` (same seam `PlayerDownedPlayModeTests.cs:38` uses). RED: 170 expected, 175 actual. GREEN: Empower 1/1, SupportAbilitiesPlayModeTests 16/16, ChargeAbilityPlayModeTests 14/14. Production code unchanged since the L6 EditMode and Seismic runs.
- L3 (decision): rule keeps the existing guards (stun seconds > 0 and finite, target alive after impact) and adds `IsApplied && AppliedDamage > 0` (finite), mirroring `VitalDrainRules.AddConfirmedDamage`.

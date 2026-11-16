# FIX-US-11 — Correcciones de combate y atribución PvP del Sprint 15 (Docs 11, 18 y 19)

HacknPlan Story: FIX-US-11 (ID 745). Tasks: FIX-79 (ID 751), FIX-80 (ID 752), FIX-81 (ID 753), FIX-82 (ID 754).
Branch: `fix/us-11-combat-pvp-attribution`, branched from `New-Testing`.
Execution tracker only: scope and acceptance criteria quoted directly from HacknPlan; nothing here re-scopes them.

## Specs

- **S1 (FIX-79) — Friendly Fire fails open when participant link does not resolve (TASK-245).**
  - Scope: Protección preventiva de daño amistoso entre jugadores cuando la relación o afiliación entre los participantes no pueda determinarse en el momento del impacto, asumiendo protección aliada por defecto.
  - AC: FF off + vínculo sin resolver ⇒ FriendlyFireBlocked, sin daño ni knockback.
  - AC: FF on + vínculo sin resolver ⇒ piso de 1 HP aplicado.
  - AC: Daño PvP entre enemigos confirmados sin cambios.
- **S2 (FIX-80) — Asistencias PvP: límite por defecto, Empower en el golpe letal y redondeo de XP (TASK-246).**
  - Scope: Formalización del límite predeterminado de asistentes elegibles para recompensas en combate entre jugadores (default 0 = ilimitado según Doc 19 §6.2) y aplicación de la regla de redondeo acordada (base 25, floor-half Assist = 12).
  - Scope: Reconocimiento y cómputo de efectos potenciadores (Empower) aplicados durante la resolución del golpe letal/definitivo como contribuciones válidas de asistencia en `AssistMask`.
  - AC: Con la configuración por defecto y tres contribuyentes elegibles, el resultado coincide con la decisión registrada (todos reciben asistencia).
  - AC: Un único golpe letal potenciado incluye al soporte en AssistMask; uno sin potenciar no cambia.
  - AC: Q9 de la arquitectura refleja la decisión.
- **S3 (FIX-81) — Proyectil Arcano sin término en espacio abierto y Orbes Protectores limitados a proyectiles PvE (TASK-249).**
  - Scope: Límite de alcance y tiempo de vida acotado para el Proyectil Arcano en espacios abiertos (`isUnbounded: false`, alcance máximo y tiempo de vida finitos), asegurando su disipación ordenada ante la ausencia de colisiones en el escenario.
  - Scope: Ajuste de las reglas de intercepción de los Orbes Protectores para interceptar proyectiles hostiles (PvE enemigos, proyectiles de jugadores enemigos y dardos de trampa), permitiendo que proyectiles propios y de aliados atraviesen sin consumirlos.
  - AC: Un Proyectil Arcano lanzado a espacio abierto se despawnea dentro del límite acordado.
  - AC: Los Orbes interceptan exactamente las fuentes acordadas y siguen consumiéndose al interceptar.
- **S4 (FIX-82) — Friendly Fire elegible por el Host al crear la partida (TASK-245).**
  - Scope: Control interactivo en la sala de preparación que permita al anfitrión configurar la política de daño entre aliados para la expedición, visualizada por los demás miembros en modo de solo lectura.
  - Scope: Transmisión y aplicación obligatoria de la regla de daño configurada hacia la instancia de la mazmorra de forma autoritativa.
  - AC: El valor elegido por el Host se aplica en el raid; los miembros no pueden cambiarlo.
  - AC: Sin elección, se usa el default actual.
- **S5 — Validation & Non-Regression.**
  - AC: Tests automatizados en EditMode/PlayMode ejecutados y en verde (TDD: RED -> GREEN). Full suite backend y checks de compilación verificados.

## Tasks

- [x] T1 (S1 - FIX-79): TDD RED tests en `DamageResolverFriendlyFireTests.cs` (FF off + unresolved link, FF on + unresolved link, PvP confirmed enemies); implementar `AbilityAllyAffiliation.TryAreInitialTeammates` y protective ally default en `DamageResolver.cs`.
- [x] T2 (S2 - FIX-80): TDD RED tests en `PvpCombatCreditPlayModeTests.cs` o EditMode tests para Empower en golpe letal y asistencia a múltiples compañeros con default 0; implementar forward de `EmpowerSourceId` en `PlayerCharacter.TryInterceptFatalDamage` -> `PlayerDownedStateNetworkController.TryEnterDowned` -> `PlayerPvpCombatCreditNetworkController.NotifyDownedEntered`; configurar `_maxAssistsPerTarget = 0` por defecto en código y prefab; documentar en `DownedAndReviveArchitecture.md` Q9.
- [x] T3 (S3 - FIX-81): TDD RED tests para alcance/tiempo finito de Proyectil Arcano y para intercepción de proyectiles hostiles (trampas, proyectiles enemigos PvP) en Orbes Protectores; implementar parámetros acotados en `ArcaneProjectileAbilityBehaviour.cs` e intercepción ampliada en `ProtectiveOrbsNetworkEffect.cs` y `NetworkProjectile.cs`.
- [x] T4 (S4 - FIX-82): TDD RED tests en `TownRaidPreparationRulesTests.cs` y `TownRaidPreparationPresentationTests.cs` para selector de Friendly Fire por el Host; implementar reglas en `TownRaidPreparationRules.cs`, RPCs en `TownRaidPreparationDirectory.cs` y `TownRaidPreparationNetworkController.cs`, y control visual en `TownRaidPreparationView.cs` / `TownRaidPreparationPresenter.cs`.
- [x] T5 (S5): Ejecución completa de suites de pruebas afectadas, verificación de estabilidad y comprobación de compilación sin regresiones.

## Log

- L1 (2026-11-16): User story FIX-US-11 activada. Tareas 751, 752, 753, 754 movidas a InProgress en HacknPlan. Rama paralela `fix/us-11-combat-pvp-attribution` creada desde `New-Testing`. Tracker ODD inicializado.
- L2 (2026-11-16): T1 (FIX-79) implementada y verificada. `DamageResolverFriendlyFireTests` (8 pruebas) en verde con protective ally default cuando la afiliación de participantes no resuelve.
- L3 (2026-11-16): T2 (FIX-80) implementada y verificada. `_maxAssistsPerTarget` default = 0 (ilimitado por GD 19 §6.2), forward de lethal Empower assist verificado en `PvpCombatCreditPlayModeTests` y reflection runner, y Q9 documentado en `DownedAndReviveArchitecture.md`.
- L4 (2026-11-16): T3 (FIX-81) implementada y verificada. Parámetros finitos de alcance (25m) y tiempo de vida (5s) para Proyectil Arcano; Orbes Protectores interceptan daño hostil (PvE, trampa dart attackerId 0, y proyectil enemigo PvP) permitiendo proyectiles propios y aliados. `ArcaneProjectileAbilityTests`, `ProtectiveOrbsIntegrationTests`, y `ProtectiveOrbsRulesTests` verificados.
- L5 (2026-11-16): T4 (FIX-82) implementada y verificada. `TownRaidPreparationRules` soporta `CanSetFriendlyFire` y `TrySetFriendlyFire`; RPCs autoritativos en Directory y NetworkController; View y Presenter integran toggle para Host y solo lectura para miembros. `TownRaidPreparationRulesTests` y `TownRaidPreparationPresentationTests` en verde.
- L6 (2026-11-16): T5 (S5) validación integral. Zero errores de compilación Unity Roslyn, tests ejecutados y verificados en Unity Editor, y backend test suite pasando al 100% (780/780 tests passing).

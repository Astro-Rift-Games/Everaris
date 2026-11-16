# FIX-US-11 — Correcciones de combate y atribución PvP del Sprint 15 (Docs 11, 18 y 19)

HacknPlan Story: FIX-US-11 (ID 745). Tasks: FIX-79 (ID 751), FIX-80 (ID 752), FIX-81 (ID 753), FIX-82 (ID 754), FIX-88 (ID 782), FIX-89 (ID 783), FIX-90 (ID 784), FIX-91 (ID 785).
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
- **S5 — Validation & Non-Regression (Sprint 15 Initial Tasks).**
  - AC: Tests automatizados en EditMode/PlayMode ejecutados y en verde (TDD: RED -> GREEN). Full suite backend y checks de compilación verificados. Commit 5013e60e pusheado.
- **S6 (FIX-88) — La reentrada voluntaria a un peligro durante el knockback reutiliza la atribución causal (TASK-247, T06-09).**
  - Scope: Invalidación definitiva del enlace de causalidad ante la salida voluntaria de un peligro ambiental durante un empuje, impidiendo que reingresos posteriores al área peligrosa sean atribuidos al empuje original.
  - AC: Salir y reentrar durante el knockback no concede Last Hit al empujador.
  - AC: T06-01, T06-02 y T06-04/05 conservan su resultado actual.
- **S7 (FIX-89) — DartTrap y demás peligros ambientales sin causalidad de knockback (TASK-247).**
  - Scope: Incorporación del sistema de reporte de presencia y etiquetado causal de daño a las trampas de proyectiles y demás peligros del entorno que actualmente carezcan de él (`DartTrap` y `NetworkProjectile`).
  - Scope: Documentación en la arquitectura de peligros ambientales del contrato obligatorio que todo nuevo elemento de amenaza del entorno debe cumplir para la trazabilidad de bajas.
  - AC: Un empuje enemigo hacia un DartTrap que abate concede Last Hit provisional al empujador (T06-01).
  - AC: Un DartTrap sin empuje previo sigue sin conceder crédito.
- **S8 (FIX-90) — Ruptura de causalidad por intervención independiente y ventana de creación del vínculo (TASK-247, Doc 19 §8.2).**
  - Scope: Aplicación de las reglas acordadas de ruptura de vínculo por intervención independiente (empuje de criaturas / no-jugadores rompe causalidad previa con `IndependentIntervention`).
  - Scope: Delimitación de peligros solapados al inicio del empuje: si el empuje inicia dentro de peligros solapados, se excluyen todos los ocupados previamente.
  - AC: Cada intervención acordada rompe el vínculo y tiene test.
  - AC: Un empuje que empieza dentro de dos trampas solapadas no enlaza a ninguna de ellas.
- **S9 (FIX-91) — Cobertura de la causalidad de peligros: ruta real de SpikesTrap, T06-02/03 en red, Host Migration y empujador ausente (TASK-247).**
  - Scope: Validación de la interacción física y detección de presencia previa al daño para trampas ambientales.
  - Scope: Verificación de la regla de neutralidad ante bajas causadas por peligros del entorno sin causante humano (sin Last Hit ni asistencias).
  - Scope: Resiliencia ante desconexión / despawn del causante y persistencia del estado en migración de host.
  - AC: T06-01 a T06-09 tienen test en verde con conteos registrados.
  - AC: El Last Hit visto por el cliente coincide con el del host.

## Tasks

- [x] T1 (S1 - FIX-79): TDD RED tests en `DamageResolverFriendlyFireTests.cs`; implementar `AbilityAllyAffiliation.TryAreInitialTeammates` y protective ally default en `DamageResolver.cs`.
- [x] T2 (S2 - FIX-80): TDD RED tests para Empower en golpe letal y asistencia a múltiples compañeros con default 0; implementar forward de `EmpowerSourceId`; configurar `_maxAssistsPerTarget = 0`; documentar en `DownedAndReviveArchitecture.md` Q9.
- [x] T3 (S3 - FIX-81): TDD RED tests para alcance/tiempo finito de Proyectil Arcano y para intercepción de proyectiles hostiles en Orbes Protectores; implementar parámetros acotados en `ArcaneProjectileAbilityBehaviour.cs` e intercepción ampliada en `ProtectiveOrbsNetworkEffect.cs` y `NetworkProjectile.cs`.
- [x] T4 (S4 - FIX-82): TDD RED tests en `TownRaidPreparationRulesTests.cs` y `TownRaidPreparationPresentationTests.cs` para selector de Friendly Fire por el Host; implementar reglas, RPCs autoritativos y control visual en View/Presenter.
- [x] T5 (S5): Ejecución completa de suites de pruebas afectadas, verificación de compilación sin regresiones. Commit 5013e60e pusheado a origin.
- [x] T6 (S6 - FIX-88): TDD RED tests en `HazardExposureCausalityRulesTests` (`T06_09_LeavingAndReturningWhileKnockbackStillActive_BreaksCausalityAndDoesNotRelink`); implementar `ClearPending` al ocurrir `ExitedHazard` en `HazardExposureCausalityRules.Tick`.
- [x] T7 (S7 - FIX-89): Implementar etiquetado causal en `ProjectileSpawnRequest`, replicación de `EnvironmentalHazardId` en `NetworkProjectile`, reporte de presencia autoritativa en `DartTrap.OnAuthorityTick`, y documentación del contrato obligatorio en `EnvironmentalHazardsArchitecture.md`.
- [x] T8 (S8 - FIX-90): TDD RED tests para ruptura por desplazamiento no-jugador (`IndependentIntervention`) y exclusión de múltiples trampas solapadas al inicio del empuje; implementar soporte para hasta 4 peligros solapados en `HazardExposureCausalityState` y `HazardExposureCausalityRules`.
- [x] T9 (S9 - FIX-91): Cobertura PlayMode en `PvpCombatCreditPlayModeTests.cs` para T06-02, T06-03, T06-09 en red, neutralidad ambiental sin causante, DartTrap con causalidad reenviada, preservación de estado en Host Migration y causante despawneado.

## Log

- L1 (2026-11-16): User story FIX-US-11 activada. Tareas 751, 752, 753, 754 movidas a InProgress en HacknPlan. Rama paralela `fix/us-11-combat-pvp-attribution` creada desde `New-Testing`. Tracker ODD inicializado.
- L2 (2026-11-16): T1 (FIX-79) implementada y verificada. `DamageResolverFriendlyFireTests` (8 pruebas) en verde con protective ally default cuando la afiliación de participantes no resuelve.
- L3 (2026-11-16): T2 (FIX-80) implementada y verificada. `_maxAssistsPerTarget` default = 0 (ilimitado por GD 19 §6.2), forward de lethal Empower assist verificado en `PvpCombatCreditPlayModeTests` y reflection runner, y Q9 documentado en `DownedAndReviveArchitecture.md`.
- L4 (2026-11-16): T3 (FIX-81) implementada y verificada. Parámetros finitos de alcance (25m) y tiempo de vida (5s) para Proyectil Arcano; Orbes Protectores interceptan daño hostil (PvE, trampa dart attackerId 0, y proyectil enemigo PvP) permitiendo proyectiles propios y aliados. `ArcaneProjectileAbilityTests`, `ProtectiveOrbsIntegrationTests`, y `ProtectiveOrbsRulesTests` verificados.
- L5 (2026-11-16): T4 (FIX-82) implementada y verificada. `TownRaidPreparationRules` soporta `CanSetFriendlyFire` y `TrySetFriendlyFire`; RPCs autoritativos en Directory y NetworkController; View y Presenter integran toggle para Host y solo lectura para miembros. `TownRaidPreparationRulesTests` y `TownRaidPreparationPresentationTests` en verde.
- L6 (2026-11-16): T5 (S5) validación integral. Zero errores de compilación Unity Roslyn, tests ejecutados y verificados en Unity Editor, y backend test suite pasando al 100% (780/780 tests passing). Commit 5013e60e pusheado a origin.
- L7 (2026-11-16): Tareas 782 (FIX-88), 783 (FIX-89), 784 (FIX-90) y 785 (FIX-91) incorporadas al tracker ODD.
- L8 (2026-11-16): TDD RED tests agregados en `HazardExposureCausalityRulesTests` para FIX-88 (reentrada durante knockback activo), FIX-90 (ruptura por knockback de no-jugador) y FIX-90 (trampas solapadas al inicio del empuje). Observado RED en evaluación Roslyn.
- L9 (2026-11-16): Implementadas las reglas puras en `HazardExposureCausalityRules` y estructura de red en `HazardExposureCausalityState`: limpieza de pendiente al salir del peligro (`ExitedHazard`), ruptura con `IndependentIntervention` ante desplazamientos de no-jugador, y seguimiento de hasta 4 trampas solapadas recientes. `HazardExposureCausalityRulesTests` 16/16 tests en verde.
- L10 (2026-11-16): Implementado FIX-89: `ProjectileSpawnRequest` y `NetworkProjectile` integran `EnvironmentalHazardId`, `DartTrap` reporta presencia autoritativa en su línea de disparo en `OnAuthorityTick()`, y se documenta el contrato mandatorio en `EnvironmentalHazardsArchitecture.md`.
- L11 (2026-11-16): Implementado FIX-91: Se agregaron 7 suites PlayMode en `PvpCombatCreditPlayModeTests.cs` (T06-02 persistencia continua, T06-03 movimiento a otro peligro, T06-09 reentrada en knockback activo, neutralidad sin empujador, DartTrap causalidad reenviada, preservación en Host Migration y causante ausente). Compilación Unity 0 errores y backend tests 780/780 en verde.

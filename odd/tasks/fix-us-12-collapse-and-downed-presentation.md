# FIX-US-12 — Progresión del Colapso y presentación de salud/Abatido del Sprint 15 (Docs 03, 13 y 20)

HacknPlan Story: FIX-US-12 (ID 746). Tasks: FIX-83 (ID 768), FIX-84 (ID 769), FIX-85 (ID 770), FIX-86 (ID 771).
Branch: `fix/us-12-collapse-downed-presentation`, branched from `New-Testing`.
Execution tracker only: scope and acceptance criteria quoted directly from HacknPlan; nothing here re-scopes them.

## Specs

- **S1 (FIX-83) — Minimum Collapse Start (08:00) on Time Path & Threshold Validation.**
  - Scope: Garantizar que la fase E3 (Colapso) nunca comience antes del minuto mínimo configurado (08:00 = 480 s) por ninguna vía (inestabilidad o tiempo transcurrido). Si los avances de inestabilidad recortan el temporizador de fase a cero antes de los 480 s, la fase previa (`CriticalPressure`) permanece con el temporizador congelado en cero hasta cumplir el plazo mínimo.
  - Scope: Rechazar en `DungeonPressureConfig.Validate` umbrales de inestabilidad <= 0 que desactiven en silencio la progresión. Preservar el cierre absoluto de la mazmorra en 1020 s (17:00).
  - AC: Con avances por inestabilidad a 180 s y 300 s, la fase sigue en `CriticalPressure` a 420 s y entra a `Collapse` recién a >= 480 s; el cierre sigue en 1020 s. Un umbral 0 es rechazado por validación.
- **S2 (FIX-84) — Instability Hooks Coverage & Architecture Documentation.**
  - Scope: Cobertura automatizada completa de las fuentes de inestabilidad de la mazmorra: puntos por eliminación de enemigos PvE por tier y origen (Ordinario, Élite, Boss, Refuerzo), exclusión estricta de bajas PvP (`IsPlayerEntity`), bonificaciones por primer hallazgo de tesoros mayores (Conventional, Divine, Cursed, Chaos), y saturación al umbral de fase. Warning explícito ante familia de tesoro desconocida.
  - Scope: Documentación formal en `DungeonPressureArchitecture.md` del modelo de progresión híbrida (tiempo e inestabilidad), umbrales E0/E1/E2, mínimo de inicio de E3 a 08:00 y cierre absoluto.
  - AC: Cada regla listada tiene un test en verde que falla si se altera. La arquitectura describe el contrato vigente.
- **S3 (FIX-85) — Downed Re-entry During Post-Process Fade-Out & Polling Architecture Decision.**
  - Scope: Reinicio limpio de reserva y progreso de reanimación al reingresar a Abatido durante el fade-out de post-proceso B/N, impidiendo arrastrar valores del ciclo anterior. Detección de ciclo nuevo basada en flanco `isDowned && (!_wasDowned || _transition <= 0f)`.
  - Scope: Registro de la decisión arquitectónica en `DownedAndReviveArchitecture.md` confirmando el uso de polling de sólo lectura en `LocalPlayerDownedPostProcessPresenter` conforme a §9.1 para evaluar suavemente la interpolación visual por frame sin acoplar eventos de simulación.
  - AC: Nuevo test en `DownedPostProcessBlendTests` pasa reingresando a Abatido a medio fade-out y los existentes siguen en verde. Decisión polling/eventos documentada.
- **S4 (FIX-86) — Screen Damage Sudden Loss Threshold & Cached Accessibility Reading.**
  - Scope: Definir un umbral de pérdida súbita de salud (`SuddenLossThreshold`, default 0.05f = 5% de la salud máxima) en `ScreenDamageConfig` para disparar el hit flash, evitando que daño gradual o mínimo active el destello visual de impacto crítico.
  - Scope: Cachear el valor de intensidad en `ScreenDamageAccessibility` para eliminar lecturas redundantes a `PlayerPrefs.GetFloat` en cada frame (`Update`).
  - AC: Una pérdida bajo el umbral no dispara el flash; una sobre el umbral sí. Cambiar la opción de accesibilidad se refleja sin lectura por frame.
- **S5 — Validation & Non-Regression.**
  - AC: Tests automatizados en EditMode ejecutados y en verde (TDD: RED -> GREEN). Full suite backend y checks de compilación verificados.

## Tasks

- [x] T1 (S1 - FIX-83): TDD RED test en `DungeonPressureHybridProgressionTests.cs` (avances por inestabilidad a 180s y 300s llevando tiempo a 420s con `remainingTicks == 0`); implementar retención en `CriticalPressure` hasta `totalElapsedSecs >= minCollapseStartElapsedSecs` en `DungeonPressureClock.cs`; validar umbrales > 0 en `DungeonPressureConfig.cs`. Evidence: RED observado ("Expected: CriticalPressure But was: Collapse"); GREEN tras el cambio.
- [x] T2 (S2 - FIX-84): TDD tests en EditMode (`DungeonPressureInstabilityHooksTests.cs`) cubriendo puntos de enemigos, exclusión PvP, puntos de tesoros por familia, saturación y descarte de overflow; warning ante familia desconocida; documentar progresión híbrida en `DungeonPressureArchitecture.md`. Evidence: 6/6 tests en verde.
- [x] T3 (S3 - FIX-85): TDD RED test en `DownedPostProcessBlendTests.cs` reentrando a Abatido a medio fade-out; implementar `_wasDowned` y reset de `_reserve` y `_revive` en `DownedPostProcessBlend.cs`; registrar decisión de polling en `DownedAndReviveArchitecture.md`. Evidence: RED observado ("Expected: 0.25f But was: 0.2722f"); GREEN tras el fix. 14/14 tests en verde.
- [x] T4 (S4 - FIX-86): TDD RED tests en `ScreenDamageTests.cs` (chip damage vs sudden loss y caching de `ScreenDamageAccessibility`); implementar `SuddenLossThreshold` en `ScreenDamageConfig.cs`, cálculo de fracción de pérdida en `ScreenDamageFeedbackState.cs`, y caché en `ScreenDamageAccessibility.cs`. Evidence: RED observado para chip damage y PlayerPrefs; GREEN tras el fix. 13/13 tests en verde.
- [x] T5 (S5): Ejecución completa de suites EditMode afectadas en Unity y backend suites (719 tests en 32 suites en verde); verificación de diff limpia.

## Log

- L1 (2026-11-14): User story FIX-US-12 activada. Tareas 768, 769, 770, 771 movidas a InProgress en HacknPlan. Rama paralela `fix/us-12-collapse-downed-presentation` creada a partir de `New-Testing`. Tracker ODD inicializado.
- L2 (2026-11-14): T1 ejecutado con TDD. `Step_InstabilityAdvances_CollapseHeldUntilEightMinutes_AndClosesAt17Min` falló en RED a los 420s y pasó a GREEN con la retención en `CriticalPressure`. Validación de umbrales inestabilidad > 0 implementada en `DungeonPressureConfig.cs`.
- L3 (2026-11-14): T2 ejecutado con TDD. Creada suite `DungeonPressureInstabilityHooksTests.cs` (6 tests EditMode) y documentada la sección 5.1 de Progresión Híbrida en `DungeonPressureArchitecture.md`.
- L4 (2026-11-14): T3 ejecutado con TDD. `Step_NewDownedCycleDuringFadeOut_StartsFromItsOwnReserveAndCleansReviveColor` falló en RED por arrastrar reserva/vignette previo y pasó a GREEN con la detección `_wasDowned`. Documentada decisión arquitectónica de polling en `DownedAndReviveArchitecture.md §9.1`.
- L5 (2026-11-14): T4 ejecutado con TDD. Tests de pérdida súbita (chip damage vs >= 5% max HP) y caching en memoria de `ScreenDamageAccessibility` observados en RED y llevados a GREEN. Suites `ScreenDamageTests` (13) y `ScreenDamagePeripheralTests` (6) en verde.
- L6 (2026-11-14): T5 validación integral. Unity compiló sin errores ni warnings. 719 tests del backend en verde (32 suites).
- L7 (2026-11-15): Autorización explícita recibida para commit, push y merge a New-Testing. Tracker cerrado.

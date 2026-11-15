# FIX-14 — Readable transition failure messages, Failed state and return to Town

HacknPlan FIX-14 (ID 681), parent FIX-US-03 (ID 667). Branch `fix/fix-14-transition-failure-messages` from `New-Testing` (`e99a16e5`). Prerequisite of FIX-55 (ID 735), whose "el jugador ve el motivo" relies on this catalog.
Execution tracker only: scope and acceptance come from the HacknPlan item; nothing here re-scopes it.

## Specs

Quoted verbatim from HacknPlan #681.

- **S1** (Alcance 1): "Catálogo unificado de mensajes legibles para el jugador ante fallos de transición entre Town y Raid o errores de recuperación, eliminando identificadores técnicos y distinguiendo fallos de servicio." Frontera: "servicio de traducción de errores y presentación; entregables observables: mensajes comprensibles en español en la interfaz."
- **S2** (Alcance 2): "Consumo continuo de resultados de transición fuera de la vinculación inicial de la vista y mensajes explicativos en el menú de raid ante fallos de cierre, recuperación o abandono." Frontera: "menús de pausa y raid; entregables observables: retroalimentación oportuna en pantalla ante cualquier interrupción."
- **S3** (Alcance 3): "Indicación explícita para la autoridad de la partida cuando su retorno a Town esté demorado esperando la salida del resto de participantes." Frontera: "interfaz de retorno de la raid; entregables observables: aviso informativo de espera de miembros."
- **S4** (AC1): "Ningún SessionTransitionResult de fallo se muestra como nombre de enum."
- **S5** (AC2): "Cada fallo de retorno o abandono muestra un mensaje en el menú de raid."
- **S6** (AC3): "El Host con retorno pendiente ve el motivo."
- **S7** (Validación): "Test de presenter/vista (EditMode o PlayMode) por cada mensaje: RED observado antes y GREEN después." / "Revisión manual multi-aplicación (Host + Cliente) de cada mensaje con capturas." / "Textos en español; ninguno muestra nombres internos de enums."
- Out of scope (verbatim): "Cambios en la lógica de retorno."
- GD 01 §13 (live, verbatim): "Una falla general del servicio multijugador debe comunicarse como problema del servicio cuando pueda distinguirse de una falla local. Los detalles de detección, timeout y recuperación pertenecen a Networking." GD 02 §7.2/§8 define no wording.

## Tasks

| ID | Specs | Route | Commit |
| --- | --- | --- | --- |
| T1 | S1, S4, S7 | inline, TDD: pure Spanish catalog for every failure `SessionTransitionResult` (incl. `ServiceUnavailable`, L4); `TownRaidPreparationView.ShowTransitionFailure` uses it — done (manual part in T6) | T1 commit |
| T2 | S1, S7 | inline, TDD: `SessionTransitionResult.ServiceUnavailable` published when the Fusion start fails for a cloud/network cause (L4) | — |
| T3 | S2, S7 | inline, TDD: Town preparation presenter consumes transition failures continuously, not only on `Bind()` | — |
| T4 | S2, S5, S7 | inline, TDD: raid menu shows each return/abandon failure and re-enables Return (presentation only, L4); Host results-return failure consumable; remove dead `AbandonRaidAsync` (L4) | — |
| T5 | S3, S6, S7 | inline, TDD: read-only coordinator getter for the pending Host return and raid menu waiting notice with remaining count (L4) | — |
| T6 | S7 | human: manual Host + Client review of every message with screenshots | — |

## Log

- **L1** (user, 2026-11-14): "revisa FIX-55 — Lanzamiento condicionado a la reserva confirmada en el backend (asimetría Solo/Duo) y empieza la implementacion siguiendo ODD"
- **L2** (user, 2026-11-14, FIX-55 decisions): player reason: "empecemos por fix-14 entonces"; offline: "Mantener offline (Recomendado)"; Host symmetry: "Ticket obligatorio ambos (Rec.)". Mirrored in Engram #1548 (`grimhold/fix-55/decisions`).
- **L3** (exploration, `e99a16e5`): `TownRaidPreparationView.cs:480-486` prints the enum name for every failure except `ConnectionFailed`; `TownRaidPreparationPresenter.cs:124-126` consumes `TryConsumeLastTransitionFailure` only in `Bind()`; `RaidMenuPresenter.cs:1036-1061` failed return only `LogError` and `RestorePresentationAfterFailedReturn` shows no text; Host path `RequestTerminalReturnOnce` (:429-449) silent; `AbandonRaidAsync` (:226-232) has no callers; Host wait in `SessionConnectionCoordinator.ObservePendingHostResultsReturn` (:717-757) has no observable state; raid menu `_statusText` (`LocalGameplayHud.prefab`) is the existing label. No localization package; inline Spanish literals; catalog precedent `TownRaidStartRejectionText`. Source-text tests `RaidResultsLifecycleSourceTests` pin `ReturnToTownAsync` text.
- **L4** (user, 2026-11-14, FIX-14 decisions): service failures: "Nuevo ServiceUnavailable (Rec.)"; `AbandonRaidAsync`: "Eliminarlo (Rec.)"; Host wait: "Sí, así (Rec.)" — read-only getter, notice in `_statusText`, re-enable Return after a failure (presentation only).
- **L5** (evidence, T1): RED via Unity CLI (`unity command test run --mode editor --filter ...`, stub catalog returning the enum name): `SessionTransitionFailureTextTests` 23 total / 12 failed, `TownRaidTransitionFailureViewTests` 10 / 10 failed (view printed `No se pudo conectar a la raid: <enum>.`). GREEN: 23/23, 10/10, `TownRaidStartFeedbackViewTests` 7/7. `ServiceUnavailable` appended last to `SessionTransitionResult` (no reordering); its publication is T2. The old `ConnectionFailed` text asserted raid state ("la raid ya no está disponible o ha finalizado") that is not always true; the catalog wording is neutral and context-free (shared with the raid menu).

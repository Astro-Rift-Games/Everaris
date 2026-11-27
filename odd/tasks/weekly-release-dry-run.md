# Weekly release — bootstrap steps 1 and 2 (runner + dry-run)

Execution tracker for `tools/changelog/BOOTSTRAP.md` steps "Required runner and validation" and the first manual
`dry-run=true` on `main`. No publication: `WEEKLY_RELEASE_ENABLED` / `WEEKLY_RELEASE_BOOTSTRAPPED` stay unset.

## Specs

- S1 — Doc fix: BOOTSTRAP.md must name the project's Unity version: "BOOTSTRAP.md pide Unity `6000.5.1f1`, pero el proyecto usa `6000.6.8f1`".
- S2 — Step 1: "Montar y probar el runner, y después definir `UNITY_EDITOR_PATH` y `UNITY_RELEASE_RUNNER_READY=true`." User decision: "Esta PC, solo mientras dure el dry-run" — registered, started by hand (run.cmd), left off afterwards.
- S3 — Step 2: "Correrlo a mano desde `main` en `dry-run=true`, con todas las validaciones en verde."
- S4 — "Las fallas hay que reportarlas en fixes en el hack n plan" (HacknPlan board `Fixes`).

## Tasks

- T1 (S1) inline — fix Unity version and CLI doc URL in BOOTSTRAP.md — done, uncommitted
- T2a (S2) inline — set fork PR approval policy to `all_external_contributors` while the runner is online — done (restored in T6)
- T2 (S2) inline — register self-hosted runner (repo URL `Astro-Rift-Games/Everaris`) `grimhold-release-unity` in `C:\Users\Dani\AppData\Local\Temp\ghr`, Git Bash first in PATH, start run.cmd — done (runner id 21, online)
- T3 (S2) inline — set repo variables `UNITY_EDITOR_PATH`, `UNITY_RELEASE_RUNNER_READY=true` — done
- T4 (S3) inline — dispatch `weekly-release.yml` on `main` with `dry-run=true`; keep artifact; confirm remote heads/tags/Releases unchanged — done: run 45941664223 failed (changelog strict check); remote identical before/after
- T5 (S4) inline — one HacknPlan Fixes item per distinct failure root cause — done: FIX-112 (ID 922), FIX-113 (ID 923), FIX-114 (ID 924); tags not set (create tool has no tags field)
- T6 (S2) inline — stop and deregister runner, set `UNITY_RELEASE_RUNNER_READY=false`, restore approval policy `first_time_contributors` — done (0 runners, READY=false, policy restored, candidate clone deleted)

## Log

- L1 — User: "corregi el documento, y los pasos 1 y 2. Las fallas hay que reportarlas en fixes en el hack n plan"
- L2 — Runner host decision: "Esta PC, solo mientras dure el dry-run".
- L4 — Repo was renamed to `Astro-Rift-Games/Everaris` and is public (runner registration 404 on the old name). User decision: "Seguir, endureciendo el repo durante la prueba" — approval for all external contributors while online; afterwards stop + deregister runner and restore `first_time_contributors`.
- L5 — Dry-run run 45941664223 (2026-11-27 04:44Z): prerequisites ok; simulate failed at the first validation (changelog strict check): 11 "Misplaced or malformed player trailer" errors over 10 commits, and v0.1.0–v0.4.0 "has no visible player notes". Backend and Unity stages never ran. Remote heads/tags/releases unchanged.
- L6 — Root causes (explorer diagnosis + code check): history-wide strict check (versions.js:60-86) fails on 9 commits on main (Player trailer outside final footer, or unindented wrap invalidating the footer) and on v0.1.0–v0.4.0 with no notes; fix via tools/changelog/player-overrides.json. changelog.yml only runs strict preview on PRs; the 9 commits have no PR. weekly-release.js:235/277/338 and config.js:2 hardcode Astro-Rift-Games/ProjectGrimhold; publish would fail with "Official publication remote mismatch" after the rename to Everaris.
- L3 — Facts 2026-11-27: repo has no Actions variables, 0 runners, weekly-release.yml never ran; `main` and `New-Testing` on 6000.6.8f1; New-Testing 62 commits ahead of main; user is repo admin.

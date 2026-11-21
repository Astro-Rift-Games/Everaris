# FIX-34 — JWT renewal and 401 handling without destroying services

HacknPlan FIX-34 (ID 706), parent FIX-US-06 (ID 702). Branch: new `fix/fix-34-jwt-refresh` from New-Testing, created only after the owner merges `fix/fix-61-dungeon-entry-checks` (L5).
Keep a long Town + raid session authenticated so extraction persists after the original JWT expires.

## Specs

- **S1** (AC1) "Con JWT_EXPIRES_IN=60, entrar a raid, esperar 61 s y extraer persiste el loot sin intervención del jugador."
- **S2** (AC2) "Un 401 real (token revocado) sigue cerrando la sesión con mensaje claro."
- **S3** (scope 1) "Mecanismo de renovación automática de credenciales de autenticación y manejo tolerante a expiración de sesión durante una expedición activa, evitando la interrupción o destrucción de servicios locales de almacenamiento."
- **S4** (scope 2) "Diferimiento ordenado de la sincronización de inventario ante credenciales expiradas hasta que el proceso de reautenticación se complete satisfactoriamente."
- **S5** (dependency) "Decisión técnica a acordar antes de implementar: refresh token, re-login silencioso o extensión de vigencia; documentarla en RemoteIdentityAndAuthArchitecture.md."
- **S6** (validation) "Test que reproduzca el defecto: RED observado antes del cambio y GREEN después (backend: node --test; Unity: EditMode vía Test Runner)." and "npm test completo; validación contra el entorno desplegado sólo con autorización explícita."

### Decided design (owner L2)

- Refresh token with rotation: login/register also return a refresh token; `POST /auth/refresh` exchanges it for a new JWT + new refresh token (single use; reuse of a rotated token is rejected); logout revokes it. Refresh lifetime `JWT_REFRESH_EXPIRES_IN`, placeholder default 24 h. Client keeps it in memory only.
- Backend distinguishes an expired access token (new error code, e.g. `TOKEN_EXPIRED`) from an invalid/revoked one (`UNAUTHORIZED`).
- Unity: on `TOKEN_EXPIRED` refresh once (single-flight) and retry the request once without destroying services; inventory sync waits for the refresh. Refresh failure or `UNAUTHORIZED` keeps the current reset (`HandleUnauthorizedSession` → `ResetForLogout`) with a clear message.
- Architecture: update `RemoteIdentityAndAuthArchitecture.md` (authentication, logout, §113 FIX-55 invalidation order).

### Contract (derived from S1-S5 and L2; the architecture document is the authority once T1 lands)

Backend:
- `POST /auth/login` and `POST /auth/register` keep `{ token, expiresIn }` and add `{ refreshToken, refreshExpiresIn }` (seconds).
- Refresh tokens are opaque random values stored only as SHA-256 hashes (`RefreshToken` model: `accountId`, `familyId`, `tokenHash`, `expiresAt` TTL index, `usedAt`, `revokedAt`). The access JWT carries `sid = familyId`.
- `POST /auth/refresh` (unauthenticated), body `{ refreshToken }`: valid, unused, unexpired, unrevoked → `200 { token, expiresIn, refreshToken, refreshExpiresIn }`, marks the old one used (atomic, single use) and issues a new one in the same family. Unknown, expired or revoked → `401 REFRESH_INVALID`. Reuse of an already-used token → revoke the whole family, `401 REFRESH_INVALID`.
- Concurrent refresh with the same token: exactly one succeeds.
- `POST /auth/logout` additionally revokes the refresh family of the access token's `sid`.
- `authenticate` middleware: expired access JWT (`TokenExpiredError`) → `401 TOKEN_EXPIRED`; invalid signature/malformed or revoked → `401 UNAUTHORIZED` (unchanged messages).
- `env.jwtRefreshExpiresIn = parseInt(process.env.JWT_REFRESH_EXPIRES_IN || '86400', 10)` — placeholder (L2).

Unity:
- `ApplicationAuthContext` keeps the refresh token in memory only, beside the access token.
- At the shared authenticated HTTP choke point: `401 TOKEN_EXPIRED` → one single-flight refresh shared by concurrent requests → retry the original request once with the new token, without `HandleUnauthorizedSession` and without `ResetForLogout` (S3). Requests issued while a refresh is in flight wait for it (S4).
- Refresh `401 REFRESH_INVALID`, a second `TOKEN_EXPIRED` after refresh, or `401 UNAUTHORIZED` → current `HandleUnauthorizedSession` path unchanged, with a clear Spanish message (S2).
- Logout sends the access token as today; the backend revokes the family.

## Tasks

| ID | Specs | Route | Commit |
| --- | --- | --- | --- |
| T1 | S5 | inline: architecture contract | — |
| T2 | S2, S3, S6 | backend: refresh model/endpoint, rotation, logout revocation, TOKEN_EXPIRED + tests | — |
| T3 | S1-S4, S6 | Unity: refresh client, single-flight retry on TOKEN_EXPIRED, deferred inventory sync + EditMode tests | — |
| T4 | S1-S6 | gentle-ai-verify (risk item 2 security, item 3 auth contract) | — |
| T5 | S1 | manual: JWT_EXPIRES_IN=60 local raid + extraction (owner) | — |
| T6 | S1, S3 | backend: refresh reuse grace window (owner L9) | fc3a6e45 |

## Log

- **L1** (user): "ok avanza con el modo exploracion para FIX-34 — Renovación del JWT y manejo de 401 sin destruir servicios durante la raid"
- **L2** (user): "ok, sigo tu recomendacion" — refresh token with rotation, 24 h configurable placeholder, approve changing §113 (expired refreshes, only revoked/failed refresh resets).
- **L3** (evidence, explorer): no refresh endpoint (`backend/src/routes/auth.routes.js`); `middleware/authenticate.js:41-49` returns `UNAUTHORIZED` for expired and invalid alike, revoked at :29-34; revocation model `models/RevokedToken.js`; Unity token in memory `Application/ApplicationAuthContext.cs:57-66`, reset at :97-107 → `Stash/ApplicationStashServiceBootstrapper.cs:590`; extraction commit 401 paths `Stash/RemoteInventoryService.cs:485-610`; arch conflict `Docs/Architecture/RemoteIdentityAndAuthArchitecture.md:113`.
- **L5** (user): "rama nueva desde new testing una vez que haya mergeado la rama actual"
- **L6** (evidence): branch `fix/fix-34-jwt-refresh` created from origin/New-Testing e49c31fb (contains FIX-95 merge 32e49896), upstream unset.
- **L7** (evidence T1+T2, worker mwogn5z8-4-y66k): RED 19/29 targeted; GREEN npm test 891/891, test:concurrency 40/40, test:integration 251/251. Choices: rotation grants full lifetime; concurrent loser treated as reuse (family revoked); invalid refresh body 400 VALIDATION_FAILED; separate refresh IP limiter. Open concern for T4: a refresh whose response is lost and is retried with the same token revokes the family (logout under flaky network); evaluate a short reuse grace window. Access JWTs of a revoked family stay valid until exp (documented).
- **L8** (evidence T3, worker mwogn5zd-5-es94): new choke point `Backend/AuthorizedRequest.cs` (all 15 Bearer sites + logout), single-flight refresh in `ApplicationAuthContext`; refresh never retried itself; transport/400/429/500 during refresh surface as transient, no reset. RED 15/23 behavioral + 3 message cases; GREEN FIX34 24/24, AuthenticationSecurityTests 6/6, BackendConfirmedLaunchTests 26/26, LoginFlowControllerTownTicketTests 9/9, BackendErrorMessagesTests 32/32, RemoteCommitRetryTests 28/28; full EditMode 7903/7914 (same 11 unrelated failures as FIX-95, not baselined). Deviation: logout routed through the shared layer (refreshes an expired token first so the family can be revoked). L4 answered: pending extraction commit is written atomically to disk before any HTTP call (`PlayerExtractionLootSaver.cs:693`, `LocalProfileStore.cs:2287`). Risk for T4: `RemoteIdempotentRetryPolicy` retrying the original request after a lost refresh response re-sends the spent refresh token -> family revoked -> logout (safe but harsh); decide on a backend reuse grace window. Editor side effects (fonts, minimap, ProjectSettings, Dialogue.meta) left uncommitted.
- **L9** (user): commits "Sí, commits por unidad (Recomendado)"; grace window "Sí, 30 s configurable (Recomendado)". Work recovered from GitHub Desktop stash@{1} (`git stash apply`, stash kept) after a branch switch stashed it. Commits 4597b104 (T1+T2), 01c65f24 (T3).
- **L10** (evidence T4, verifier mwqr86ts-2-3199, range e49c31fb..fc3a6e45): no blockers. S5 pass; S1-S4 pass by code reading; S6 backend GREEN observed (npm test 902/902, test:concurrency 41/41, test:integration 251/251). Unity EditMode not rerun by the verifier: no Editor connected (`unity status` STATUS_NO_INSTANCES); Unity evidence is the T3 worker run (L8). No Bearer header outside `AuthorizedRequest.cs:89`. Advisories: (1) within grace a resend of a parent whose successor was already exchanged can leave two live branches (needs a stolen token within 30 s); (2) logout with an expired `_pendingToken` before character setup (`LoginFlowController.cs:372`) does not refresh, so the family stays live until expiry; (3) no Unity test for lost refresh response + policy retry inside the grace window. Accepted as documented advisories.
- **L11** (evidence, parent rerun in the owner's open Editor at fc3a6e45): FIX34 category 24/24, AuthenticationSecurityTests 6/6, BackendConfirmedLaunchTests 26/26, LoginFlowControllerTownTicketTests 9/9, BackendErrorMessagesTests 32/32, RemoteCommitRetryTests 28/28 (two suites needed a rerun after a transient Pipeline server unavailability).
- **L4** (answered in L8): confirm whether the pending extraction commit (`LocalProfileStore.cs:2256`, `:2430-2446`) is persisted before the HTTP call; confirm no backend expired-token test exists.

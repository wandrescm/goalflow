# Spec 003: Admin authentication and league membership

- **Status:** Approved
- **Date:** 2026-11-01 (amended on 2026-11-02 after the mutation audit and the JWKS finding: R-17, T-15, C-11; T-02, T-04, R-12, R-16, T-13, T-14)
- **Related:** ADR-01 (authentication), ADR-14 (multi-tenancy), ADR-05 (C-09), ADR-03, Spec 002
- **Effort:** Medium (default)

> Requirement and test IDs are local to this Spec ("Spec 003 T-04").

## 1. Context

Spec 002 isolated leagues and left the identity port (`IIdentidadAutenticada`) set only by tests. This Spec connects it to real Supabase tokens and adds the first Admin-only route, so that the 401, 403 and 200 cases can be proven before any write endpoint exists. The architecture test that forbids `IgnoreQueryFilters` and raw SQL outside the allowlist is **not** part of this Spec: it moved to the domain-model work (decided on 2026-11-01).

## 2. Goal

`GET /{slug}/admin/sesion` answers 200 only for a valid Supabase ES256 token whose user currently administers that league; every other case answers 401, 403 or 404 as specified, proven by tests that do not depend on Supabase.

## 3. Requirements (EARS)

### 3.1 Token validation

| ID | Requirement |
| --- | --- |
| R-01 | The API shall accept only tokens signed with ES256 by a key from the configured JWKS whose `kid` matches the token. |
| R-02 | If a token is signed with any other algorithm, including HS256 and `none`, then the API shall respond 401. |
| R-03 | If a token's signature does not verify, or its `iss`, `aud` (`authenticated`), `exp` (30-second skew), `nbf`, `role` (`authenticated`) or `is_anonymous` (not `true`) is not valid, or its `sub` is not a UUID, then the API shall respond 401. |
| R-04 | When a token arrives with a `kid` that is not in the cached JWKS, the API shall refresh the JWKS once and accept the token if the new set contains that key; it shall not refresh more than once per configured interval. |
| R-05 | The API shall not download the JWKS on every request. |
| R-06 | The API shall not write tokens, claims or the `Authorization` header to logs. |

### 3.2 Admin route

| ID | Requirement |
| --- | --- |
| R-07 | When `GET /{slug}/admin/sesion` is requested with an unknown slug, the API shall respond 404, with or without a token. |
| R-08 | When it is requested for an existing league without a token, or with a token that fails R-01 to R-03, the API shall respond 401 with the header `WWW-Authenticate: Bearer` and no error details in the header or the body (RFC 9110 requires the header on every 401). |
| R-09 | When it is requested with a valid token whose user has no row in `admins_liga` for that league, the API shall respond 403. |
| R-10 | When it is requested with a valid token whose user has a row in `admins_liga` for that league, the API shall respond 200 with the league slug and the GoalFlow role from `admins_liga.rol`. |
| R-11 | If the membership row is deleted while the token is still valid, then the next request with that token shall get 403. |
| R-12 | The membership check shall run inside a transaction that carries both the league (tenant) and the user (identity), through a port in `GoalFlow.Application.Ligas`. The adapter shall filter **explicitly** by both the league and the user; RLS on `admins_liga` is the second layer, not the only one (ADR-14 §2.2). |
| R-13 | The identity shall be set in `IIdentidadAutenticada` only from the `sub` of a token that passed validation. |
| R-16 | If the JWKS cannot be loaded (network error, invalid document), then Admin routes shall respond 401 (fail closed) and the API shall log the failure at warning level or above, without the token. Public routes are not affected. |
| R-17 | After a failed JWKS load, the API shall not try to load it again for a configurable back-off period (`Auth:JwksFailureBackoffSeconds`, 30 s in production). Within that period, Admin requests shall get 401 without any download, and the failure shall be logged at warning level once per period, not once per request. |

### 3.3 Production check (manual, by the owner)

| ID | Requirement |
| --- | --- |
| R-14 | In production, a token obtained from Supabase Auth for a test Admin of a test league shall get 200, and 403 after the membership row is deleted. This also confirms that Supabase Auth works with the Data API disabled (ADR-05 C-09). |
| R-15 | In production, a sign-up attempt through the Supabase Auth API with the public key shall be rejected, because public sign-up is disabled (ADR-01 C-07). |

## 4. Non-functional constraints

- Configuration: `Auth:Issuer`, `Auth:JwksUrl` and the JWKS refresh intervals come from configuration; no value is hard-coded. None of them is a secret.
- Tests generate a test-only ECDSA P-256 key pair in memory. No private key is committed, and no test calls Supabase.
- Order of checks on Admin routes: slug (404) → token (401) → membership (403), as in ADR-01 §2.2.
- R-15 is a manual production check with no automated test: the sign-up setting lives in Supabase, outside the code.
- Application has no reference to ASP.NET Core (existing architecture tests).
- Only one new package is allowed: `Microsoft.AspNetCore.Authentication.JwtBearer`, in `GoalFlow.Api`, in the 10.0.x line matching the runtime.

## 5. Clarifications

| # | Question | Decision | Decided by / date |
| --- | --- | --- | --- |
| C-01 | Accepted algorithms? | ES256 only (ADR-01 C-01). | Owner, 2026-11-01 |
| C-02 | 401 or 403 for a revoked membership with a valid token? | 403 (ADR-01 C-02). | Owner, 2026-11-01 |
| C-03 | Where does the GoalFlow role come from? | `admins_liga.rol` (ADR-01 C-03). | Owner, 2026-11-01 |
| C-04 | Which route proves the flow? | `GET /{slug}/admin/sesion`, returning the slug and the role. | Owner, 2026-11-01 |
| C-05 | How are tokens produced in tests? | A test-only P-256 key pair; the API reads the test public key through the same JWKS mechanism, replaced in the test host. | Owner, 2026-11-01 |
| C-06 | Does `is_anonymous = true` give 401 or 403? | 401: anonymous sign-ins are not an accepted Admin identity, so the token fails validation. | Mentor, 2026-11-01 |
| C-07 | Does Supabase expose an OpenID discovery document, or only the JWKS? | Not assumed. The implementation checks it; if there is no discovery document, the JWKS is loaded directly from `Auth:JwksUrl`. Recorded in the PR. | Pending (handoff) |
| C-08 | Architecture test for `IgnoreQueryFilters` and the raw SQL allowlist? | Moved to the domain-model work. | Owner, 2026-11-01 (replanning) |
| C-09 | Why did the HS256 mutation survive T-04? | In the audit of 2026-11-02, adding HS256 to `ValidAlgorithms` left T-04 green: the token's `kid` points to an EC key, and an EC key cannot verify an HMAC signature, so validation fails anyway. T-04 now has a behavioral case where the list is the only barrier (RS256 with a published RSA key) and a check of the effective configuration. The configuration check was proposed by the owner. | Owner and mentor, 2026-11-02 (mutation audit) |
| C-10 | Is HS256 in the list exploitable today? | Not with the current JWKS, which holds only asymmetric keys. It would become exploitable if a symmetric key ever entered the key set, so the list stays strict and tested. | Mentor, 2026-11-02 |
| C-11 | What happens if the API starts while the JWKS cannot be loaded? | Found by Claude Code on 2026-11-02: with no keys cached, every Admin request with a well-formed JWT (which anyone can forge, because the signature is checked only after the keys are loaded) triggered a new download and a warning. Decision: a 30-second back-off after a failure (R-17). Trade-off: a transient error at startup keeps Admins on 401 for up to 30 s. With keys already cached, a failed refresh keeps the previous keys. | Owner, 2026-11-02 |

## 6. Tests

| Test ID | Covers | Description | Must fail when |
| --- | --- | --- | --- |
| T-01 | R-10 | Valid ES256 token, user is Admin of league A → 200 with `liga-a` and role `admin`. | The membership check is skipped or reads the wrong league. |
| T-02 | R-08 | No token, league A → 401 with `WWW-Authenticate` equal to `Bearer`, and neither the header nor the body contains `error` details. | The route is not protected, or the header is missing. |
| T-03 | R-03 | Token signed by a different P-256 key with the same `kid` → 401. | Signature validation is disabled. |
| T-04 | R-01, R-02 | (a) `alg: HS256` with the test public key's bytes as HMAC secret → 401. (b) `alg: none` → 401. (c) **The algorithm list is the only barrier:** the test JWKS also publishes an RSA 2048 key K3 with its own `kid`; a token **correctly signed** with K3 using RS256 → 401. (d) **Effective configuration:** the `JwtBearerOptions` built by the real registration have `ValidAlgorithms` equal to exactly `[ES256]`. | (c) `ValidAlgorithms` is removed, or RS256 is added. (d) Any algorithm is added, including HS256. Cases (a) and (b) alone do not detect a widened list: the mutation audit showed that adding HS256 leaves them green, because an EC key cannot verify HMAC (C-09). |
| T-05 | R-03 | One case each, all → 401: expired by 2 minutes; wrong `iss`; wrong `aud`; `role` = `anon`; `is_anonymous` = `true`; `sub` not a UUID. | The corresponding check is removed (one mutation per case). |
| T-06 | R-03 | Token expired by 10 seconds → 200 (inside the 30-second skew); expired by 2 minutes → 401 (same as T-05). | The skew is set to zero (first case red) or left at the 5-minute default (T-05 expiry case red). |
| T-07 | R-04, R-05 | (1) **Legitimate rotation:** the test JWKS source starts with key K1; K2 is added to the source; a token signed with K2 (unknown `kid` for the cache) → 200 after one on-demand refresh. (2) **No per-request reads:** 10 requests with K1 do not read the source again. (3) **Bounded refresh against unknown `kid`s:** inside one minimum interval, 10 tokens with 10 random `kid`s that are in no key set → all 401, and the source is read **at most once**. The counter is part of the test source. | On-demand refresh is disabled (case 1 stays 401), the JWKS is read on every request (case 2), or every unknown `kid` triggers a download (case 3: an attacker could make the API hammer Supabase's JWKS endpoint). |
| T-08 | R-09 | Valid token of the Admin of league A, request to `liga-b` → 403. | Membership is checked by user only, ignoring the league. |
| T-09 | R-11 | Same valid token: 200; the membership row is deleted (as superuser); same token → 403. | Membership is cached or read from the token. |
| T-10 | R-07 | Valid token and no token, slug `no-existe` → 404 in both. | Authentication runs before slug resolution. |
| T-11 | R-12, R-13 | Connected as `goalflow_app` under RLS: if the identity is not set in the transaction, a real Admin gets 403. Covered by the mutation of T-01 (identity not set → T-01 red). | — |
| T-12 | R-06 | With a capturing logger, one request of each kind (200, 401, 403) writes no log entry that contains the token or the word `Bearer`. | A log line includes the `Authorization` header or the token. |
| T-13 | R-16 | The test JWKS source throws on read. A valid-looking ES256 token on `/liga-a/admin/sesion` → 401; a log entry at warning or above records the failure and contains neither the token nor `Bearer`; `GET /liga-a/equipos` → 200. | The failure surfaces as 500, or the token appears in the log. |
| T-15 | R-17 | With the test JWKS source failing and a short back-off (for example 2 s): 5 Admin requests inside the period → all 401, the source is read **once**, and exactly **one** warning is logged. After the period, the source works again and the next request → 200 after **one** new read. A third case: a forged, well-formed JWT (random `kid`, no valid signature) inside the period does not read the source. | The back-off is removed (5 reads), the warning is logged per request, or the back-off never expires (the request after the period stays 401). |
| T-14 | R-12 | **Explicit user filter, without RLS:** the membership adapter runs with a superuser connection, so RLS does not apply. With tenant `liga-a` and the identity of a user with **no** membership there, it returns no role. | The adapter filters by league only and relies on RLS for the user (it then returns another Admin's role). |

## 7. Out of scope

- Fan sign-in (Google, email), Captain magic link, frontend sign-in, refresh tokens on the client.
- Any write endpoint.
- The architecture test for `IgnoreQueryFilters` and raw SQL (C-08).
- Revoking the legacy HS256 secret (ADR-01 C-05).
- A foreign key from `admins_liga.user_id` to Supabase's `auth.users`.

## 8. Definition of done

- [ ] All tests above pass and none was modified to make it pass.
- [ ] Every requirement maps to at least one test or to a production check (R-14, R-15).
- [ ] Production checks R-14 and R-15 passed, with evidence recorded in the PR.
- [ ] No new warnings; no secrets or private keys in the diff.
- [ ] ADR-05 C-09 updated after the production check.
- [ ] PR description contains the validation table with real results.

## 9. Validation (filled at PR time)

| Requirement | Test | Result | Evidence |
| --- | --- | --- | --- |
| R-01 | T-01 | | |

## 10. Risks and open items

- **Key rotation:** a rotation must be planned (standby key → wait for caches → rotate). Covered in code by R-04; operationally by ADR-01 §4.
- **Supabase Auth accounts outside the backup** (ADR-05 C-10): to be decided before fan sign-in.
- **Rate limit** on public routes: still the temporary exception of Spec 002 §10 (deadline 2026-11-16). `/{slug}/admin/sesion` requires a valid token, so it is not public.

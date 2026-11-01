# ADR-01: Authentication of league Admins

- **Status:** Accepted
- **Date:** 2026-11-01
- **Related:** ADR-14 (multi-tenancy, `admins_liga`), ADR-05 (hosting, Data API off, C-09, C-10), ADR-03 (modules), Spec 003

## 1. Context

League Admins are the only users who will write match results. The API must know **who** is calling and **whether** that person currently administers the league in the route. Identity is delegated to Supabase Auth; authorization is GoalFlow's own concern. Fans and Captains are out of scope here.

Facts that shape the decision (checked on 2026-11-01):

1. The project's **current** signing key is asymmetric **ECC P-256 (ES256)**. The previous key, a legacy **HS256** shared secret, is listed as "previously used": Supabase still accepts tokens it signed until they expire. The legacy `anon` and `service_role` API keys are JWTs signed with that legacy secret.
2. Supabase publishes the public signing keys at `https://<project-ref>.supabase.co/auth/v1/.well-known/jwks.json`, only for asymmetric keys. Supabase's edge caches it for 10 minutes and recommends that backends refresh their own cache instead of fetching it on every request. Key rotation does not sign users out; tokens signed with the old key stay valid until they expire.
3. A Supabase access token identifies the user (`sub`) and carries `iss`, `aud`, `exp`, `iat`, `role` and `is_anonymous`. It carries no league.
4. Accepting the algorithm named in the token's own header enables algorithm-confusion attacks (`alg: none`, or HS256 using a public key as the HMAC secret).
5. Membership is stored in `goalflow.admins_liga` and can only be changed by `postgres` (ADR-14 §2.7).

## 2. Decision

### 2.1 Authentication: Supabase issues, the API verifies

- The API validates Supabase access tokens itself, in `GoalFlow.Api`, with ASP.NET Core's JWT bearer authentication. It never calls Supabase per request.
- **Accepted algorithm: ES256 only.** The list of valid algorithms is fixed in configuration; the token's `alg` header is never trusted. HS256 and `none` are rejected, including tokens signed with the previous legacy secret.
- **Signing keys:** only keys from the project's JWKS whose `kid` matches the token. The API caches the JWKS, refreshes it periodically, and refreshes it on demand when a token arrives with an unknown `kid`, at most once every few minutes. It never downloads the JWKS on every request.
- **Validated claims:**
  - signature;
  - `iss` = `https://<project-ref>.supabase.co/auth/v1` (from configuration);
  - `aud` = `authenticated`;
  - `exp`, with a clock skew of **30 seconds** (the .NET default of 5 minutes is too generous);
  - `nbf` if present;
  - `sub` must be a UUID;
  - `role` = `authenticated`;
  - `is_anonymous` must not be `true`.
- A token that fails any check gives **401**. Tokens, claims and `Authorization` headers are never logged.

### 2.2 Authorization: GoalFlow decides, on every request

- A valid token proves identity, not permission. Every Admin route checks the **current** membership in `admins_liga` for (`sub`, league of the route).
- The Supabase `role` claim (`authenticated`) is not a GoalFlow role. The GoalFlow role comes from `admins_liga.rol`.
- Order on an Admin route `/{slug}/admin/…`:
  1. resolve the slug, or **404** (leagues are public; ADR-14 §2.6);
  2. authenticate the token, or **401**;
  3. set the tenant from the slug and the identity from `sub`;
  4. read the membership inside a transaction that carries both (ADR-14 §2.3, §2.7), or **403**.
- Revoking an Admin (deleting the row) takes effect on the next request, without waiting for the token to expire. The cost is one indexed query per Admin request.

### 2.3 Placement (ADR-03)

- Token validation and the Admin-route filter live in `GoalFlow.Api` (`Hosting`).
- The membership check is a port in `GoalFlow.Application.Ligas`, with its adapter in `GoalFlow.Infrastructure.Ligas`. Application stays free of ASP.NET Core.
- The identity reaches Application only through `IIdentidadAutenticada`, set from `sub` after the token is validated.

### 2.4 Admin accounts for the milestone

- Admin users are created by the owner in the Supabase dashboard (Authentication → Users → **Create new user**, not *Send invitation*), with email and password and the email already confirmed. No email is sent, so the account does not depend on the mail service or its built-in limit.
- **Public sign-up is disabled** in Supabase (Authentication → setting that allows new users to sign up). Creating accounts by hand does not disable it by itself: with sign-up enabled, anyone holding the public key could create an account through the Auth API. Such an account would still get 403 on Admin routes, because authorization depends on `admins_liga`, but the setting removes that surface. Accounts created from the dashboard keep working with sign-up disabled. The setting is revisited before fan sign-in (which needs sign-up), with P-07.
- Membership rows are inserted by the owner as `postgres` (ADR-14 §2.7), after each league administrator's written authorization.
- Admin sign-in from the frontend arrives with the frontend work. Until then, a token is obtained directly from Supabase Auth for verification.

## 3. Rejected alternatives

- **League or role in a custom claim (Custom Access Token Hook):** revocation would wait for token expiry (≈ 1 hour), and logic would live in Supabase's `auth` schema. Rejected in ADR-14 C-03.
- **Asking Supabase on every request (`GET /auth/v1/user`):** one network round trip per request to another service; it only made sense for HS256 projects.
- **Validating with the legacy HS256 secret:** a shared secret in Render and an algorithm Supabase no longer recommends; the project already signs with ES256.
- **`404` for an Admin of another league:** the league is public (ADR-14 C-04).

## 4. Consequences

- **(+)** No signing secret in the API: only public keys.
- **(+)** Revocation is immediate.
- **(+)** Tokens can be produced in tests with a test-only key pair, so no test depends on Supabase.
- **(−)** One extra query per Admin request.
- **(−)** The JWKS cache must follow key rotations. A rotation is planned: create the standby key, wait for the caches, then rotate.
- **(−)** Supabase Auth users are not in the `goalflow` backup (ADR-05 C-10). Losing the project loses the accounts; memberships would then point to users that no longer exist.

## 5. Verification

Automated tests and a production check are listed in Spec 003. The production check also resolves ADR-05 **C-09**: whether Supabase Auth works with the Data API disabled.

## 6. Clarifications

- **C-01** *Which algorithms are accepted?* ES256 only. Decided by the owner on 2026-11-01.
- **C-02** *401 or 403 when a valid token's membership was deleted?* 403: the identity is valid; the permission is not. Decided by the owner on 2026-11-01.
- **C-03** *Where does the GoalFlow role come from?* From `admins_liga.rol`, never from the token's `role` claim. Decided by the owner on 2026-11-01.
- **C-04** *How are Admin accounts created during the milestone?* By the owner in the Supabase dashboard with *Create new user* (not *Send invitation*), with email and password, already confirmed; no public sign-up. Memberships in `admins_liga` are managed separately, as `postgres`. Decided by the owner on 2026-11-01.
- **C-05** *Should the legacy HS256 secret be revoked?* Not now. The API already rejects HS256. Revoking it first requires disabling the legacy `anon` and `service_role` keys and checking that nothing uses them (the keep-alive and backup workflows connect to PostgreSQL directly, not through those keys). To be done before the pilot opens to real users.
- **C-06** *Clock skew?* 30 seconds (`ClockSkew = TimeSpan.FromSeconds(30)`), instead of the .NET default of 5 minutes. Decided by the owner on 2026-11-01.
- **C-07** *Is public sign-up disabled?* Yes, explicitly, in the Supabase dashboard; verified in production by a sign-up attempt that must be rejected (Spec 003 R-15). Raised by the owner on 2026-11-01.

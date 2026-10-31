# ADR-14: Multi-tenancy and data isolation between leagues

- **Status:** Accepted
- **Date:** 2026-10-30 (revised the same day after the owner's review: §2.2, §2.3, §2.6, §2.7, §4, C-08 to C-11)
- **Related:** ADR-03 (modular monolith), ADR-05 (hosting, roles, migrations), ADR-01 (authentication, pending), Spec 002

## 1. Context

GoalFlow serves several leagues from one API process and one PostgreSQL database. A league (tenant) must never read or write another league's data, and a single bug must not be enough to break that. The pilot starts with two leagues.

Facts that shape the decision:

1. The API connects as `goalflow_app`, a login role with `NOBYPASSRLS` that owns no tables (ADR-05). Tables are created by `postgres` through migrations, so row-level security (RLS) policies apply to `goalflow_app`.
2. PostgreSQL skips RLS for superusers, for roles with `BYPASSRLS` and for the table owner (unless the table has `FORCE ROW LEVEL SECURITY`).
3. The API reaches the database through Supavisor in session mode, and Npgsql keeps a client-side pool: one physical connection serves requests from different leagues one after another. Session-level state left on a connection is visible to the next request that reuses it.
4. `set_config(name, value, true)` (equivalent to `SET LOCAL`) lasts only until the end of the current transaction. After that, `current_setting(name, true)` returns an empty string, not `NULL`, for the rest of the session.
5. EF Core builds the model once per context type and caches it. A global query filter that captures a local value in `OnModelCreating` keeps that first value forever; a filter that references a member of the `DbContext` is evaluated per query. EF Core 10 supports named query filters, so the tenant filter can be ignored or kept independently of other filters.
6. Global query filters apply to queries only. They do not validate the `TenantId` of entities being inserted or updated.
7. The access token issued by Supabase Auth identifies the user (`sub`) but carries no league. Writing league data into the token requires either a custom access token hook or the admin API with `service_role`, which ADR-05 forbids.
8. Public pages of a league (results, standings, teams) are readable by anyone at `/{slug}`, so the existence of a league is not secret.
9. EF Core's default `AutoTransactionBehavior` is `WhenNeeded`: when `SaveChanges` sends a single statement, EF Core does not open a transaction, because the database already runs that statement atomically. A transaction interceptor therefore does not run for such a `SaveChanges`.

## 2. Decision

### 2.1 Model

- **Shared database, shared schema** (`goalflow`). Every tenant-scoped table has `tenant_id uuid not null`.
- **A tenant is a league** (one to one). The tenant identifier is the league identifier.
- Every `tenant_id` column has a foreign key to `ligas(id)` with `ON DELETE RESTRICT`: a league that still has rows cannot be deleted, and `CASCADE` is never used for tenant keys. Referential-integrity checks are not subject to RLS and need no extra privilege for `goalflow_app`. They are database constraints, not code dependencies, so they do not affect the module graph of ADR-03.

### 2.2 Two layers

Isolation is enforced twice. A missing or wrong EF Core filter, an `IgnoreQueryFilters` or a raw query is stopped by RLS; a missing or wrong policy is stopped by the EF Core filter. Both layers read the tenant from the same `ITenantContext`, so they do **not** protect against a wrong tenant value: that is protected by tenant resolution (§2.6), by authorization (ADR-01) and by their tests (see §4).

1. **Application layer (EF Core).** Every tenant-scoped entity has a named query filter `Tenant` whose expression references a member of the `DbContext` that reads `ITenantContext`. The filter never captures a local value.
2. **Database layer (RLS).** Every tenant-scoped table has `ENABLE ROW LEVEL SECURITY` and one policy for all commands:
   `USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)` and the same expression in `WITH CHECK`.
   `nullif` turns the empty string left by an ended transaction (fact 4) into `NULL`, so the policy returns no rows instead of raising a cast error.
   Tables are not `FORCE`d: their owner is `postgres`, used only for migrations, seeds and maintenance. `goalflow_app` must never own a table.

### 2.3 The scope is an explicit transaction

- Every read or write on tenant-scoped tables and on `admins_liga` runs inside an **explicit** transaction opened through a unit-of-work port in Application. Transactions that EF Core might open on its own are not relied upon (fact 9).
- **One exception: slug resolution.** Reading `ligas` to resolve a slug happens before any tenant or identity exists. It runs without a transaction and without context, through its own port. This is safe because `ligas` has no RLS and holds only public data, and because an EF Core query outside an explicit transaction opens none, so the transaction interceptor does not run.
- When a transaction starts on the GoalFlow `DbContext`, a transaction interceptor in Infrastructure sets, with transaction scope (`set_config(…, true)`):
  - `app.tenant_id`, if `ITenantContext` has a tenant;
  - `app.user_id`, if the authenticated-identity port has a user (§2.7).
  If neither is set, the request fails. The unit-of-work port checks first, before using EF Core, so the connection is never opened. For a transaction opened directly on the `DbContext`, the interceptor checks again in its transaction-starting hook: EF Core has already opened the connection at that point, but no transaction exists yet and no command has run.
- If `SaveChanges` is called while no transaction is active, a `SaveChanges` interceptor throws a specific exception and nothing is sent to the database.
- Setting either value at session level (`is_local = false`) is forbidden.
- Outside a transaction, RLS returns no rows on tenant-scoped tables and on `admins_liga`, and rejects writes. This is the intended fail-closed behavior.
- The `DbContext` and its interceptors live in a shared persistence namespace of Infrastructure that belongs to no module. The tenant context, the identity port, the `DbContext` and the interceptors are scoped; DbContext pooling is not used, and no singleton may capture scoped state. Passing scoped interceptor instances must not make EF Core build a new internal service provider per scope; `ManyServiceProvidersCreatedWarning` is configured to throw so that this cannot happen silently. Because ASP.NET Core validates scopes and registrations only in Development, the API enables both validations in every environment. Without them, a singleton that depends on a scoped service starts in Production with a root-level instance shared by every request: depending on the code, it breaks every request at runtime or shares tenant state across requests. With validation, it fails at startup (found in the mutation audit, Spec 002 C-18).

### 2.4 Tenant context

- `ITenantContext` is a port in `GoalFlow.Application.Tenancy`. Reading the current tenant before it is set throws a specific exception. It never returns an empty value.
- The tenant is set once per DI scope. Setting it again with a different value throws.
- As in ADR-03 §8, background jobs set it from the message, never from ambient state.

### 2.5 Write guard

- An EF Core `SaveChanges` interceptor checks every tenant-scoped entity being added or modified:
  - If an added entity has an empty `TenantId`, the interceptor assigns the current tenant.
  - If an added entity has a different `TenantId`, the interceptor throws.
  - If a modified entity changes its `TenantId`, the interceptor throws.
- RLS `WITH CHECK` backs this up at the database.

### 2.6 How the tenant is resolved

- **Public routes** `/{slug}/…`: the API resolves the slug to a league through a port of the `Ligas` module, without a transaction (§2.3), and sets `ITenantContext`. The public read that follows runs in a tenant transaction. An unknown slug returns `404`.
- **Admin routes:** in this order: (1) the slug is resolved and the tenant is set, as for public routes; (2) the token is validated and the authenticated identity is set from its `sub`; (3) membership is checked in `admins_liga` inside a transaction that carries both values. Details in ADR-01. An authenticated Admin who does not belong to that league gets `403`, not `404`: the league is public (fact 8), so a `404` would hide nothing and would contradict the public page.

### 2.7 Global tables and the authorization path

Two tables are read **before** a tenant exists, so they cannot use the tenant policy:

- **`ligas`** (league catalog: id, slug, name). No RLS: its content is public. `goalflow_app` has `SELECT` only.
- **`admins_liga`** (`user_id`, `tenant_id`, `rol`). RLS by identity, independent of the tenant:
  `USING (user_id = nullif(current_setting('app.user_id', true), '')::uuid)`. `goalflow_app` has `SELECT` only.
- `app.user_id` comes only from the `sub` of a token that was already validated, through an authenticated-identity port owned by the `Ligas` module. It is set by the same transaction interceptor (§2.3).
- A transaction with an identity and **no tenant** is the **authorization path**: it can read the user's own rows in `admins_liga` (for example, to list the leagues an Admin manages). Business operations cannot run there: the EF Core tenant filter throws because the tenant is missing, and RLS returns no tenant-scoped rows.
- The role script grants DML on every new table through `alter default privileges` (ADR-05). The migration that creates these two tables therefore **revokes** `INSERT`, `UPDATE` and `DELETE` on them from `goalflow_app`, and revokes all privileges on the EF Core migrations history table, which the API does not need.
- League and Admin assignments are created only by SQL run as `postgres`. The API cannot create, change or delete them.

### 2.8 Module placement

- **Tenancy** (shared kernel) contains only the `TenantId` value, the `ITenantContext` port and the missing-tenant exception. It does not grow.
- **Ligas** is a new module that owns `Liga` and `AdminLiga` and the ports that resolve a slug and a membership. Module graph row: `Ligas → Tenancy` (ADR-03 §7).
- The API composition root uses the `Ligas` ports to resolve the tenant. Other modules receive the `TenantId` from `ITenantContext` and do not depend on `Ligas`.

### 2.9 SuperAdmin

- There are no SuperAdmin endpoints for now. Leagues and Admins are created by SQL as `postgres`.
- `IgnoreQueryFilters` stays forbidden in application code. Any future cross-tenant path needs a new decision, because RLS would block it anyway.

### 2.10 Migrations (resolves ADR-05 C-02)

- Schema changes are EF Core migrations. RLS policies and revokes are part of the migration (raw SQL inside the migration), so the schema and its security change together.
- CI generates an idempotent SQL script (`dotnet ef migrations script --idempotent`) and publishes it as an artifact. The owner reviews it and applies it as `postgres` in the Supabase SQL editor.
- Objects are owned by `postgres`, so the default privileges of the role script apply. The `postgres` password never enters GitHub.

## 3. Rejected alternatives

- **EF Core filter only:** one `IgnoreQueryFilters`, raw SQL query or misconfigured filter leaks data, and nothing below catches it.
- **RLS only:** isolation would depend entirely on every transaction setting the tenant. Missing context would show up only as empty results, with no application-level error.
- **Tenant set when the connection opens and cleared when it closes:** safe only if the clearing never fails and the pool always resets the session. A transaction-local value ends with the transaction and cannot outlive it.
- **Database or schema per tenant:** migrations, backups and connections multiplied by the number of leagues; does not fit the free plan or one developer.
- **League in a custom JWT claim:** stale until the token is refreshed, and it adds logic to Supabase's `auth` schema. Through `app_metadata` it also needs `service_role`, forbidden by ADR-05.
- **`404` for an Admin of another league:** the league is public, so `404` would hide nothing and would make errors harder to diagnose.

## 4. Consequences

- **(−)** The two layers share one input. If tenant resolution or authorization sets the wrong league, both layers filter faithfully by that wrong league. Resolution and membership checks therefore need their own tests (Spec 002 T-15; Spec 003).

- **(−)** Every unit of work needs an explicit transaction plus one extra statement (`set_config`). Public reads also run in transactions, and single-statement writes lose EF Core's round-trip saving (fact 9). The overhead is measured once the API connects to the database.
- **(−)** Two mechanisms must stay consistent: every new tenant-scoped table needs both the EF Core filter and the RLS policy in the same migration.
- **(−)** Tests need a real PostgreSQL and a non-superuser role: superusers and table owners skip RLS (fact 2), so a test connected as either would pass without proving anything.
- **(+)** Each layer fails closed on its own when the tenant is **missing**: the application throws and the database returns no rows.
- **(+)** Admin assignments cannot be changed by the API, even through a bug or an injection.

## 5. Verification

Isolation is tested in two layers, because a test that goes through both cannot tell which one protected the data.

- **EF Core layer:** tests through the `DbContext`. The canary is a filter that captures a local value, which must make the two-league test fail.
- **RLS layer:** raw SQL connected as `goalflow_app`, without EF Core. The canary is the same query run as a superuser, which must return the other league's rows. If it returned none, the test would be measuring nothing.
- Tests run against PostgreSQL 17 (same major version as production) in a disposable container. Setup (roles script and migrations) runs as the container superuser; isolation tests run as `goalflow_app`.

The full list of requirements and tests is in Spec 002.

## 6. Clarifications

- **C-01** *Filter only or filter plus RLS?* Both, now. Decided by the owner on 2026-10-30.
- **C-02** *How is the tenant set for RLS?* Inside a transaction with `set_config(…, true)`; never at session level. Decided by the owner on 2026-10-30.
- **C-03** *Where does an Admin's league come from?* From the `admins_liga` table, looked up by the token's `sub`. Decided by the owner on 2026-10-30.
- **C-04** *Status code for an Admin of another league?* `403`. Decided by the owner on 2026-10-30.
- **C-05** *Where does `Liga` live?* In a new `Ligas` module; Tenancy keeps only `TenantId` and `ITenantContext`. Decided by the owner on 2026-10-30.
- **C-06** *Restrictions on the global tables?* `admins_liga` is read only by the token's `sub` through the authorization path. `goalflow_app` cannot modify league or Admin assignments. Missing tenant context is allowed only in that authorization path. Decided by the owner on 2026-10-30.
- **C-07** *SuperAdmin?* No endpoints for now; leagues and Admins are created by SQL as `postgres`. Decided by the owner on 2026-10-30.
- **C-08** *Can the design rely on transactions that EF Core opens inside `SaveChanges`?* No. With the default `WhenNeeded`, a single-statement `SaveChanges` opens no transaction (fact 9), so the interceptor would not run. Every unit of work opens an explicit transaction, and `SaveChanges` without one throws (§2.3). Raised by the owner on 2026-10-30.
- **C-09** *How can `admins_liga` be read before the tenant is known, if every transaction requires a tenant?* The interceptor sets whatever context exists (tenant, identity or both) and throws only when neither exists (§2.3). Admin routes resolve the tenant from the slug first (§2.6); the identity-only path is limited to the user's own membership rows (§2.7). Raised by the owner on 2026-10-30.
- **C-10** *Raw SQL is forbidden, but RLS can only be proven with raw SQL. What is allowed?* In production code: migrations (policies, revokes) and the `set_config` statement of the transaction interceptor. In tests: isolation tests that connect to the disposable test database. Nothing else. Spec 003 turns this allowlist into an architecture test. Raised by the owner on 2026-10-30.
- **C-11** *Are the two layers independent?* Only against missing filters, policies or context. They share the tenant value, so a wrong value passes both (§2.2, §4). Raised by the owner on 2026-10-30.
- **C-12** *How is a public league resolved with neither tenant nor user?* The slug resolver reads `ligas` without a transaction and without context (§2.3); it is the only read allowed outside the unit-of-work port. Raised by the owner on 2026-10-30.
- **C-13** *Foreign keys from `tenant_id` to `ligas`?* Yes, on `equipos` and `admins_liga`, added before the first migration is applied, with `ON DELETE RESTRICT`. Decided by the owner on 2026-10-30; delete behavior raised by the owner and set on 2026-10-31.
- **C-14** *Rate limit on the first public endpoint?* Deferred as a temporary exception to `CLAUDE.md`, with owner, deadline (before the frontend is public, no later than 2026-11-16) and pending tests (real client IP, forged `X-Forwarded-For`, HTTP `429`). Recorded in Spec 002 §10. Decided by the owner on 2026-10-30.

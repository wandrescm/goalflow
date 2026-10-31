# Spec 002: Multi-tenancy and data isolation between leagues

- **Status:** Approved
- **Date:** 2026-10-30 (revised the same day after the owner's review; amended on 2026-10-30 and 2026-10-31 after the implementation audit: T-11, T-13, T-18, R-29, R-30, rate-limit exception)
- **Related:** ADR-14 (multi-tenancy), ADR-03 (modular monolith), ADR-05 (hosting, roles, migrations), Spec 001
- **Effort:** Medium (default) for implementation; High for the RLS and transaction-interceptor parts

> A Spec states **what** must be true, not how to build it. Design decisions live in ADR-14.
> Requirement and test IDs in this file are local to Spec 002 (for example "Spec 002 T-04").

## 1. Context

The API is deployed and connects to nothing yet: there is no EF Core, no table and no tenant concept in code. Before any league data exists, isolation between leagues must be built and proven, in the application (EF Core) and in the database (row-level security). This Spec introduces the first database access, the first migration and the first tenant-scoped table. Authentication of Admins comes in Spec 003.

## 2. Goal

Two leagues can be served by the same API and database, and every test that tries to read or write across leagues fails closed, in both layers, with a canary proving each test can fail.

## 3. Requirements (EARS)

### 3.1 Kernel and modules

| ID | Requirement |
| --- | --- |
| R-01 | The Tenancy module shall contain only the `TenantId` value type (in Domain), the `ITenantContext` port and the missing-tenant exception (in Application). |
| R-02 | The system shall have a `Ligas` module that owns `Liga` and `AdminLiga` and depends only on Tenancy; ADR-03 §7 and `TablaDeModulos` shall contain the row `Ligas → Tenancy`. |
| R-03 | `GoalFlow.Domain` shall not reference EF Core; the architecture rule that forbids frameworks in Domain shall have a canary with an EF Core type. |
| R-04 | The system shall have a minimal tenant-scoped entity `Equipo` (identifier, `TenantId`, name) in the `Torneos` module, used as the first isolated table. Its full shape is defined by the domain-model Spec. |

### 3.2 Tenant context

| ID | Requirement |
| --- | --- |
| R-05 | If the current tenant is read before it is set, then `ITenantContext` shall throw a specific exception and shall not return an empty value. |
| R-06 | If the tenant is set a second time in the same scope with a different value, then `ITenantContext` shall throw. |

### 3.3 EF Core layer

| ID | Requirement |
| --- | --- |
| R-07 | Every tenant-scoped entity shall have a named query filter `Tenant` evaluated per query against the current tenant. |
| R-08 | When two consecutive scopes for different leagues query the same table, each scope shall return only its own league's rows. |
| R-09 | When an entity with an empty `TenantId` is added, the system shall assign the current tenant before saving. |
| R-10 | If an entity being added has a `TenantId` different from the current tenant, or an entity being modified changes its `TenantId`, then the system shall throw a specific exception and persist nothing. |

### 3.4 Database layer (RLS)

| ID | Requirement |
| --- | --- |
| R-11 | Every tenant-scoped table shall have RLS enabled with a policy that limits `SELECT`, `INSERT`, `UPDATE` and `DELETE` to rows whose `tenant_id` equals the transaction-local setting `app.tenant_id`. |
| R-12 | When a transaction starts on the GoalFlow `DbContext`, the system shall set, with transaction scope (`set_config(…, true)`), `app.tenant_id` if a tenant is set and `app.user_id` if an authenticated identity is set. |
| R-23 | If a transaction is requested with neither a tenant nor an identity set, then the system shall throw a specific exception: through the unit-of-work port, before the database connection is opened; through a direct `BeginTransaction` on the `DbContext`, before any transaction is created and before any command is executed. |
| R-24 | If `SaveChanges` is called while no transaction is active, then the system shall throw a specific exception and send nothing to the database. |
| R-25 | Every unit of work on tenant-scoped tables or on `admins_liga` shall run inside an explicit transaction opened through a port in Application; the system shall not rely on transactions that EF Core opens on its own. |
| R-27 | When a slug is resolved, the system shall read `ligas` through its own port without opening a transaction and without requiring a tenant or an identity. |
| R-28 | While no transaction is active, a query on a tenant-scoped table shall return no rows, even if a tenant is set in `ITenantContext`. |
| R-13 | While no transaction-local `app.tenant_id` is set, a query by `goalflow_app` on a tenant-scoped table shall return no rows and shall not raise an error, including on a connection that previously ran a committed transaction for another league. |
| R-14 | If `goalflow_app` inserts or updates a row with a `tenant_id` different from `app.tenant_id`, then the database shall reject the statement. |

### 3.5 Global tables

| ID | Requirement |
| --- | --- |
| R-15 | The table `ligas` (identifier, unique slug, name) shall have no tenant policy, and `goalflow_app` shall have only `SELECT` on it. |
| R-16 | The table `admins_liga` (`user_id`, `tenant_id`, `rol`) shall have RLS by identity: `goalflow_app` shall see only rows whose `user_id` equals the transaction-local setting `app.user_id`, and no rows while it is not set. |
| R-26 | While a transaction has an identity and no tenant, the system shall allow reading the user's own `admins_liga` rows and shall throw the missing-tenant exception on any query of a tenant-scoped entity. |
| R-17 | If `goalflow_app` attempts `INSERT`, `UPDATE` or `DELETE` on `ligas` or `admins_liga`, then the database shall reject it. |
| R-18 | `goalflow_app` shall have no privileges on the EF Core migrations history table. |
| R-29 | The `tenant_id` columns of `equipos` and `admins_liga` shall have foreign keys to `ligas(id)` with `ON DELETE RESTRICT`; if a row references a league that does not exist, or a league that still has rows is deleted, then the database shall reject it. |
| R-30 | The system shall not create a new EF Core internal service provider per request or scope; `ManyServiceProvidersCreatedWarning` shall be configured to throw in the real registration. |
| R-31 | The API shall validate service lifetimes and registrations (`ValidateScopes` and `ValidateOnBuild`) in every environment, not only in Development; if a singleton depends on a scoped service, then the API shall fail to start. |

### 3.6 Tenant resolution and public read

| ID | Requirement |
| --- | --- |
| R-19 | When `GET /{slug}/equipos` is requested with an existing slug, the API shall return only that league's teams. |
| R-20 | If the slug does not exist, then the API shall respond `404`. |

### 3.7 Migrations

| ID | Requirement |
| --- | --- |
| R-21 | Tables, RLS policies and revokes shall be created by EF Core migrations in schema `goalflow`. |
| R-22 | When CI runs on `main` or on a pull request, it shall generate the idempotent SQL script of all migrations and publish it as an artifact. CI shall never connect to the production database to apply it. |

## 4. Non-functional constraints

- Isolation tests run against PostgreSQL **17** in a disposable container. Setup (role script `db/roles/001_roles.sql` and migrations) runs as the container superuser; isolation tests connect as `goalflow_app`. Covered by T-09 to T-15.
- Test data uses invented slugs and names (for example `liga-a`, `liga-b`). No real league, team or person names in the repository.
- No connection string, password or generated test password is committed or logged.
- Application never references `DbContext` (ADR-03 §4): reads and the tenant transaction go through ports. Covered by the existing architecture tests.
- **Raw SQL allowlist** (ADR-14 C-10). Production code may run raw SQL only in migrations (policies, revokes) and in the transaction interceptor's `set_config`. Isolation tests may run raw SQL against the disposable test database; that is the only way to prove RLS on its own. Any other raw SQL needs a decision first. Spec 003 adds the architecture test for this list.
- The `DbContext` and its interceptors live in a shared persistence namespace of Infrastructure that belongs to no module (ADR-14 §2.3).
- **Destructive test statements never persist:** every test case that expects the database to reject a write (T-13, T-22) runs it inside a transaction that is always rolled back. Otherwise a regression that lets the write through also changes the shared test database and breaks unrelated tests, as seen in the mutation audit (removing the `REVOKE`s also failed T-13 read and T-15). Covered by T-13 and T-22.
- **Lifetimes:** `ITenantContext`, the identity port, the `DbContext` and the interceptors are scoped, one instance per request or job scope. DbContext pooling is not used. No singleton may hold or capture scoped state; a singleton interceptor would keep the first scope's tenant, the same failure as a captured filter value. Covered by T-05 and T-17.
- Adding the container-based tests must keep the CI job under 10 minutes (approved by the owner on 2026-10-30). The measured duration is reported in the PR.

## 5. Clarifications

| # | Question | Decision | Decided by / date |
| --- | --- | --- | --- |
| C-01 | EF Core filter only, or filter plus RLS? | Both now (ADR-14 C-01). | Owner, 2026-10-30 |
| C-02 | How is the tenant set for RLS? | Transaction-local `set_config(…, true)`; session level is forbidden (ADR-14 §2.3). | Owner, 2026-10-30 |
| C-03 | What happens without a tenant? | Application throws (R-05); the database returns no rows (R-13). Both layers fail closed. | Owner, 2026-10-30 |
| C-04 | Where do `Liga` and `AdminLiga` live? | New `Ligas` module; Tenancy stays minimal (ADR-14 §2.8). | Owner, 2026-10-30 |
| C-05 | Can the API modify league or Admin assignments? | No. `SELECT` only; assignments are created by SQL as `postgres` (ADR-14 §2.7). | Owner, 2026-10-30 |
| C-06 | Which database do isolation tests use? | Real PostgreSQL 17 in a container. In-memory providers and SQLite do not run RLS, policies or PostgreSQL types. | Owner, 2026-10-30 |
| C-07 | How are migrations applied in production? | CI publishes the idempotent script; the owner applies it as `postgres` (ADR-14 §2.10, resolves ADR-05 C-02). | Owner, 2026-10-30 |
| C-08 | Why is `nullif(…, '')` in the policies? | After a transaction-local setting ends, `current_setting(…, true)` returns `''`, and casting `''` to `uuid` raises an error. T-12 checks this. | Mentor, 2026-10-30 |
| C-09 | Is `Equipo` the real team entity, and why a public endpoint now? | `Equipo` is the minimal shape needed to prove isolation; the domain-model Spec extends it. `GET /{slug}/equipos` is the smallest read that exercises slug resolution end to end. | Owner, 2026-10-30 (mentor proposal) |
| C-10 | Npgsql TLS mode in production (ADR-05 C-05)? | Resolved in the implementation handoff after checking current Npgsql and Supabase documentation; never weaker than `Require`. Production wiring only: the tests do not depend on it. | Pending (handoff) |
| C-11 | Does the interceptor run for a `SaveChanges` without an explicit transaction? | Not reliably: with EF Core's default `WhenNeeded`, a single-statement save opens no transaction. Hence R-24 and R-25. | Owner, 2026-10-30 |
| C-12 | How is `admins_liga` read before the tenant is known? | The interceptor sets tenant, identity or both, and throws only with neither (R-12, R-23). Admin routes set the tenant from the slug first (ADR-14 §2.6). | Owner, 2026-10-30 |
| C-13 | Raw SQL in isolation tests? | Allowed by the allowlist in §4 (ADR-14 C-10). | Owner, 2026-10-30 |
| C-14 | Are the two layers independent? | Only against missing filters, policies or context; a wrong tenant value passes both (ADR-14 C-11). T-15 covers resolution; Spec 003 covers membership. | Owner, 2026-10-30 |
| C-15 | How is a public league resolved with neither tenant nor user? | The slug resolver reads `ligas` without a transaction and without context; it is the only read outside the unit-of-work port (R-27, ADR-14 C-12). | Owner, 2026-10-30 |
| C-16 | Can the system guarantee that the connection is never opened when context is missing? | Only through the unit-of-work port, which checks before using EF Core. On a direct `BeginTransaction`, EF Core opens the connection before the transaction-starting hook runs, so the guarantee there is: no transaction created and no command executed (R-23, T-18). Raised by the owner on 2026-10-30. | Owner, 2026-10-30 |
| C-17 | What happens to a league's rows when the league is deleted? | `ON DELETE RESTRICT`: the database refuses. Deleting a league is a deliberate SQL operation by `postgres` that must remove or archive its data first. `CASCADE` (EF Core's default for a required relationship) would erase a whole league's data with one statement, and referential actions run with the table owner's rights, outside RLS. | Raised by the owner; mentor proposal, 2026-10-31 |
| C-18 | Why validate DI lifetimes in Production too? | The mutation audit registered the transaction interceptor as a singleton: 19 tests failed, but only because the tests run in Development, where ASP.NET Core validates scopes and registrations by default. In Production it validates nothing, so the app would start and the singleton would receive scoped dependencies resolved from the root provider, shared by every request. What happens then depends on the code: with this interceptor, the root `ITenantContext` is never set, so every transaction would fail at runtime; a singleton that sets or caches tenant state would share it across requests and leak it. Validation turns both cases into a startup failure. | Owner and mentor, 2026-10-31 (mutation audit) |

## 6. Tests

Each test lists the mutation that must turn it red. Before running a mutation, the owner writes the prediction and the reason.

| Test ID | Covers | Description | Must fail when |
| --- | --- | --- | --- |
| T-01 | R-01 | Types in any `GoalFlow.*.Tenancy` namespace are exactly `TenantId`, `ITenantContext` and the missing-tenant exception. | A fixture adds another type to a Tenancy namespace (canary). |
| T-02 | R-02 | `TablaDeModulos` has `Ligas → Tenancy`, and the module-graph rule passes on production assemblies. | Canary fixture: a `Torneos` type uses a `Ligas` entity. |
| T-03 | R-03 | The existing rule "Domain without frameworks" gains a canary fixture `DomainConEfCore` with an EF Core type. | The fixture is removed from the canary, or the rule stops checking `Microsoft.EntityFrameworkCore`. |
| T-04 | R-05, R-06 | Unit tests: reading an unset context throws; setting a different second value throws; setting the same value twice is allowed. | `Current` returns `Guid.Empty` or a nullable instead of throwing. |
| T-05 | R-07, R-08 | Using the application's real DI registration: create a scope for league A, then a scope for league B from the same service provider; each sees only its own `Equipo` rows. | The filter captures a local value in `OnModelCreating` instead of referencing a `DbContext` member, or the tenant context or an interceptor is registered as a singleton. |
| T-06 | R-09 | Adding an `Equipo` with empty `TenantId` persists it with the current tenant. | The interceptor stops assigning the tenant. |
| T-07 | R-10 | Inside an explicit tenant transaction: adding an `Equipo` with another league's `TenantId` throws the application's specific exception, and the row does not exist afterwards. Changing `TenantId` on a tracked entity also throws. | The tenant guard of the `SaveChanges` interceptor is removed (RLS still rejects the insert, but with a different exception, so the assertion on the exception type fails). |
| T-08 | R-24 | Connected as `goalflow_app`, `SaveChanges` with a new `Equipo` and **no** active transaction throws the application's specific exception, and the row does not exist afterwards. | The no-transaction guard is removed (RLS rejects the insert with a database error instead, so the exception-type assertion fails). |
| T-17 | R-12, R-25 | Connected as `goalflow_app`, the unit-of-work port opens a transaction for league A and an `Equipo` is saved and committed; reading it back in a new transaction for A finds it. | The transaction interceptor is removed (RLS rejects the insert). |
| T-18 | R-23 | Case 1, through the unit-of-work port: the specific exception is thrown and a test connection interceptor counts **zero** connection openings. Case 2, direct `BeginTransaction` on the `DbContext`: the specific exception is thrown, a test command interceptor counts **zero** executed commands, and EF Core's `TransactionStarted` event is logged **zero** times; a positive control in the same test, with a tenant, logs it once. `Database.CurrentTransaction` being `null` is not enough: EF Core assigns it after the transaction-started hook, so it is also `null` when the check runs too late. The connection may already be open in case 2: EF Core opens it before raising the transaction-starting hook. | Case 1: the port no longer checks before using EF Core. Case 2: the check moves from the transaction-starting hook to the transaction-started hook (the event is then logged once). |
| T-19 | R-26 | Transaction with identity U1 and no tenant: reading `admins_liga` returns only U1's rows; querying `Equipo` throws the missing-tenant exception. | The interceptor does not set `app.user_id` (no rows), or the filter returns empty instead of throwing. |
| T-20 | R-27 | Connected as `goalflow_app`, with neither tenant nor identity set, the slug-resolver port returns league A for `liga-a` and nothing for an unknown slug, without throwing. | The resolver opens its read through the unit-of-work port (the interceptor throws for missing context). |
| T-21 | R-28 | Connected as `goalflow_app`, with tenant A set but **no** transaction, an EF Core query on `Equipo` returns no rows. Inside a transaction for A, the same query returns A's rows. | `app.tenant_id` is set when the connection opens or at session level (rows appear without a transaction). |
| T-22 | R-29 | (1) Connected as `goalflow_app`, in a transaction whose `app.tenant_id` is a random uuid that is not a league, inserting an `equipos` row with that same `tenant_id` is rejected with a foreign-key violation (SQLSTATE `23503`), not an RLS error. The same check runs as superuser on `admins_liga`. (2) **Each foreign key is checked on its own**, as superuser, inside a transaction that is always rolled back: a new league with **only** a team cannot be deleted (`23503`, constraint `fk_equipos_ligas`), and a new league with **only** an Admin cannot be deleted (`23503`, constraint `fk_admins_liga_ligas`). A league that has both is not enough: one `RESTRICT` key would block the delete even if the other one cascaded. (3) The catalog confirms it: in `pg_constraint`, both constraints have `confdeltype = 'r'` (restrict). | The foreign keys are missing from the migration, or **either** of them is created with `ON DELETE CASCADE` (EF Core's default for a required relationship). |
| T-23 | R-30 | With the real registration, resolve the `DbContext` and run one query in each of 30 scopes from the same service provider (above EF Core's warning threshold) without an exception. | Canary mutation: give each scope its own `ILoggerFactory` through the options, which forces a new internal service provider per scope. |
| T-24 | R-31 | With the API host in the `Production` environment: (1) `GET /health` returns `200`, so validation does not break a correct registration; (2) with an extra test-only singleton whose constructor takes `ITenantContext`, starting the host throws. The outer exception type is not asserted: `ValidateOnBuild` wraps the errors in an `AggregateException`, and the host may wrap it again. The test walks the exception chain (`InnerException` and `AggregateException.InnerExceptions`) and asserts that it contains an `InvalidOperationException` whose message names both the test singleton and `ITenantContext`. | The explicit validation settings are removed from `Program.cs`: in `Production`, ASP.NET Core validates nothing by default, so case 2 starts without error. |
| T-09 | R-11 | Raw SQL as `goalflow_app`, no EF Core: inside a transaction with `app.tenant_id` = A, `select` returns only A's rows. | The policy is dropped or RLS is disabled on the table. |
| T-10 | R-11 (canary) | The same query as the container superuser returns rows from both leagues. | Setup does not create rows for both leagues, or the test connects as `goalflow_app` (the test would measure nothing). |
| T-11 | R-14 | Raw SQL as `goalflow_app`, transaction with `app.tenant_id` = A: inserting a row with `tenant_id` = B is rejected by the database with SQLSTATE `42501` **and** a message that names a row-level security policy. | The policy's check expression allows other tenants (for example `WITH CHECK (true)`). |
| T-12 | R-13 | On **one connection kept open** for the whole test (never returned to the pool, so Npgsql cannot reset the session): (1) record `pg_backend_pid()`; (2) in a transaction for league A, assert A's rows are visible (precondition) and commit; (3) outside any transaction, assert the same `pg_backend_pid()` and query the table: no rows and no error. A second variant closes and reopens the connection with `Maximum Pool Size=1` and `No Reset On Close=true`, and also asserts the same backend PID. | `set_config` uses `is_local = false` (A's rows leak), or `nullif` is removed from the policy (cast error). Without the open connection or `No Reset On Close`, Npgsql's session reset would clear the leaked value and hide the first mutation: that is the false positive this design avoids. |
| T-13 | R-15, R-17, R-18 | As `goalflow_app`: `select` on `ligas` works; `insert`, `update` and `delete` on `ligas` and `admins_liga` are rejected; any access to the migrations history table is rejected. Each rejection asserts SQLSTATE `42501` **and** a message starting with `permission denied for table`. The SQLSTATE alone is not enough: an RLS rejection uses the same code, so without the message check the `INSERT` on `admins_liga` stays green when its `REVOKE` is removed (found in the mutation audit, 2026-10-30). | The migration does not revoke the privileges granted by default privileges: all 8 cases must turn red. |
| T-14 | R-16 | As `goalflow_app`: with `app.user_id` = U1, `admins_liga` returns only U1's rows; without `app.user_id`, it returns none. | The identity policy is dropped or RLS is disabled on `admins_liga`. |
| T-15 | R-19, R-20 | API over a real database: `GET /liga-a/equipos` returns only A's teams; `GET /no-existe/equipos` returns `404`. | The slug resolver does not set the tenant (request fails), or resolves a fixed league. |
| T-16 | R-21, R-22 | CI generates the idempotent migration script and uploads it as an artifact; the script contains the RLS policies and the revokes. | The migration omits the raw SQL for policies or revokes. |

Canaries already present in this table: T-01, T-02, T-03 and T-10 are explicit canary fixtures; the "must fail when" column gives the mutation for the rest. Mutations to run and record in the PR, at minimum: T-05 (captured filter value), T-08 (guard removed), T-12 (`is_local = false`) and T-13 (revokes removed).

## 7. Out of scope

- Admin authentication, token validation, the authorization transaction that sets `app.user_id` from a real token, and `403` for an Admin of another league (Spec 003, ADR-01).
- Any write endpoint.
- The architecture test that forbids `IgnoreQueryFilters` and raw SQL outside an allowlist (Spec 003).
- SuperAdmin endpoints (ADR-14 §2.9).
- The SQL script that creates the real leagues and Admins in production (run by the owner after Spec 003).
- Caching of the slug lookup.

## 8. Definition of done

- [ ] All tests above pass and none was modified to make it pass.
- [ ] Every requirement maps to at least one test (see validation table).
- [ ] No new warnings; no secrets in the diff.
- [ ] ADR-03 (§6 Tenancy, §7 `Ligas` row, §3 transitive Domain reference), ADR-05 (C-02) and `CLAUDE.md` updated in the same PR.
- [ ] PR description contains the validation table with real results and the CI duration.
- [ ] The migration script artifact was reviewed by the owner before being applied to production.

## 9. Validation (filled at PR time)

| Requirement | Test | Result | Evidence |
| --- | --- | --- | --- |
| R-01 | T-01 | | |
| … | … | | |

## 10. Risks and open items

- **Temporary exception: no rate limit on `GET /{slug}/equipos`** (decided by the owner on 2026-10-30). `CLAUDE.md` requires a rate limit on public endpoints; it is deferred because a per-IP limit is only correct once the real client IP behind Render and Cloudflare is verified (ADR-05 R-15).
  - **Owner:** the project owner.
  - **Deadline:** before the frontend is public, and no later than 2026-11-16.
  - **Pending tests:** (1) the API uses the real client IP in Render, (2) a client cannot evade or redirect the limit with a forged `X-Forwarded-For`, (3) exceeding the limit returns HTTP `429`.
  - Until then, the endpoint's data is public and small, and the database pool (`Maximum Pool Size`) bounds the load it can put on the database.

- **Transaction overhead:** every unit of work adds one statement. Measured once the API reads from the database in production.
- **Memory and cold start:** re-measure after EF Core is connected (baseline in ADR-05 §9).
- **C-10:** Npgsql TLS mode, resolved in the implementation handoff.
- **Pool Size (ADR-05 C-03):** must be read from Supabase before production wiring.

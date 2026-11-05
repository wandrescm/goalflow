# Spec 004: Post-save pipeline (durable outbox)

- **Status:** Draft
- **Date:** 2026-11-04 (revised twice the same day after the owner's reviews: R-05, R-09, R-10, R-12, R-14, R-23, R-26, R-32, R-44, R-45; T-01, T-04, T-09, T-12, T-13, T-23, T-25, T-31 to T-35; C-10 to C-16). Revised on 2026-11-05 after the mutation audit: R-24, R-46; T-36, T-37; C-17 to C-21; M16, M17.
- **Related:** ADR-02 (post-save pipeline), ADR-03 (amended §7: `Mensajeria`), ADR-14 (amended §2.3: tenant enumeration), ADR-05 (C-03 connection budget, R-11), Spec 002
- **Effort:** High for the claim, lease and loop (`opusplan`); medium for the rest

> Requirement and test IDs are local to this Spec ("Spec 004 T-07").

## 1. Context

Saving or correcting a match must trigger work in other modules without making the Admin wait and without losing that work on a restart. ADR-02 decides a transactional outbox (one delivery row per consumer), a `Channel` that only wakes the worker, one processing loop, claims by compare-and-swap with `ExecuteUpdateAsync`, leases by the database clock and fencing by lease token. `Partido` does not exist yet, so this Spec proves the mechanism with a **test-only** event and consumers; the real producer arrives in H12. This Spec also adds two cross-cutting rules: event contracts carry only identifiers, and every tenant-scoped entity and table has its filter and its policy (ADR-02 C-13).

## 2. Goal

A delivery committed with its producer's transaction is completed exactly once by the database state machine, survives a host restart, is never visible to another league, and is never claimed by two workers at once. This is proven against PostgreSQL 17 as `goalflow_app`, with no new raw SQL outside the ADR-14 allowlist.

## 3. Requirements (EARS)

### 3.1 Contracts and placement

| ID | Requirement |
| --- | --- |
| R-01 | The system shall place the outbox in the shared namespace `Mensajeria` (`GoalFlow.Application.Mensajeria` for ports and contracts, `GoalFlow.Infrastructure.Mensajeria` for adapters), listed in `TablaDeModulos.NamespacesCompartidos` and in ADR-03 §7. |
| R-02 | Every integration event shall implement the event contract of `Mensajeria`. It carries exactly `TenantId`, `AgregadoId` (`Guid`) and `Version` (`int`) as instance properties, plus a stable event name that is a constant, not the CLR type name. |
| R-03 | Every consumer shall implement the consumer contract of `Mensajeria` for one event type and expose a stable name that is a constant. |
| R-04 | The tenant-source port shall be declared in `GoalFlow.Application.Mensajeria` and implemented in `GoalFlow.Infrastructure.Ligas`. It shall return only the identifiers of all leagues, reading `ligas` without a transaction and without context (ADR-14 §2.3, second exception). |

### 3.2 Publishing

| ID | Requirement |
| --- | --- |
| R-05 | When an event is published, the system shall add one delivery row per consumer registered for that event type to the producer's own `DbContext` and shall not call `SaveChanges`, so that the rows are written by the producer's `SaveChanges` in the producer's transaction. |
| R-06 | If an event is published while no transaction is active on the `DbContext`, then the publishing port shall throw `TransaccionRequeridaException` and add nothing. |
| R-07 | If the producer's transaction rolls back, then no delivery row of that publication shall exist. |
| R-08 | When an event type has no registered consumer, the publishing port shall add no row and log at `Information`. |
| R-09 | If a delivery with the same logical key `(tenant_id, event_type, aggregate_id, aggregate_version, consumer)` already exists, then the producer's `SaveChanges` shall throw the duplicate-delivery exception of `Mensajeria`, translated by the `SaveChanges` override of `GoalFlowDbContext`, with the original exception as inner exception. Only SQLSTATE `23505` on the logical-key index is translated; any other database error passes through unchanged. The transaction stays usable (EF Core savepoint, ADR-02 fact 10). |
| R-10 | When a transaction in which events were published commits, a scoped transaction interceptor of `Mensajeria` shall send one hint per published tenant with `TryWrite`, without waiting for channel capacity. When that transaction rolls back or is disposed without commit, it shall send none. The producer contains no notification code. If a hint cannot be written, the commit is unaffected. |

### 3.3 Delivery table

| ID | Requirement |
| --- | --- |
| R-11 | The system shall store deliveries in `goalflow.entregas_eventos`, created by a migration run as `postgres`, with: `tenant_id` and a foreign key to `ligas` (`ON DELETE RESTRICT`); the EF Core filter named `Tenant`; an RLS policy limited to `app.tenant_id`; and a unique index on the logical key. All of them are created in the same migration. |
| R-12 | `goalflow_app` shall have `SELECT`, `INSERT` and `UPDATE` on `entregas_eventos`, and shall not have `DELETE`. The migration revokes `DELETE` explicitly, because the default privileges in `db/roles/001_roles.sql` grant it on every new table. |
| R-13 | The delivery entity shall expose its tenant as a property named `TenantId` of type `TenantId`, so that the existing `SaveChanges` guard validates it (ADR-14 §2.5). |
| R-14 | The last-error column shall hold only the exception type and, for the permanent-failure exception, the reason code given by the consumer. It shall never hold an exception message. Logs shall carry the delivery id, tenant, event name, consumer, attempt, the exception type, the types of its inner exceptions and the stack trace, and shall never carry an exception message, at any log level. The exception object shall not be passed to `ILogger`. |

### 3.4 Processing loop

| ID | Requirement |
| --- | --- |
| R-15 | When the host starts, the processing loop shall run a first pass without waiting for a hint or the timer. |
| R-16 | After each pass, the loop shall wait until either a hint arrives or the timer period elapses (`Mensajeria:PeriodoDelTemporizador`, 60 s), and then run another pass. |
| R-17 | In each pass, the loop shall enumerate tenants through the tenant-source port. It shall process them one at a time, each in its own DI scope with the tenant set from the enumerated value, and shall never hold more than one database connection. |
| R-18 | The channel shall be bounded (`Mensajeria:CapacidadDelCanal`, 64), with `BoundedChannelFullMode.Wait` and a single reader. |
| R-19 | The hosted service shall resolve every scoped dependency inside the scope it creates, and shall capture none of them. |
| R-20 | When the host stops, the loop shall stop claiming and cancel in-flight consumers, and shall write nothing for the cancelled deliveries: no state change, no attempt change, no release. |
| R-21 | Where `Mensajeria:CicloHabilitado` is `false`, the hosted service shall not run any pass. This switch exists for tests; production uses `true`. |
| R-44 | If any exception other than shutdown cancellation occurs during a pass, then the loop shall log it at `Warning` (type only, R-14), shall not stop the host, and shall run the next pass as usual. |

### 3.5 Claim and lease

| ID | Requirement |
| --- | --- |
| R-22 | The loop shall claim deliveries in a short explicit transaction opened through the unit-of-work port, with no consumer work inside it. It reads at most `Mensajeria:LotePorTenant` (10) candidates and claims each one with an `ExecuteUpdateAsync` conditioned on availability. |
| R-23 | A delivery shall be available when its consumer is registered in the running host **and** it is either `Pendiente` with next attempt time `<=` now, or `EnProceso` with lease expiry `<` now. |
| R-24 | A successful claim shall, in the same statement, set `EnProceso`, a new lease token, lease expiry = now + `Mensajeria:DuracionDelLease` (5 min) and attempts = attempts + 1. Its condition shall include both the availability of R-23 and the attempts read with the candidate, which must be unchanged and below the maximum. It succeeds only when the statement affected exactly one row. |
| R-25 | If two workers try to claim the same available delivery at the same time, then exactly one shall succeed. |
| R-26 | Every timestamp on `entregas_eventos` (availability comparisons, lease expiry, next attempt time, completion time) shall be computed by the database clock: `DateTime.UtcNow` inside the LINQ expression, translated by Npgsql to `now()`. Creation time shall come from a column default of `now()`. The application may send durations as parameters, never a timestamp. |
| R-27 | If a claim, completion or failure statement on `entregas_eventos` is attempted while no transaction is active, then the adapter shall throw `TransaccionRequeridaException` before sending any statement. |
| R-28 | When an available delivery's attempts have already reached `Mensajeria:MaximoDeIntentos` (5), the claim transaction shall set it to `Fallida`, with the same compare-and-swap, without running the consumer. |

### 3.6 Processing and outcomes

| ID | Requirement |
| --- | --- |
| R-29 | After the claim commits, each claimed delivery shall be processed in a new DI scope with its tenant set. The consumer receives the event rebuilt from the row (tenant, aggregate id, version) and the attempt number. No transaction is open during consumer work. |
| R-30 | Each consumer call shall be cancelled after `Mensajeria:TimeoutDelConsumidor` (2 min). |
| R-31 | When the consumer returns normally, the system shall set `Completada` and the completion time, conditioned on `id`, state `EnProceso` and the worker's lease token. |
| R-32 | When the consumer throws any exception other than the permanent-failure exception, or its timeout expires, the system shall set `Pendiente` with next attempt time = database `now()` + min(30 s × 2^(attempts − 1), 30 min) and record the last error, under the same condition as R-31. Only the backoff duration is computed by the application. |
| R-33 | When the consumer throws the permanent-failure exception of `Mensajeria`, the system shall set `Fallida` and record the last error, under the same condition as R-31. |
| R-34 | If a completion or failure statement affects zero rows, because the lease was taken over, then the system shall log at `Warning` and shall not retry the statement. |
| R-35 | One consumer's failure shall not change the state of another consumer's delivery for the same event. |
| R-36 | When a delivery becomes `Fallida`, the system shall log at `Warning` with the delivery id, tenant, event name, consumer and attempts. |

### 3.7 Startup validation

| ID | Requirement |
| --- | --- |
| R-37 | If two registered consumers share a name, then the API shall fail to start. |
| R-38 | If `TimeoutDelConsumidor` is not shorter than `DuracionDelLease`, or any `Mensajeria` value is not positive, then the API shall fail to start. |
| R-45 | A delivery whose consumer is not registered in the running host shall not be claimed, failed or changed by that host. |
| R-46 | No type in `GoalFlow.Infrastructure` shall depend on a `Microsoft.AspNetCore` namespace, even though the project references the ASP.NET Core shared framework (ADR-03 §5). |

### 3.8 Architecture rules

| ID | Requirement |
| --- | --- |
| R-39 | Every type in a production assembly that implements the event contract shall have exactly the properties of R-02 and shall live in a `.Contracts` sub-namespace of a module. |
| R-40 | Every entity type in the real `GoalFlowDbContext` model that has a `TenantId` property shall have the query filter named `Tenant`, except `AdminLiga` (identity policy, ADR-14 §2.7). The exception list lives in the test and in this Spec. |
| R-41 | Every table in schema `goalflow` with a `tenant_id` column shall have RLS enabled and at least one policy, verified against the migrated test database. |
| R-42 | Every application `IMemoryCache` registered in the API's real container shall have an explicit `SizeLimit`. EF Core's internal cache is out of scope (Spec 002 T-23). |

### 3.9 Production check (manual, by the owner)

| ID | Requirement |
| --- | --- |
| R-43 | After the migration script is applied as `postgres` and the merge commit is Live on Render, the logs shall show the loop's first pass completed over the existing leagues with zero deliveries, and `goalflow_app` shall be able to `SELECT` from `entregas_eventos`. The previous version has neither the table nor that log line, so this check tells the two versions apart. |

## 4. Non-functional constraints

- **No new raw SQL in production code.** Claims, completions and failures use `ExecuteUpdateAsync`; the only new raw SQL is the policy and grants inside the migration (ADR-14 C-10). Tests of R-11, R-12 and R-41 may use raw SQL against the disposable database.
- **Real database:** tests of persistence, claims and isolation run against PostgreSQL 17 in the existing container, connected as `goalflow_app`. Concurrency tests use two independent scopes, `DbContext` instances, connections and transactions.
- **Configuration keys** under `Mensajeria:` with the values of ADR-02 §2.8, as `TimeSpan` for durations, including `BackoffInicial` (30 s) and `BackoffMaximo` (30 min); tests use short values (for example a 2 s lease and a 1 s period).
- **No new packages.** `System.Threading.Channels` and the hosting abstractions are part of the shared framework; no Polly (ADR-02 C-08).
- **Test doubles:** the test event and consumers live only in `GoalFlow.Tests`. No production fake event, no football entity and no endpoint are added.
- **Timing in tests:** waits use polling with a timeout (at most 30 s per test), never fixed sleeps as assertions. The CI job stays under 10 minutes; the measured duration is reported in the PR.
- **Logs:** structured `ILogger`: delivery id, tenant, event name, consumer, attempt, exception type, inner exception types and stack trace. Never an exception message, at any level (R-14).
- **The `SaveChanges` guard, the transaction interceptor and DI validation in all environments** stay as they are; this Spec adds nothing that bypasses them.

## 5. Clarifications

| # | Question | Decision | Decided by / date |
| --- | --- | --- | --- |
| C-01 | Build now or with H12? | Now, with a test-only event and consumers (ADR-02 C-01). | Owner, 2026-11-04 |
| C-02 | What does the event carry? | Exactly `TenantId`, `AgregadoId` and `Version`, so that the worker can rebuild it from the row and the consumer re-reads current state (ADR-03 §6). Any other property fails R-39. | Mentor, 2026-11-04 |
| C-03 | Why a stable event and consumer name instead of the CLR type name? | Rows outlive refactors: a renamed class must not orphan pending deliveries. | Mentor, 2026-11-04 |
| C-04 | How is R-25 tested without timing luck? | Worker A claims inside an open transaction and does not commit. Worker B's claim on the same row blocks on the row lock. A commits; B's statement re-evaluates its `WHERE` and affects zero rows (ADR-02 fact 6). The test controls the order; it does not race two tasks and hope. | Mentor, 2026-11-04 |
| C-05 | How is the restart (T-03) told apart from in-memory processing? | Host 1 runs with `CicloHabilitado = false`, publishes and commits three deliveries, sends hints, and is disposed. Host 2 starts on the same database with the loop enabled and completes all three. With an in-memory-only queue, host 2 has nothing to process. | Owner and mentor, 2026-11-04 |
| C-06 | Why reuse `TransaccionRequeridaException` for R-06 and R-27? | It already means "this operation needs an explicit transaction" (Spec 002 R-24). Reusing it keeps one meaning per exception. | Mentor, 2026-11-04 |
| C-07 | The event-contract rule (R-39) has no production event to check until H12. Is a green result meaningful? | Only together with its canaries (T-25), which prove that the rule detects a violation. The production run is vacuous until H12, and the PR says so. | Mentor, 2026-11-04 |
| C-08 | Why `AdminLiga` as the only exception in R-40? | Its isolation is by identity, not by tenant (ADR-14 §2.7). `Liga` has no `TenantId` property: its key is the tenant. | Mentor, 2026-11-04 |
| C-09 | Does the backfill of A-01 belong here? | No. It is an obligation of H14 (ADR-02 §2.3 and C-16). This Spec only provides the duplicate-delivery exception and proves that an idempotent caller, with one scope per aggregate, can rely on it (T-13). | Owner, 2026-11-04 |
| C-10 | Where is the duplicate violation translated, and what state does it leave? | In the `SaveChanges` override; the transaction survives through EF Core's savepoint, and the failed rows stay `Added` (ADR-02 C-17, C-18). | Owner, 2026-11-04 (review) |
| C-11 | Why can T-01 not catch a `SaveChanges` inside the publishing port? | If the port saves on the same `DbContext` inside the producer's transaction, the rollback undoes that save too, and T-01 stays green. T-31 counts the calls instead. | Owner, 2026-11-04 (review) |
| C-12 | Is a truncated message safe? Is `Debug` safe? | No to both. Messages are never stored and never logged, at any level (ADR-02 C-19, C-21). | Owner, 2026-11-04 (two reviews) |
| C-14 | Which clock sets the next attempt time? | The database clock; the application sends only the backoff duration (ADR-02 C-22). | Owner, 2026-11-04 (second review) |
| C-15 | How is "one connection at a time" (R-17) proven? | T-34: a connection interceptor tracks the maximum number of connections open at once across scopes, and a run with `Maximum Pool Size = 1` must complete. Any overlap (for example, the consumer running while the claim connection is still held) times out. | Owner and mentor, 2026-11-04 (second review) |
| C-16 | Why must a host ignore deliveries of consumers it does not know? | During a deploy, the old instance would otherwise fail the rows of a consumer added by the new version; in tests, a host would claim rows left by other tests (ADR-02 C-23). | Mentor, 2026-11-04 |
| C-17 | Does the claim compare the attempts read? | Yes (R-24, ADR-02 C-25); proposed by Claude Code during implementation. | Owner, 2026-11-05 (audit) |
| C-18 | Is the availability condition in the `UPDATE` redundant with the attempts check? | No. M1 was caught only by T-09, through a helper that re-reads the row. T-36 covers the real case: a stale candidate after its previous owner completes or retries (ADR-02 C-26). | Owner, 2026-11-05 (audit) |
| C-19 | Infrastructure references the ASP.NET Core shared framework. Is that allowed? | Yes, for host and options types only; R-46 and T-37 forbid `Microsoft.AspNetCore` types (ADR-03 §5). | Owner, 2026-11-05, option (a) |
| C-21 | Why did M17 survive the first version of T-37? | The mutant type had no namespace, and the rule selected types with `ResideInNamespaceStartingWith("GoalFlow")`, copied from the Domain rule. A rule's selection is part of the rule: a filter can exclude exactly the violation. T-37 now checks assembly references, which do not depend on namespaces, and all types. T-02, T-04 and T-05 have the same filter, but each is backed by a check that does not depend on it: T-01 and T-03 check assembly references, and a reference from Infrastructure to Api would be a project cycle. | Mentor and owner, 2026-11-05 (audit) |
| C-20 | Why did M15 turn tests outside T-35 red? | Tests share one container, and `goalflow_app` cannot delete rows, so rows from earlier tests remain. Isolation between tests relies on unique consumer names **and** on R-45. Without the filter, a test's oldest-first batch of 10 fills with other tests' rows, and claiming an unknown consumer's row aborts the pass. That is detection, not flakiness, but a failure of R-45 shows up in unrelated tests. | Mentor, 2026-11-05 (audit) |
| C-13 | Who sends the hint? | A transaction interceptor after the commit (ADR-02 C-20); T-32 pins the moment. | Owner and mentor, 2026-11-04 (review) |

## 6. Tests

| Test ID | Covers | Description | Must fail when |
| --- | --- | --- | --- |
| T-01 | R-07 | A test producer, in a transaction for league A, adds an `Equipo` (the existing tenant-scoped table stands in for the future match) and publishes a test event with two registered consumers, then rolls back. Neither the `Equipo` nor any delivery exists. The same with commit: two deliveries exist, both `Pendiente`, with attempts 0. | Publishing writes through another scope or connection (its rows survive the rollback), or fan-out writes one row instead of two. A `SaveChanges` inside the same transaction is **not** detected here (C-11); T-31 covers it. |
| T-02 | R-06 | Publishing with no active transaction throws `TransaccionRequeridaException`, and the change tracker has no delivery entry. | The no-transaction check is removed. |
| T-03 | R-15, R-21 | Restart (C-05): host 1 with the loop disabled publishes three deliveries and is disposed. Host 2 with the loop enabled completes all three; the test consumer records three calls. | Recovery depends on the channel: host 2 completes zero. |
| T-04 | R-10, R-18 | With a channel of capacity 1 that is already full, a test producer publishes and commits: the commit returns within 1 s, the delivery exists, and the channel still holds only the old hint. The real registration builds the channel with capacity 64, `Wait` and a single reader. | The interceptor awaits `WriteAsync` (the commit blocks), or the channel is unbounded. |
| T-05 | R-16, R-10 | With the loop running and idle after its first pass, a delivery is committed **without** a hint (as if the hint had been dropped by a full channel): it is completed within two timer periods. | The timer is missing; only hints trigger passes, so a dropped hint loses the delivery until the next restart. |
| T-06 | R-17, R-29 | Leagues A and B with one delivery each: the loop completes both, and the test consumer records each call with the tenant of its own delivery. In a scope for A, a query on `entregas_eventos` returns only A's row, and a claim attempt on B's row id affects zero rows. | The loop uses one scope for all tenants, or sets the tenant from anything other than the enumerated value. |
| T-07 | R-25 | Controlled concurrency (C-04): A claims without committing; B's claim on the same row waits; A commits; B affects zero rows. Exactly one lease token is stored. | The claim's `WHERE` drops the availability condition (both succeed), or claims read then save without a condition. |
| T-08 | R-25 | Like T-07, but A rolls back instead of committing: B's claim then succeeds with one row. | Availability is decided outside the statement (B gives up because of what it read earlier). |
| T-09 | R-23, R-24, R-26, R-32 | With a 2 s lease: a fresh claim blocks another claim (zero rows); after the lease expires, the other claim succeeds and attempts = 2. The generated SQL of the claim, of the failure and of the completion statements uses `now()` and has **no** parameter of type `DateTime` or `DateTimeOffset` (duration parameters are allowed). The row's creation time was set by the database. | The lease is ignored, attempts are not incremented at claim, or any timestamp comes from the instance clock (a timestamp parameter appears). |
| T-10 | R-31, R-34 | Fencing: worker A claims; the lease expires; worker B claims; A completes with its old token → zero rows and a `Warning`; the row is still `EnProceso` with B's token; B completes → `Completada`. | Completion filters by `id` only (A's completion succeeds). |
| T-11 | R-27 | A claim with no active transaction throws `TransaccionRequeridaException`, and a command interceptor counts zero statements sent to `entregas_eventos`. | The check is removed: the claim silently affects zero rows under RLS, which looks like a lost race. |
| T-12 | R-09 | A delivery is committed. In a new transaction, publishing the same logical key and calling `SaveChanges` throws the duplicate-delivery exception, with a `PostgresException` `23505` as inner exception. A query in the same transaction afterwards still works (savepoint); disposing the transaction without commit leaves exactly one row. A different violation (a delivery whose `tenant_id` is not a league, rejected by the foreign key) passes through as `DbUpdateException`, not translated. | The override translates every `DbUpdateException`, or none, or `AutoSavepointsEnabled` is turned off (the follow-up query fails because the transaction is aborted). |
| T-13 | R-09 (C-09) | A test-only idempotent caller ("ensure this delivery exists": one DI scope and one transaction per aggregate, an existence check, the duplicate exception caught and counted as done, the scope discarded) runs twice concurrently for the same key, with the order controlled as in C-04. Exactly one row exists, and neither run throws. | The caller relies on the existence check only (the second run fails), or the exception is not specific. |
| T-14 | R-32, R-28, R-36 | A consumer that always throws, with maximum attempts 3 and short backoff: it runs 3 times; next attempt times grow; the next available pass sets `Fallida` without a fourth run, and a `Warning` is logged. | Attempts are counted only on failure, the backoff is constant, or a fourth run happens. |
| T-15 | R-24, R-28 | Abandoned claims: a test worker claims and never completes (it simulates a crash), with maximum attempts 3 and a 2 s lease. After 3 claims, the row is `EnProceso` with attempts 3; after the lease expires, the next pass sets `Fallida` and the consumer has run 0 extra times. | Attempts are incremented after processing (the abandoned claims count nothing, so it loops forever). |
| T-16 | R-33 | A consumer that throws the permanent-failure exception: one run, then `Fallida`. | The permanent failure is retried like any other exception. |
| T-17 | R-30, R-32 | A consumer that honours cancellation but would run for 10 s, with a 1 s timeout, is cancelled; the delivery returns to `Pendiente` with attempts 1. | No timeout is applied (the consumer runs until the lease expires). |
| T-18 | R-20 | While a consumer runs, the host stops: the consumer's token is cancelled, and the row stays `EnProceso` with the same attempts and lease token. | The cancellation is treated as a retryable failure (the row becomes `Pendiente`). |
| T-19 | R-35 | Two consumers of the same event, one always failing: the healthy one ends `Completada`, the other keeps its own attempts. | Rows are shared, or one consumer's failure stops the pass. |
| T-20 | R-08 | Publishing an event type with no consumers adds no row and logs at `Information`. | It throws, or writes a row with no consumer. |
| T-21 | R-37, R-38 | Startup fails for: two consumers with the same name; timeout equal to the lease; a zero capacity. Startup succeeds with the default values. | Validation is missing or runs only in Development. |
| T-22 | R-11, R-12 | Raw SQL against the test database, as `goalflow_app`: `DELETE` on `entregas_eventos` is rejected with permission denied (SQLSTATE `42501` **and** a permission message, not an RLS one). Inside a transaction for A, inserting a row with `tenant_id` = B is rejected by RLS (`42501` with a row-level security message). | The grant includes `DELETE`, or the policy is missing. Checking only the SQLSTATE would not tell the two cases apart (Spec 002 C-16). |
| T-23 | R-14 | A consumer throws an exception whose message contains a marker string, first as a retryable failure and then as a permanent failure with reason code `prueba`. The stored last error contains the exception type (and `prueba` in the second case) and never the marker. With logs captured at `Trace` (every level), no entry contains the marker, and at least one entry contains the exception type name. | The message is stored (even truncated) or logged at any level, or the exception object is passed to `ILogger`. The type-name check proves the capture is real, so a green result does not just mean nothing was logged. |
| T-24 | R-19 | With the host in `Production`, DI validation passes with the hosted service registered; a test-only variant of the hosted service that takes the `DbContext` in its constructor fails at startup. | Validation is disabled, or the hosted service captures scoped state. |
| T-25 | R-39, R-02 | The event-contract rule passes on production assemblies (vacuous until H12, C-07). Canaries, each failing the rule on its own: an event with an extra `string` property; an event outside `.Contracts`; an event whose public `Version` is a `string` (with the contract member implemented explicitly); an event whose public `AgregadoId` is an `int`. | The rule checks property names but not types, or ignores the namespace. |
| T-26 | R-40 | The rule over the real model passes. Canary: a fixture `DbContext` with an entity that has `TenantId` and no `Tenant` filter fails, naming that entity. | The rule skips entities outside Domain, or the exception list grows silently. |
| T-27 | R-41 | Catalog query on the migrated test database: every `goalflow` table with `tenant_id` has `relrowsecurity` and at least one policy. Canary: a fixture table with `tenant_id`, created in the test transaction without RLS and then rolled back, fails the rule. | The query checks only `equipos`, or only RLS without a policy. |
| T-28 | R-42 | The cache rule on the real container passes. Canaries: a container with `AddMemoryCache()` and no `SizeLimit` fails; with `SizeLimit` it passes. | The rule finds zero caches and reports green without the canary. |
| T-29 | R-01 | `TablaDeModulos.NamespacesCompartidos` contains `Mensajeria`, and the module-graph rule passes on production assemblies. Canary: an existing graph fixture with a module using another module's entity through `Mensajeria` still fails. | `Mensajeria` is added as a module, or the graph rule ignores types in it. |
| T-30 | R-04 | As `goalflow_app`, with neither tenant nor identity, the tenant-source adapter returns the ids of A and B, opens no transaction (zero transaction starts counted) and does not throw. | It opens a transaction (the interceptor throws for lack of context), or it reads more than identifiers. |
| T-31 | R-05 | With a test interceptor that counts `SaveChanges` calls: publishing a test event with two consumers makes zero calls, and the change tracker holds two delivery entries in state `Added`. | The publishing port calls `SaveChanges` itself (M9), which T-01 cannot see (C-11). |
| T-32 | R-10 | Three cases on a channel observed by the test. (a) After publishing and after `SaveChanges`, but before the commit: zero hints. After the commit: exactly one hint, for tenant A. (b) Publish, `SaveChanges`, rollback: zero hints. (c) Publish, `SaveChanges`, dispose without commit: zero hints. | The hint is sent from the publishing call or from `SaveChanges` (case a sees it before the commit), or is not discarded on rollback or dispose (b, c). |
| T-33 | R-44 | The tenant-source port is replaced by a test double that throws on its first call and works afterwards, with a 1 s timer period. The host is still running after the first pass; a `Warning` with the exception type is logged; a delivery is completed on a later pass. | An exception escapes `ExecuteAsync` (the host stops, by .NET's default `StopHost` behaviour). |
| T-34 | R-17, R-29 | Two tenants with two deliveries each, and a test consumer that reads `Equipo` through its own unit of work. (1) A connection interceptor that counts opens and closes across all scopes records a maximum of **1** connection open at once. (2) The same run with `Maximum Pool Size = 1` and a 5 s connection timeout completes all four deliveries. | The consumer runs while the claim's connection or transaction is still open, or tenants are processed in parallel (case 2 times out). |
| T-35 | R-45, R-23 | A delivery for consumer `no-registrado` and one for a registered consumer, both available: the pass completes only the registered one; the other stays `Pendiente` with attempts 0, even after several passes. | The claim does not filter by registered consumer names (the unknown row is claimed and failed, or retried). |
| T-36 | R-23, R-24 | Stale candidate (C-18). Worker A claims with a 2 s lease and commits. After the lease expires, worker B reads the candidate (attempts 1). Before B claims: (a) A completes with its token, so the row is `Completada`; then B's claim affects zero rows and the row stays `Completada`. (b) In a second case, A retries with its token and a 60 s backoff, so the row is `Pendiente` with a future next attempt time; then B's claim affects zero rows and attempts stay 1. | The availability condition is removed from the claim's `UPDATE` (M1): B revives a completed delivery or ignores the backoff. |
| T-37 | R-46 | Two checks, neither filtered by namespace. (a) **References:** the compiled `GoalFlow.Infrastructure` assembly references no assembly whose name starts with `Microsoft.AspNetCore`; the compiler emits a reference only for assemblies actually used, so this holds whatever namespace a type is in. Canary: the same check on the test assembly, which uses `Microsoft.AspNetCore.Mvc.Testing`, fails. (b) **Types:** no type of the assembly, **including types in the global namespace**, depends on `Microsoft.AspNetCore`; the failure names the type. Canary: a fixture type that uses `HttpContext` fails. | The check filters types by a `GoalFlow` namespace (M17 survived this way, C-21), or checks only public types. |

**Canaries in this table:** T-25 to T-29 have explicit canary fixtures; T-22 and T-27 distinguish their failure causes; T-23 proves its log capture is real.

**Mutations to run and record in the PR** (the owner predicts the number of red tests and why before each one):

- M1: drop the availability condition from the claim's `WHERE`.
- M2: complete by `id` only, without the lease token.
- M3: count attempts on failure instead of on claim.
- M4: compute expiry with an instance-clock parameter.
- M5: remove the active-transaction check in the claim adapter.
- M6: the producer uses `WriteAsync` instead of `TryWrite`.
- M7: remove the timer (hints only).
- M8: treat a shutdown cancellation as a retryable failure.
- M9: the publishing port calls `SaveChanges` itself.
- M10: translate every `DbUpdateException` to the duplicate exception.
- M11: send the hint from the publishing call instead of the committed hook.
- M12: store the exception message, truncated to 500 characters, in the last-error column.
- M13: run each consumer before committing the claim transaction.
- M14: let exceptions escape the loop (remove the catch around a pass).
- M15: remove the registered-consumer condition from the claim (and from the candidate listing, so that it is not masked).
- M16 (after the audit): M1 again, to show that T-36 now detects it.
- M17 (after the audit): a type in `GoalFlow.Infrastructure`, with no namespace, that uses `HttpContext`. It survived the first version of T-37 (C-21) and is run again after the fix.

## 7. Out of scope

- The real producer `PartidoGuardado` and the atomicity test with a real match (H12).
- A-01, the LLM client, its budget and its backfill (H13–H14, ADR-12).
- Polly or any in-memory retry.
- A retention job or `DELETE` on deliveries.
- An Admin view of failed deliveries.
- The architecture rule for `IgnoreQueryFilters` and raw SQL (S3D1).

## 8. Definition of done

- [ ] All tests above pass and none was modified to make it pass.
- [ ] Every requirement (R-01 to R-46) maps to at least one test or to the production check (R-43).
- [ ] Mutations M1–M17 run, with the owner's predictions frozen beforehand and results in the PR.
- [ ] ADR-02 committed; ADR-03 §7 and ADR-14 §2.3 and C-12 amended in the same PR.
- [ ] Migration script generated by CI as an artifact; it contains the policy and the grants.
- [ ] No new warnings; no secrets in the diff; no new raw SQL outside the migration.
- [ ] PR description contains the validation table with real results and the CI duration.

## 9. Validation (filled in the closing commit)

> While the PR is under review, this evidence lives in the PR description. After the production check, a closing docs commit fills this table with real results, marks §8 and sets the status to Implemented.

| Requirement | Test | Result | Evidence |
| --- | --- | --- | --- |
| R-01 | T-29 | | |

## 10. Risks and open items

- **No processing-time bound** while Render's free service sleeps (ADR-02 §2.8). Accepted.
- **At-least-once delivery:** every future consumer must handle redelivery explicitly (ADR-02 §2.7). For A-01, see ADR-12.
- **Timing-based tests** (T-04, T-05, T-09, T-14, T-15, T-17) could be flaky in CI. They use polling with a timeout and short configured values; if any flakes, it is fixed in the test's synchronization, never by widening an assertion.
- **The event-contract rule is vacuous** on production code until H12 (C-07).
- **Memory and cold start** with EF Core connected are still unmeasured (Estado §9); the hosted service adds one more background task.

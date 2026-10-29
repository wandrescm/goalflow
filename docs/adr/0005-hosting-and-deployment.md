# ADR-05: Backend hosting, database connectivity and operations

- **Status:** Accepted
- **Date:** 2026-10-28
- **Related:** ADR-03 (modular monolith), ADR-07 (.NET 10 LTS), ADR-09 (frontend hosting), ADR-02 (post-save pipeline, pending), multi-tenancy ADR (pending)

## 1. Context

GoalFlow's API is an ASP.NET Core (.NET 10) service backed by PostgreSQL on Supabase. The stack in `CLAUDE.md` already names Render for the backend and Supabase for the database; this ADR fixes *how* they are used.

Facts that shape the decision (checked against provider documentation on 2026-10-27):

1. Render has no native .NET runtime; .NET services are deployed as Docker images.
2. Render does not support outbound IPv6.
3. Supabase's direct database endpoint is IPv6-only unless a paid IPv4 add-on is enabled. The shared pooler (Supavisor) is reachable over IPv4 on every plan; its session mode (port 5432) is the documented option for persistent backends on IPv4-only networks.
4. In Supavisor session mode, the number of concurrent clients is capped by the project's "Pool Size" setting.
5. Through the shared pooler, the built-in admin role connects as `postgres.<project-ref>` and a custom role as `<role>.<project-ref>`. On hosted Supabase, `postgres` has `CREATEROLE` and `CREATEDB`, and tables it owns bypass row-level security unless it is forced.
6. Supabase's auto-generated Data API (PostgREST) exposes the `public` schema by default, and the anonymous key is public by design (it ships in frontend code). A table in `public` without row-level security is readable and writable by anyone holding that key. With the Data API disabled, its endpoints do not respond regardless of grants or RLS.
7. Render's free web instances spin down after 15 minutes without inbound traffic, can be restarted at any time, and have an ephemeral filesystem. Paid instances do not spin down but still restart on every deploy.
8. Render web services are served over TLS by Render's proxy; the container receives plain HTTP on the port Render assigns.
9. ASP.NET Core's forwarded-headers middleware, by default, only trusts proxies on loopback and applies one hop (`ForwardLimit = 1`).
10. A Render deploy hook accepts a `ref` query parameter with a commit SHA and deploys exactly that commit.
11. Supabase's free plan pauses a project after one week of inactivity and has no managed backups.
12. Inside a container with a memory limit, the .NET GC sets a default heap hard limit of 75% of that limit.
13. The repository is public: anyone signed in to GitHub can download workflow artifacts, and scheduled workflows are disabled automatically after 60 days without repository activity.
14. When a deploy is triggered while another is in progress, Render's workspace-level overlapping deploy policy decides: **Override** (default) cancels the in-progress deploy; **Wait** lets it finish and then runs only the most recently triggered deploy.
15. `pg_dump` sets `row_security = off` by default: if the dumping role cannot bypass row-level security on a table, the dump fails with an error instead of exporting only the visible rows. `--enable-row-security` changes this to a partial export. Supabase's own guidance for backups is a dedicated login role with `BYPASSRLS` and read-only defaults, connecting through the shared pooler as `<role>.<project-ref>`.

## 2. Decision

### 2.1 Runtime and image

- The API runs on a Render **web service** built from a multi-stage `Dockerfile` at the repository root: `mcr.microsoft.com/dotnet/sdk:10.0` to build and publish, `mcr.microsoft.com/dotnet/aspnet:10.0` to run, as the image's non-root user.
- Kestrel listens over plain HTTP on the port given by the `PORT` environment variable that Render injects.
- Region: **Virginia (US East)**. The Supabase project lives in the matching AWS region (**US East, North Virginia**). API and database must share a region because one request issues several sequential queries, so the API-to-database round trip is multiplied per request while the browser-to-API round trip is paid once.
- Instance: free instance during development; switched to the lowest always-on instance before real users access the system.

### 2.2 Database connectivity

- The API connects through **Supavisor session mode (port 5432)**, host `aws-…pooler.supabase.com`. The direct endpoint (`db.<project-ref>.supabase.co`) is not used from Render: it shares port 5432 but is IPv6-only.
- Transaction mode (port 6543) is not used: it may hand each transaction a different server connection, which breaks connection-scoped state such as prepared statements.
- Npgsql's `Maximum Pool Size` is set **below** the project's Supavisor Pool Size (initial value: 10), so the API cannot exhaust the pooler.
- Connection strings are supplied only through environment variables (Render) and repository secrets (GitHub Actions). They never appear in the repository.

### 2.3 Database roles and exposure (least privilege)

The database is the last line of defence if the API has a bug (for example a missing tenant filter or an injection). The API therefore does not connect as an administrator.

| Role | Used by | Privileges |
|---|---|---|
| `postgres` (built-in) | Owner only: SQL editor, migrations from CI or local | Admin. Never used by the running API. |
| `goalflow_app` | The API on Render; the keep-alive workflow | `LOGIN`, `NOBYPASSRLS`. `USAGE` on schema `goalflow`; `SELECT, INSERT, UPDATE, DELETE` on its tables; `USAGE, SELECT` on its sequences. No DDL, no `CREATE`, no access to other schemas. |
| `goalflow_backup` | The backup workflow | `LOGIN`, **`BYPASSRLS`**, `default_transaction_read_only = on`. `USAGE` on schema `goalflow` and `SELECT` on its tables and sequences; nothing else. |

- The API's username in the connection string is **`goalflow_app.<project-ref>`**.
- Application tables live in a **private schema `goalflow`**, not in `public`.
- **The Supabase Data API is disabled.** GoalFlow reads and writes data only through its own API; no client talks to PostgREST.
- Supabase's `service_role` key is not used. Note: it is a key for Supabase's HTTP APIs, not a Postgres login, so "not using it" says nothing about the privileges of the Npgsql connection; those come from the role above.
- Roles are created with SQL run as `postgres`, by a versioned script in the repository that contains no passwords. Passwords are set by the owner outside the repository and stored only in the password manager, Render and GitHub secrets.
- **Why the backup role bypasses RLS while the API role does not.** A backup must contain every row of every tenant; a role subject to RLS would make `pg_dump` fail (or, with `--enable-row-security`, export a silently incomplete copy). The API, in contrast, must never see more than its tenant allows. The two roles have opposite needs, so they get opposite attributes. The backup role's real limit is its grants (`SELECT` only); `default_transaction_read_only` is a safety default a session can turn off, not a security boundary.
- **Tenant isolation is not decided here.** This ADR only guarantees that the API runs with limited privileges. Whether isolation is enforced by EF Core's global filter alone or also by row-level security per tenant is decided in the multi-tenancy ADR.

### 2.4 Transport security (TLS) and the proxy

There are three hops, each with its own rule:

| Hop | Encryption | Who handles it |
|---|---|---|
| Browser → Render | HTTPS | Render's proxy (managed certificate) |
| Render proxy → API container | Plain HTTP inside Render's network | Kestrel on `PORT`; no certificate in the container |
| API or CI workflow → Supabase | TLS | Npgsql / `psql` / `pg_dump` with TLS required |

**Forwarded headers.** Behind the proxy, every request appears to come from the proxy over `http`. The API runs the forwarded-headers middleware **first in the pipeline**, processing `X-Forwarded-For` (client IP) and `X-Forwarded-Proto` (original scheme). The client IP matters because public endpoints are rate-limited per client.

- By default the middleware only trusts loopback proxies, and Render's proxy is not on loopback, so the defaults would silently ignore the headers. Render's proxy addresses are not fixed, so the known-proxy and known-network lists are **cleared** in production.
- Clearing them is acceptable only because the container is reachable **exclusively** through Render's proxy.
- `ForwardLimit` stays at **1**: only the last entry, added by the proxy, is used. A value a client injects in its own `X-Forwarded-For` lands earlier in the list and is ignored.
- No TLS certificate or private key is stored in the image or the repository.
- Every database connection (API, keep-alive, backup) requires TLS. A connection that cannot negotiate TLS fails instead of falling back to plain text.

### 2.5 Restarts and in-memory state

- The system shall not depend on in-memory state surviving a restart. Anything held only in memory (the bounded `Channel` and the cache from ADR-03) must be rebuildable from the database.
- A restart may drop queued post-save work after the match itself is committed. Recovery of that work is ADR-02's responsibility; derived data is recomputed idempotently from saved matches, as `CLAUDE.md` requires.

### 2.6 Memory

- The default GC heap limit (75% of the container limit) is kept; no explicit `GCHeapHardLimit` is set until measurements justify one.
- Initial bounds: cache `SizeLimit` of 1,000 entries; post-save queue capacity of 500 events. Both are guards against unbounded growth, not sizing for current data.
- After the first deployment, the API's idle and post-request memory are read from Render's metrics and recorded in this ADR (§9).

### 2.7 Deployment pipeline (deploy by SHA)

- Render auto-deploy is **off**.
- On a push to `main`, the `build-test` job runs. Only if it passes, a `deploy` job calls the service's deploy hook with **`ref=<commit SHA that just passed>`** (`github.sha`). Render builds and deploys exactly that commit, never "whatever is newest on `main`".
- Why this matters: without the SHA, if commit A passes CI while commit B (not yet tested) has already landed on `main`, the hook would deploy B. With the SHA, an untested commit cannot reach production.
- The deploy hook URL is a repository secret; it is never printed in logs.
- **What each layer guarantees about overlapping deploys:**
  - GitHub Actions: deploy jobs share one concurrency group without cancelling the running job. This only serializes the **hook calls**, in order; the job ends as soon as Render accepts the request, not when the deploy finishes. If several runs queue up, GitHub keeps only the newest pending one.
  - Render: the workspace's overlapping deploy policy is set to **Wait**, so a deploy in progress is never cancelled halfway; when it finishes, Render runs only the most recently triggered deploy.
  - Net guarantee: every deployed commit passed CI, and after a burst of merges the service ends on the newest tested SHA. Intermediate SHAs may be skipped. CI does **not** confirm that Render's deploy succeeded; that is checked in Render's dashboard (polling Render's API from CI is deferred, §6).
- The API exposes `GET /health` (liveness, no database access). Render's health check points to it.
- CORS allows only the frontend origin, read from configuration (see ADR-09).
- CI runs on a pinned runner image (`ubuntu-24.04`), not `ubuntu-latest`.

### 2.8 Keep-alive

- **Scope: the Supabase database only.** A scheduled GitHub Actions workflow runs `select 1` as `goalflow_app` **every 3 days**, through the session pooler with TLS, so the free project never reaches a week of inactivity, including weeks without matches.
- It does not call the API and does not keep Render awake. The API on Render is not kept awake artificially during development.
- It does not exercise Supabase Auth or any other Supabase service.

### 2.9 Backups and restore

- A scheduled workflow runs `pg_dump` of schema `goalflow` **daily** as `goalflow_backup`, through the session pooler with TLS.
- `pg_dump` runs **without** `--enable-row-security`, on purpose: if anything ever prevents reading all rows, the dump fails loudly instead of producing a partial backup that looks valid.
- The dump is **encrypted before it leaves the runner** with `age`, using a public key stored as a repository variable. The private key never touches GitHub; it is kept only by the project owner.
- The encrypted dump is uploaded as a workflow artifact with **14-day retention**. An unencrypted dump is never uploaded or logged.
- **Weekly restore test:** the owner downloads the latest backup, decrypts it, restores it into a local PostgreSQL container and checks row counts of core tables against production. The test also checks the **date of the latest backup**: if it is older than one day, the scheduled workflows have stopped (see §5) and are re-enabled. The result is recorded outside the repository.

## 3. Requirements (EARS)

- **R-01** The API shall connect to PostgreSQL through the Supavisor session-mode endpoint on port 5432.
- **R-02** The API shall read its connection string only from configuration supplied by the environment.
- **R-03** The API shall listen on the port given by the `PORT` environment variable.
- **R-04** When `GET /health` is requested, the API shall respond `200 OK` without accessing the database.
- **R-05** When a commit reaches `main` and `build-test` passes, CI shall call the Render deploy hook with that commit's SHA as `ref`.
- **R-06** If `build-test` fails, CI shall not trigger a deploy.
- **R-07** The keep-alive workflow shall query the database at least every 3 days.
- **R-08** The backup workflow shall produce a daily dump encrypted with `age` before upload.
- **R-09** The backup workflow shall not upload, print or cache an unencrypted dump.
- **R-10** Encrypted backup artifacts shall be retained for 14 days.
- **R-11** When the API restarts, the system shall be able to rebuild all in-memory state from the database (resolved by ADR-02).
- **R-12** The API shall accept cross-origin requests only from the configured frontend origin.
- **R-13** Every connection to the database from the API or from CI workflows shall use TLS; if TLS cannot be negotiated, the connection shall fail.
- **R-14** When a request arrives through Render's proxy, the API shall use the client IP and scheme added by the proxy.
- **R-15** If a client sends its own `X-Forwarded-For` value, the API shall not use that value as the client IP.
- **R-16** The running API shall connect as `goalflow_app`, which shall not be able to create, alter or drop database objects.
- **R-17** Application tables shall live in schema `goalflow`, outside any schema exposed by the Supabase Data API.
- **R-18** While the Supabase Data API is disabled, a request to its REST endpoint with the anonymous key shall not return application data.
- **R-19** The backup workflow shall export every row of schema `goalflow`, including tables with row-level security enabled.
- **R-20** If `pg_dump` cannot read every row, the backup workflow shall fail rather than upload a partial dump.
- **R-21** `goalflow_backup` shall not be able to insert, update or delete data, or create, alter or drop objects.

## 4. Consequences

**Positive**
- Works on free tiers without IPv4 add-ons; moving to an always-on instance is a dashboard change, not a redesign.
- A bug in the API cannot drop tables or create roles, and the public anonymous key cannot reach application data.
- Only a commit that passed CI can be deployed, and the deployed SHA is visible in the workflow log.
- Backups exist despite the free database plan, and a public repository does not expose them.
- No certificates to manage.

**Negative / risks**
- Three roles and a role script to maintain; every new table needs its grants (handled in the migration that creates it).
- Free instance: about one minute of cold start after 15 idle minutes. Acceptable in development, not with real users.
- Session mode caps concurrency at the pool size; enough for the pilot scale, revisited if load grows.
- Restore depends on the owner keeping the private key safe. Losing it makes every backup unreadable.
- The restore test is manual; skipping it silently weakens the backup guarantee.
- Clearing the trusted-proxy lists is safe only while the container has no public path other than Render's proxy.
- The backup role's credential can read every tenant's data. It lives only in a repository secret and the password manager, and is rotated if exposure is suspected.

## 5. Known operational risks

- **Scheduled workflows stop after 60 days without repository activity.** Keep-alive and backups would stop silently, and the database could pause a week later. Detection: the weekly restore test checks the latest backup date. Fix: re-enable the workflows in the Actions tab.
- **A paused free database** (for example if the keep-alive stopped) needs a manual restart from the Supabase dashboard.
- **Custom roles through the shared pooler:** community reports describe authentication failures for custom roles through Supavisor on some projects. The first deployment verifies `goalflow_app` end to end before anything depends on it (C-08).

## 6. Alternatives considered

- **Direct connection over IPv6:** not reachable from Render.
- **Supabase IPv4 add-on:** requires a paid Supabase plan; rejected.
- **Transaction mode (6543):** designed for serverless clients that open and close connections constantly; adds connection-state pitfalls for a persistent .NET backend with its own pool.
- **API connecting as `postgres`:** simplest, but gives a running process admin rights and bypasses row-level security on its own tables; rejected.
- **Keeping tables in `public` with RLS and no policies:** blocks the Data API too, but depends on every future table remembering to enable RLS. Disabling the Data API and using a private schema removes the exposure path entirely.
- **Render auto-deploy "After CI Checks Pass":** simpler (no hook secret) and also waits for green checks, but the deployed commit is chosen by Render, it does not deploy if no checks are detected, and the decision is not visible in our workflow. The explicit SHA was preferred for traceability; this remains a valid fallback.
- **CI polling Render's API until the deploy finishes:** would make CI report the real deploy result, but needs a Render API key as a secret and more workflow code. Deferred until a failed deploy goes unnoticed or real users depend on the service.
- **Deploy hook without `ref`:** deploys the newest commit on the branch, which may not be the one CI tested; rejected.
- **Plain (unencrypted) backup artifacts:** downloadable by anyone in a public repository; rejected.
- **Keeping the API awake with pings:** consumes nearly all of the free monthly instance hours and is unnecessary during development.

## 7. Clarifications

- **C-01** *Does `pg_dump` need a specific client version?* Yes: the client's major version must be equal to or newer than the server's. The server version is read from the Supabase dashboard when the backup workflow is built, and the workflow installs a matching client.
- **C-02** *When and how are EF Core migrations applied, and with which role?* Migrations need DDL, so they never run as `goalflow_app`. The mechanism (CI step or manual) is decided when the first migration exists (multi-tenancy work).
- **C-03** *What is the Supavisor Pool Size on the project?* Read from the Supabase dashboard during the first deployment; `Maximum Pool Size` must stay below it.
- **C-04** *Which frontend origin does CORS allow?* The production origin defined in ADR-09, plus the local development origin in the `Development` environment only.
- **C-05** *Npgsql TLS mode: encrypt only (`Require`) or encrypt and verify the server certificate (`VerifyFull`)?* Decided during the first deployment after checking current Npgsql behaviour and whether Supabase provides the CA certificate. `VerifyFull` is preferred if available; never weaker than `Require`.
- **C-06** *Does the keep-alive keep Render awake?* No. It touches only the database (§2.8).
- **C-07** *Does the container need HTTPS, and how is the forwarded-header setup verified?* No HTTPS in the container: Render terminates TLS. Verification happens at two levels, and neither replaces the other. Automated tests prove **our configuration**: with a fixed simulated proxy address, the last `X-Forwarded-For` entry wins, and a mutation (`ForwardLimit = 2`) must turn the spoofing test red. They cannot prove **Render's behaviour** (that its proxy appends the real client IP as the last entry). That is checked after deployment: send a request with a fake `X-Forwarded-For` value and confirm the API logs the real client IP, not the fake one. Whether the configuration is done in code or through the ASP.NET Core environment switch for forwarded headers is decided in the implementation; code is preferred because it is visible and testable.
- **C-08** *What if `goalflow_app` cannot authenticate through the shared pooler?* The deployment stops and the problem is diagnosed. Falling back to `postgres` is not automatic: it requires an explicit, dated decision by the owner recorded in this ADR.
- **C-09** *Does disabling the Data API affect Supabase Auth?* Auth is a separate service; this is verified when Admin login is implemented (multi-tenancy and auth work).
- **C-10** *Are Supabase Auth users (schema `auth`) covered by the backup?* No: the dump covers schema `goalflow` only. Losing the project would lose user accounts and the link between users and their data (for example prediction-game entries). The backup strategy for identities is decided before fan login is implemented.
- **C-11** *Is there a conflict between least privilege and backups once RLS exists?* Yes, and it is resolved by giving `goalflow_backup` `BYPASSRLS` while keeping it read-only by grants (§2.3). The conflict is latent today (no RLS on `goalflow` tables yet) but is fixed now so that enabling RLS in the multi-tenancy work cannot silently break backups. If Supabase ever refuses `BYPASSRLS` for a custom role, the fallback is decided then; `--enable-row-security` is not an acceptable fallback.

## 8. Implementation notes for the first deployment

- **Bootstrap order:** the Render service, its deploy hook secret and the overlapping deploy policy (**Wait**) are configured **before** the first merge that contains the `deploy` job. Otherwise that job fails on `main` by design (missing secret). The service's very first build, created before the `Dockerfile` reaches `main`, is expected to fail and is harmless.
- Create schema `goalflow` and the two roles with the versioned script, then disable the Data API in the Supabase dashboard (API settings).
- Verify R-16 with a negative test: `goalflow_app` attempting `CREATE TABLE` must fail.
- Verify R-18 with a negative test: a request to the REST endpoint with the anonymous key must not return data.
- Verify R-21 with a negative test: `goalflow_backup` attempting `INSERT` must fail.
- Canary for R-19/R-20 (once any table has RLS enabled): temporarily remove `BYPASSRLS` from `goalflow_backup` on a test run and confirm the backup workflow fails; restore the attribute afterwards.

## 9. Measurements (filled in after the first deployment)

| Metric | Prediction (owner) | Measured | Date |
|---|---|---|---|
| API memory at idle after startup | 120 MB | _pending_ | |
| API memory after first requests | _pending_ | _pending_ | |
| Cold start time on free instance | _pending_ | _pending_ | |

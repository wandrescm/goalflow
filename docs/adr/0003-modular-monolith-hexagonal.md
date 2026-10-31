# ADR-03: Modular monolith with Clean/Hexagonal architecture

- **Status:** Accepted
- **Date:** 2026-10-08 (S2D1)
- **Related:** ADR-07 (runtime), ADR-02 (post-save pipeline, pending), ADR-14 (multi-tenancy)
- **Amended:** 2026-10-30 (`Ligas` module, Tenancy content, transitive Domain reference)

Translated from Spanish on 2026-10-11; the decision is unchanged. Amended on 2026-10-30 together with ADR-14; the amendments are listed in the "Amendments" section.

## Context

GoalFlow is built and operated by a single person and deployed as a single process with limited memory (512 MB). Its capabilities (tournaments, statistics, prediction games, AI) change at different rates and must not become coupled by accident. The domain must be testable without infrastructure.

## Decision

1. **Main axis: modules by capability.** Tenancy, Ligas, Torneos, Estadisticas, Pollas and IA. The secondary axis is the layers.
2. **Four production projects plus tests:** `GoalFlow.Domain`, `GoalFlow.Application`, `GoalFlow.Infrastructure`, `GoalFlow.Api` and `GoalFlow.Tests`. Modules are namespaces inside each layer: `GoalFlow.<Layer>.<Module>` (for example `GoalFlow.Domain.Torneos`).
3. **Allowed references between projects (arrows point inward):**
   - `Api → Application` and `Api → Infrastructure` (the composition root sees both).
   - `Infrastructure → Application` (implements the ports defined in Application).
   - `Application → Domain`.
   - `Domain →` nothing.

   Infrastructure and Api use Domain types through the transitive reference that comes with Application; neither project adds a direct reference to Domain. The architecture tests check the direct references listed above.
4. **Ports and adapters.** Ports (interfaces) live in Application; adapters live in Infrastructure. Application does not use `DbContext`. Heavy reads, such as recomputing statistics from matches, go through read ports owned by the use case, not through a full-aggregate repository.
5. **Allowed external dependencies:**
   - Domain: none (no EF Core, no ASP.NET, no `Microsoft.Extensions.*`).
   - Application: only Domain and `Microsoft.Extensions.*.Abstractions` (for example `ILogger`).
6. **Communication between modules by events.** The emitting module (for example Torneos with `PartidoGuardado`) publishes the event to a port and does not know its consumers. The event carries identifiers (`TenantId`, `PartidoId`, version) and not match data: the consumer re-reads the current state, so a correction or a retry never processes stale data. A module may depend on another only through its `.Contracts` sub-namespace (events, read DTOs), never on its entities.
Exception: Tenancy is the shared kernel and may be used whole. It contains only the `TenantId` value (Domain) and the `ITenantContext` port (Application), plus the exception thrown when the tenant is missing, and it must not grow any further. League data (catalog, Admin membership) lives in the `Ligas` module (ADR-14 §2.8).
7. **Allowed module graph (each new row requires updating this ADR):**

   | Module | May depend on |
   | --- | --- |
   | Tenancy | nothing |
   | Ligas | Tenancy |
   | Torneos | Tenancy |
   | Estadisticas | Tenancy, Torneos.Contracts |
   | Pollas | Tenancy, Torneos.Contracts, Estadisticas.Contracts |
   | IA | Tenancy, Torneos.Contracts, Estadisticas.Contracts |

   An entry "X.Contracts" allows only that sub-namespace; an entry "X" (only Tenancy) allows the whole module.

   **Shared namespaces that are not modules:** `Hosting` (Api composition: endpoints, middleware, configuration) and `Persistencia` (the unit-of-work port in Application; the `DbContext`, interceptors and migrations in Infrastructure, ADR-14 §2.3). They are not evaluated as modules, and any module may use them. Adding another shared namespace requires updating this list and `TablaDeModulos.NamespacesCompartidos` in the same commit.

8. **Tenant context.** The abstraction for the current tenant is a port in Application. The `TenantId` is data of the message or command and never ambient thread state: each background job creates its own DI scope and sets the tenant from the message (the pipeline has no `HttpContext`).
9. **A single process with bounded memory.** Every in-memory queue is bounded (`Channel` with a maximum capacity and a wait policy) and every in-memory cache has a `SizeLimit`. The LLM provider is remote: it does not consume model memory, but it does use buffers for responses.

## Rejected alternatives

- **Simple N-tier without modules:** less structure, but capabilities get mixed and the drift is only noticed when it already hurts.
- **One project per layer and per module (≈ 20 projects):** a strong boundary enforced by the compiler, but the boilerplate cost does not fit a single developer's schedule.
- **Microservices:** over-engineering for a single developer and a single instance.
- **A repository per aggregate for everything:** works for writes, not for derived reads with their own shape.

## Consequences

- **(−)** The boundary between modules is not enforced by the compiler (they are namespaces): it depends on the architecture tests. If a module grows or becomes problematic, it is extracted into its own project; consistent namespaces make that step mechanical.
- **(−)** Extra discipline in events and commands: carry the tenant and the version in every message; consumers must be idempotent.
- **(+)** A domain that can be tested without infrastructure; read ports that keep Application decoupled from EF Core.

## Verification

Architecture tests in `GoalFlow.Tests/Architecture`: rules for references between layers, forbidden dependencies in Domain and Application, and the module-graph rule. Each rule is also tested against a *fixture* with a deliberate violation that must make it fail (canary); without that test, a green rule proves nothing.

## Amendments

- **2026-10-30 (with ADR-14):**
  - New module `Ligas` (league catalog and Admin membership), row `Ligas → Tenancy` in §7. Other modules receive the `TenantId` from the tenant context and do not depend on `Ligas`.
  - Tenancy reviewed: it holds only `TenantId`, `ITenantContext` and the missing-tenant exception (§6).
  - §3 states that Infrastructure and Api reach Domain through the transitive reference of Application.
  - §7 lists the shared namespaces `Hosting` and `Persistencia`, which are not modules.

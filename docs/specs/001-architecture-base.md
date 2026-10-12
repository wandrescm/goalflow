# Spec 001: Architecture base (layers, module graph, CI)

- **Status:** Implemented with known gaps (archived)
- **Date:** 2026-10-08
- **Related:** ADR-03 (modular monolith, Clean/Hexagonal), ADR-07 (.NET 10 LTS)
- **Effort:** High for the ADRs, Medium for implementation

> **Provenance.** This Spec was written before any implementation code and handed to Claude Code as the basis of PR #1. It is archived here after the fact so the repository shows the intent behind the code. If you are reading the history, compare it with the PR: differences are listed in section 10. Sections 6 and 9 were reconciled against the code after implementation; the original test table was reconstructed from memory and corrected.

## 1. Context

GoalFlow is a multi-tenant SaaS for neighborhood football tournaments, built and operated by one developer and deployed as a single process with limited memory. The repository started from the default .NET Web API template. Before any feature exists, the dependency rules must be executable, otherwise they erode silently.

## 2. Goal

A solution with four production projects plus tests, whose allowed dependencies are enforced by automated tests that are themselves proven able to fail, running in CI on every push and pull request.

## 3. Requirements (EARS)

| ID | Requirement |
| --- | --- |
| R-01 | The solution shall contain the projects `GoalFlow.Domain`, `GoalFlow.Application`, `GoalFlow.Infrastructure`, `GoalFlow.Api` and `GoalFlow.Tests`, all targeting `net10.0`. |
| R-02 | The project references shall be exactly: Api → Application, Infrastructure; Infrastructure → Application; Application → Domain; Domain → none; Tests → all four. |
| R-03 | If `GoalFlow.Domain` references any other GoalFlow assembly, ASP.NET, EF Core or `Microsoft.Extensions.*`, then the architecture tests shall fail. |
| R-04 | If `GoalFlow.Application` references Infrastructure, Api, EF Core or ASP.NET, then the architecture tests shall fail. `Microsoft.Extensions.*.Abstractions` (e.g. `ILogger`) is allowed. |
| R-05 | When a type in a module depends on another module, the architecture tests shall pass only if that dependency appears in the module graph table of ADR-03 §7. Cross-module access is allowed only through the `.Contracts` sub-namespace; Tenancy is a shared kernel and may be used whole. |
| R-06 | Each architecture rule shall have a canary test: a fixture with a deliberate violation that makes the rule report failure. |
| R-07 | The SDK shall be pinned in `global.json` to a .NET 10 SDK with `rollForward: latestPatch`. |
| R-08 | Nullable reference types and warnings-as-errors shall be configured once in `Directory.Build.props`. |
| R-09 | When code is pushed or a pull request is opened, CI shall restore, build and test the solution using the SDK declared in `global.json`. |

## 4. Non-functional constraints

- Architecture rules are shared functions used by both the real tests and the canary tests (one definition, two uses), so a canary exercises the same logic as the rule.
- The module table is data (`TablaDeModulos`), kept in sync with ADR-03 §7 in the same commit.
- CI workflow runs with `permissions: contents: read`.

## 5. Clarifications

| # | Question | Decision |
| --- | --- | --- |
| C-01 | Modules or layers as the main axis? | Modules by capability as the main axis, layers as the secondary one. |
| C-02 | One project per module and layer (~20 projects)? | No. Four projects by layer; modules are namespaces `GoalFlow.<Layer>.<Module>`. The compiler does not enforce module boundaries, tests do. |
| C-03 | May Application use `DbContext`? | No. Heavy reads go through read ports owned by the use case. |
| C-04 | How do modules communicate? | Events carrying identifiers and a version, not match data; consumers re-read current state. Background jobs receive `TenantId` in the message. |
| C-05 | Can Tenancy be consumed whole? | Yes, it is the shared kernel. Other modules only via `.Contracts`. |
| C-06 | `rollForward` policy? | `latestPatch`, so local machines and CI build with the same SDK band. |

## 6. Tests

Test IDs below follow the order in which the rules were specified. They map to the tests in `GoalFlow.Tests/Architecture`.

| Test ID | Covers | Test method | Description | Must fail when |
| --- | --- | --- | --- | --- |
| T-01 | R-03 | `ArquitecturaTests.Domain_no_referencia_nada_fuera_de_System_y_netstandard` | Every assembly referenced by Domain is `System*` or `netstandard` | Domain references any other assembly (GoalFlow layers, ASP.NET, EF Core, `Microsoft.Extensions.*`, packages) |
| T-02 | R-03 | `ArquitecturaTests.Domain_no_depende_de_EFCore_AspNetCore_ni_Extensions` | No Domain type depends on `Microsoft.EntityFrameworkCore`, `Microsoft.AspNetCore` or `Microsoft.Extensions` | A Domain type depends on any of those namespaces |
| T-03 | R-04 | `ArquitecturaTests.Application_solo_referencia_Domain_System_y_Extensions_Abstractions` | Application references only `GoalFlow.Domain`, `System*` and `Microsoft.Extensions.*.Abstractions` | Application references any other assembly (Infrastructure, Api, EF Core, ASP.NET, packages) |
| T-04 | R-04 | `ArquitecturaTests.Application_no_depende_de_Infrastructure_ni_Api` | No Application type depends on `GoalFlow.Infrastructure` or `GoalFlow.Api` | An Application type depends on Infrastructure or Api |
| T-05 | R-02 | `ArquitecturaTests.Infrastructure_no_depende_de_Api` | No Infrastructure type depends on `GoalFlow.Api` | An Infrastructure type depends on Api |
| T-06 | R-02 | `ArquitecturaTests.Infrastructure_referencia_Application_via_AssemblyAppVersion` | `AssemblyAppVersion` implements `IAppVersion`, and Infrastructure references `GoalFlow.Application` | Infrastructure does not reference Application, or the adapter does not implement the port |
| T-07 | R-06 | `ArquitecturaTests.Cada_ensamblado_de_produccion_tiene_al_menos_un_tipo` | Anti-empty guard: Domain, Application, Infrastructure and Api each contain at least one non-compiler-generated type | A production assembly is empty (rules over it would pass vacuously) |
| T-08 | R-06 | `CanariosTests.T01_canario_Domain_con_referencias_no_permitidas`, `T02_canario_Domain_con_framework` (2 cases: ASP.NET, Extensions), `T03_canario_Application_con_referencias_no_permitidas`, `T04_canario_Application_con_capa_externa` (2 cases: Infrastructure, Api), `T05_canario_Infrastructure_con_Api` | One canary per rule T-01..T-05, calling the same rule function on a deliberate violation (the T01 and T03 canaries use the test assembly itself; the others use fixtures in `Architecture/Fixtures`). The `T01`..`T05` prefixes name the rule being canaried | A rule reports success on its violating input |
| T-09 | R-05, R-06 | `CanariosTests.T09_canario_grafo_de_modulos_detecta_dependencia_no_permitida` | Module-graph rule (signature analysis via reflection) run on example fixtures: `EstadisticasViolaPorEntidad` depends on a Torneos entity and must be the single violation | The violating fixture is not detected, or the violation count is not exactly one |

Test IDs appear only in comments (`// T-0x`) in the code. Canary method names start with `T01`..`T05` to name the rule they canary; all of them belong to T-08.

## 7. Out of scope

- Any domain entity, endpoint, database or authentication.
- Deployment (Spec 002).
- Detecting forbidden calls inside method bodies (T-09 analyzes signatures only); tracked as known debt.

## 8. Definition of done

- [x] All tests pass and none was modified to make it pass.
- [x] CI green on the pull request and on `main`.
- [x] ADR-03 and ADR-07 accepted and committed in `docs/adr/`.
- [x] Manual mutation check recorded (section 9).

## 9. Validation

| Requirement | Test | Result | Evidence |
| --- | --- | --- | --- |
| R-01 | solution builds on net10.0 | pass | CI run on PR #1 |
| R-02 | T-05, T-06 (partial) | partial | Only Infrastructure → Api (forbidden) and Infrastructure → Application are tested. Api and Tests references are not covered (see section 10) |
| R-03 | T-01, T-02; canaries in T-08 | pass | 16/16 tests pass in CI (1 placeholder, 7 rules, 8 canary cases) |
| R-04 | T-03, T-04; canaries in T-08 | pass | idem |
| R-05 | T-09 (canary on fixtures only) | partial | The module-graph rule has not run on production code yet; no modules exist (see section 10) |
| R-06 | T-08, T-09 | pass | idem |
| R-07 | no test; verified by CI | pass | CI resolves the SDK from `global.json` |
| R-08 | no test; verified by build | pass | warnings-as-errors build in CI |
| R-09 | CI workflow | pass | PR run ≈ 49 s, `main` run ≈ 18 s |
| R-03 | T-01, T-02 under mutation | fail as expected | Added an ASP.NET reference to Domain: of the 7 rule tests, T-01 and T-02 went red and the other 5 stayed green; reverted, 16/16 green |

## 10. Risks, known gaps and differences from the plan

- The Tests project still carries template packages from .NET 8 and a placeholder test; cleaned up in Spec 002.
- `ubuntu-latest` migrates to Ubuntu 26 on 19 Oct 2026; CI will pin `ubuntu-24.04` (Spec 002).
- No canary yet for EF Core in Domain; planned with the persistence Spec.
- Module rules run against example fixtures; they will run against real modules once those exist.
- Infrastructure reaches Domain through Application's transitive reference; ADR-03 will state this explicitly.
- R-02 is only partially tested (Api and Tests references have no test).
- R-05 has no test over production code; it will get one when real modules exist.
- R-04 has no dedicated canary for EF Core or ASP.NET in Application; the T-03 canary uses the test assembly itself.

# GoalFlow

[![CI](https://github.com/wandrescm/goalflow/actions/workflows/ci.yml/badge.svg)](https://github.com/wandrescm/goalflow/actions/workflows/ci.yml)

Multi-tenant SaaS for neighborhood football tournaments: leagues, fixtures, standings, top scorers, and free prediction games for fans.

> **Status: work in progress.** This repository is a deliberate exercise in Specification-Driven Development (SDD) with AI-assisted implementation. The architecture foundation is done and tested; product features are being added one vertical slice at a time.

## What works today

- Four-project Clean/Hexagonal solution on **.NET 10 (LTS)**: `Domain`, `Application`, `Infrastructure`, `Api`, plus `Tests`.
- Architecture rules enforced as tests (NetArchTest), each rule paired with a **canary** test that must fail on a deliberate violation.
- CI on every push and pull request (build + test, SDK pinned by `global.json`).

Nothing else is implemented yet; the list above is the whole of it.

## Architecture

```
Api ──► Application ──► Domain
 │          ▲
 └► Infrastructure   (implements Application's ports)
```

- Modular monolith: modules (Tenancy, Torneos, Estadisticas, Pollas, IA) are namespaces inside each layer; cross-module access goes only through `.Contracts`.
- Standings and points are **derived** from saved matches and recomputed idempotently.
- Tenant isolation is a hard rule on every core entity.

Decisions and their trade-offs are in [`docs/adr/`](docs/adr/).

## How this is built (SDD)

1. **Spec / ADR** — requirements in EARS form, clarifications, tests ([`docs/specs/`](docs/specs/), [`docs/adr/`](docs/adr/)).
2. **Test first** — tests are written from the Spec before the code.
3. **Implementation** — done with Claude Code under the rules in [`CLAUDE.md`](CLAUDE.md).
4. **Audit** — an independent review of the generated code; each PR carries a table mapping requirement → test → result.

AI wrote a large share of the code; the specifications, the decisions and the review are mine. Specs written before implementation are archived afterwards and labeled as such.

## Domain glossary

| Term | Meaning |
| --- | --- |
| Tenancy | Tenant (league) isolation shared kernel. |
| Torneos | Tournaments, fixtures and results. |
| Estadisticas | Standings, top scorers and clean-sheet records derived from saved matches. |
| Pollas | Free prediction games for fans. |
| IA | AI features (match summaries, rulebook assistant). |

## Run locally

Requires the .NET SDK version in `global.json`.

```bash
dotnet restore
dotnet build
dotnet test
```

## Repository map

| Path | Content |
| --- | --- |
| `GoalFlow.Domain` | Entities, rules, events. No dependencies. |
| `GoalFlow.Application` | Use cases and ports. |
| `GoalFlow.Infrastructure` | Adapters (database, cache, external providers). |
| `GoalFlow.Api` | Thin controllers and composition root. |
| `GoalFlow.Tests` | Unit, integration and architecture tests. |
| `docs/adr/` | Architecture Decision Records. |
| `docs/specs/` | Specifications and the Spec template. |

## Roadmap

Deployment and health check, persistence with tenant isolation, authentication, tournaments and results, derived standings, prediction games, AI-generated match summaries. Order may change; the Specs record what was actually built.

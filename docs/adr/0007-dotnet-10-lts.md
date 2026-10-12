# ADR-07: .NET 10 (LTS) runtime

- **Status:** Accepted
- **Date:** 2026-10-08 (S2D1)
- **Related:** ADR-03

Translated from Spanish on 2026-10-11; the decision is unchanged.

## Context

A new project is starting, with a production release planned within a few weeks. Microsoft announced that .NET 8 (LTS) and .NET 9 (STS) end support on 10 November 2026. .NET 10 is LTS and is supported until November 2028 (the support policy page gives 14 November 2028). Data verified on 2026-10-08 on the official .NET support policy page and on the .NET blog; confirm again before citing them in an external document.

## Decision

1. The runtime and the SDK are **.NET 10 (LTS)**; all projects use `net10.0`.
2. The SDK is pinned in `global.json` to version `10.0.401`. The `rollForward` policy changes from `latestFeature` to **`latestPatch`**: with `latestFeature`, a machine with a newer SDK band builds with a different SDK than CI and the results may diverge. Moving to a new SDK band is an explicit, reviewed change.
3. **Stack compatibility with .NET 10, verified so far:**
   - Npgsql EF Core provider: versions 10.0.x exist, and they depend on EF Core 10.
   - NetArchTest.Rules 1.3.2 verified in S2D1 (it builds and runs on net10.0). actions/checkout@v7 and actions/setup-dotnet@v6 verified by the first CI run.
   - **Pending verification in the session that introduces each one:** Polly (S2D5), JwtBearer (S2D3) and the Docker base image (S2D2).
4. A dependency is not added to the project without checking its compatibility with .NET 10 and recording it in the ADR of the corresponding session.

## Rejected alternatives

- **.NET 8 or .NET 9:** they would lose support weeks after launch and create upgrade debt from day one.
- **Waiting for the next version:** no benefit; .NET 10 has the longest support window available.

## Consequences

- **(+)** A support window until 2028 with no mandatory migration during the planned lifetime of v1.
- **(−)** Some third-party libraries may be slow to support .NET 10; mitigation: check each dependency before adding it.
- **(−)** The SDK is updated only by explicit decision (a change in `global.json`), not automatically.

## Verification

`dotnet --list-sdks` shows the pinned version, `dotnet build` and `dotnet test` pass locally and in CI, and CI uses the SDK declared in `global.json`. Re-evaluate the version before November 2028.

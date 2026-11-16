# Spec 010: Client IP source and rate limiting of public and Admin routes

- **Status:** **Approved v2** (2026-11-16, H18), after the owner's review of v1 the same day: public global limit raised from 300 to 600 per minute (R-11, C-11), the Admin availability risk made explicit (C-11, §10), C-06 accepted as a residual operational risk with four conditions (C-06, R-07, R-22), and mutations M1 to M3 selected for the Phase B of PR 1 (§8.1). **Part A** (the temporary network diagnostic, §3.1) is complete and is what PR 1 implements. **Part B** (§3.2 to §3.5) is fixed except for two values that wait for evidence: the client IP source (chosen in v3 by the decision rule of §3.2, frozen **before** the evidence exists) and the final thresholds (provisional, R-11 and C-11). History: Draft v1 (2026-11-16), built from the owner's decisions of that day: scope option A, D6 kept (the diagnostic endpoint is for an Admin only), and retos R1 to R6 with predictions (6 of 6 exact).
- **Date:** 2026-11-16
- **Related:** ADR-05 §2.4, R-15 and C-07 (forwarded headers; amended by PR 2), Spec 002 §10 and `CLAUDE.md` (temporary exception of `GET /{slug}/equipos`; closed by this Spec), Spec 009 R-37 and C-21 (statistics endpoints stay off until this Spec is verified in production), ADR-01 §2.2 and Spec 003 (Admin order 404 → 401 → 403), Spec 009 R-35 (no names in logs; extended here to IPs)
- **Effort:** Medium. Part B's permit accounting (R-17) is subtle: the order of the limiters changes what other clients can do.

> Requirement and test IDs are local to this Spec ("Spec 010 T-03").

## 1. Context

`CLAUDE.md` requires a rate limit on public endpoints. Today there is none: `GET /{slug}/equipos` runs under a temporary exception (Spec 002 §10), and the three statistics endpoints of Spec 009 are mapped only behind a switch that is off in production. A per-client limit is only correct if the API knows the real client IP. The request path is client → Cloudflare edge → Render's proxy → container (ADR-05 §2.4: responses carry `Server: cloudflare` and `CF-RAY`). The API keeps the **last** `X-Forwarded-For` entry (`ForwardLimit = 1`, trusted lists cleared). Non-official reports say that on Render that entry is a Cloudflare edge address, that Render appends to `X-Forwarded-For` without clearing it, and that Cloudflare's `CF-Connecting-IP` carries the client; a Render staff comment from 2021 says the client is the **first** entry, which a client can forge if Render only appends. None of this is documented officially by Render, so it is measured in production first (Part A) and only then used (Part B). Every route with a `{slug}` also reaches the database before any check: `ResolutorDeSlug` reads `ligas` without a cache, and the Admin filter resolves the slug before it answers `401` (R4: one query for an unknown slug, one for a known slug without a token).

## 2. Goal

Part A: an Admin can observe, in production, exactly what the container receives about the client's address, without the API logging or storing it. Part B: every route except `/health` is limited per client and globally per route class, with the client IP taken from a source that the production evidence showed to be correct and not forgeable by the client; a rejection is `429`, readable by the frontend, and costs no database work.

## 3. Requirements (EARS)

### 3.1 Part A — Temporary network diagnostic (PR 1; removed by PR 2)

| ID | Requirement |
| --- | --- |
| R-01 | Where `Diagnostico:RedHabilitado` is `true` at startup, the API shall map `GET /{slug}/admin/diagnostico/red` inside the Admin group, so the Admin filter applies unchanged (unknown slug `404`, no valid token `401`, no membership `403`). The key is `false` in every committed `appsettings*.json`. While it is `false` or absent, the route shall not exist: any caller, a valid Admin included, gets `404`. |
| R-02 | While the diagnostic is enabled, for a request whose path is the diagnostic route, a middleware placed **before** the forwarded-headers middleware shall take a snapshot of: the connection's remote IP as Kestrel received it, and the values of the allowlisted headers only — `X-Forwarded-For`, `X-Forwarded-Proto`, `Forwarded`, `X-Real-IP`, `CF-Connecting-IP`, `CF-Connecting-IPv6`, `CF-Pseudo-IPv4` and `True-Client-IP`. Each header is kept as the list of its instances in arrival order (at most 8 instances; each value cut at 512 characters). For any other request the middleware does nothing. The snapshot lives only in the request's features. |
| R-03 | When an Admin of the league calls the route, the API shall answer `200` with `Cache-Control: no-store` and this body: `{ "antes": { "ipDeConexion": string \| null, "encabezados": { <allowlisted header present>: [string, …] } }, "xForwardedForEntradas": [string, …], "despues": { "ipRemota": string \| null, "esquema": string } }`. `antes` is the snapshot of R-02; `xForwardedForEntradas` is every `X-Forwarded-For` instance of the snapshot split by commas and trimmed, in order; `despues` is `HttpContext.Connection.RemoteIpAddress` and `Request.Scheme` as the endpoint sees them, after the forwarded-headers middleware. Headers that did not arrive are absent from `encabezados` (never an empty list). |
| R-04 | The response shall never contain `Authorization`, `Cookie`, any header outside the allowlist of R-02, the Admin's `sub`, the league's id or any database value. |
| R-05 | Neither the snapshot middleware nor the endpoint shall write a log entry, at any level, that contains an IP address or the value of an allowlisted header. |
| R-06 | The snapshot and the endpoint shall not change what any other route receives: with the diagnostic enabled, every existing route behaves exactly as with it disabled. |

### 3.2 Part B — Client IP source (decided in v3 from the evidence)

| ID | Requirement |
| --- | --- |
| R-07 | The API shall derive the client IP from exactly **one** source, chosen in Spec v3 by applying the decision rule below to the production evidence of §8.2 (step A). The choice holds only for the topology and the routes that the evidence covered (C-06). PR 2 amends ADR-05 §2.4, R-15 and C-07 accordingly. |

**Decision rule (frozen in v1 and kept in v2, before any evidence; C-04).** The evidence set has at least four requests to the diagnostic route, with `curl -4`: network A without forged headers, network A with forged headers, network B without, network B with. "Forged headers" means `X-Forwarded-For`, `X-Real-IP`, `CF-Connecting-IP` and `True-Client-IP` all set to `203.0.113.66` (a documentation address). The **truth** of each request is the public IPv4 that `https://www.cloudflare.com/cdn-cgi/trace` reports (`ip=`) from the same machine and network, with `curl -4`, right before it (C-05). If the network offers IPv6, the same four requests are repeated with `curl -6` and the IPv6 truth; they are recorded and must not contradict the choice. If the edge rejects a forged request, its status is recorded and that request is repeated forging only `X-Forwarded-For` and `X-Real-IP`.

1. **Source (d) `CF-Connecting-IP`** is chosen if, in every request of the set, it arrives exactly once, is a valid IP literal, equals the truth, and is the same in the forged request as in the unforged one.
2. Otherwise, **source (c) "penultimate `X-Forwarded-For` entry"** (`ForwardLimit = 2`) is chosen if, in every request, `xForwardedForEntradas` has at least two entries, its penultimate entry equals the truth, and the forged request has exactly one more entry than the unforged one from the same network.
3. Otherwise **no source is chosen**: Part B stops before the per-client limiter, and the chat decides (for example, a global limit only).

(d) is preferred over (c) because it does not depend on the number of proxies inside Render (C-06). Sources (a) "last entry" and (b) "first entry" are excluded by R1: (a) names the edge, not the client, and (b) is forgeable.

### 3.3 Part B — Partition key and classes

| ID | Requirement |
| --- | --- |
| R-08 | The per-client partition key shall be (class, normalized client IP): an IPv4 address as is; an IPv4-mapped IPv6 address as its IPv4; any other IPv6 address as its `/64` prefix; a missing or unparsable value as the single shared key `sin-ip`. |
| R-09 | Every request shall belong to exactly one class, by path, compared without regard to case: `salud` for `/health`; `admin` when the second path segment is `admin` (`/{slug}/admin` and below); `publica` for everything else, including paths that match no route. |

### 3.4 Part B — Limiters

| ID | Requirement |
| --- | --- |
| R-10 | The API shall apply a single global limiter made of a chain, in this order: **(1) per client** (fixed window, partition of R-08) → **(2) global per class** (fixed window, one partition per class). No endpoint policy is used: an endpoint policy would run after the global limiter, which is the order R2 rejected (C-07). |
| R-11 | Both limiters shall use a 60-second window, automatic replenishment and a queue of 0. Their permit limits come from configuration `LimiteDeFrecuencia:{Publica\|Admin}:{PorCliente\|Global}`, validated at startup (integers ≥ 1; an invalid value stops the start). Provisional defaults (C-11): `Publica` 120 per client and 600 global (5:1); `Admin` 60 per client and 120 global (2:1). |
| R-12 | Requests of class `salud` shall never be limited and shall not consume permits. |
| R-13 | If a request is rejected, the API shall answer `429` with the body `{ "error": "limite_de_frecuencia" }` and, when the rejecting lease gives a retry time, `Retry-After` in whole seconds (rounded up). The body and headers shall not reveal the class, the partition, the client IP, the limits or the counts. |
| R-14 | The pipeline order shall be: forwarded headers → CORS → rate limiter → endpoints. For an allowed origin, a `429` shall carry the CORS headers and expose `Retry-After` to the browser. |
| R-15 | If a request is rejected, then no endpoint code shall run for it: in particular, the slug resolver shall not be called. |
| R-16 | Rejections shall not be logged with the client IP or the partition key. At most, the class and the fact of the rejection may be logged. |
| R-17 | The permit accounting shall follow from the order of R-10: with `Publica` limits of 10 per client and 100 global, after one client sends 50 requests in a window (10 accepted, 40 rejected), nine other clients shall each get their 10 requests accepted in the same window (90 in total), and the first request of a tenth other client shall be rejected. |
| R-18 | When a client exhausts its per-client quota, another client in the same class shall still be accepted while the global quota of the class lasts; exhausting the `publica` class shall not reject `admin` requests, and the reverse. |
| R-19 | A value of a header that is not the chosen source (R-07) shall not change the partition of a request. The exact tests depend on the source chosen in v3 (T-17). |

### 3.5 Part B — Removal of the diagnostic and closing of the exception

| ID | Requirement |
| --- | --- |
| R-20 | PR 2 shall remove the diagnostic route, its snapshot middleware and the `Diagnostico:RedHabilitado` key. After PR 2, `GET /{slug}/admin/diagnostico/red` shall answer `404` even when that key is set to `true`. |
| R-21 | Once Part B is verified in production (§8.2, step B), the temporary exception of Spec 002 §10 and `CLAUDE.md` shall be closed in the closing PR, and the owner may then turn on `Estadisticas:EndpointsPublicosHabilitados` (Spec 009 R-37). |
| R-22 | The client IP derived from forwarded headers shall be used only as the per-client partition key of the rate limiter (and, until PR 2 removes it, as data of the diagnostic). It shall never take part in authentication, authorization or tenant isolation. |

## 4. Non-functional constraints

- **No packages:** the rate limiter is `Microsoft.AspNetCore.RateLimiting`, part of the shared framework. (Spec 005 C-20.)
- **Personal data:** an IP may be personal data. It is never persisted or logged (R-05, R-16); per-client counters live in memory only while their partition is active (C-12).
- **No database work for rejections** (R-15).
- **Deterministic tests:** limiter tests use small configured limits and never depend on wall-clock timing to cross a window boundary; a test that would need it is replaced by one that configures a long window.

## 5. Clarifications

| # | Question | Decision | Decided by / date |
| --- | --- | --- | --- |
| C-01 | Why measure before building the limiter? | ADR-05 C-07 left the real client IP behind Cloudflare and Render unproven, the non-official sources contradict each other (first entry vs. Cloudflare header; Render appends without clearing), and a per-client limiter keyed on the edge address would put many clients in one partition. Part A produces the evidence; Part B uses it. | Owner, 2026-11-14 (D6) and 2026-11-16 |
| C-02 | Why an Admin route and a switch, not a public route with a secret? | D6 kept: the route reuses the tested Admin filter and answers only to whoever calls it. A secret in a URL leaks through history and logs. The switch keeps the route absent outside the test window; turning it off in Render needs no deploy. It requires a temporary league and membership in production (§8.2), removed afterwards. | Owner, 2026-11-16 (R6) |
| C-03 | Why snapshot **before** the forwarded-headers middleware? | That middleware replaces the connection IP and may rewrite the forwarded headers it consumes. Reading them in the endpoint would show what is left, not what arrived. The snapshot shows the input; `despues` shows our current configuration's output. | Mentor, 2026-11-16 |
| C-04 | Why freeze the decision rule now? | So the choice cannot be fitted to the evidence after seeing it. If the evidence matches no branch, the rule says stop, not "pick the closest". | Mentor, 2026-11-16 |
| C-05 | What is the "truth" of a request? | The IPv4 Cloudflare reports from the same machine and network through `cdn-cgi/trace`, taken with `curl -4` like the request itself (both families must match). It is Cloudflare's view too, but from an endpoint we do not control, and it is the address any honest source must give. | Mentor, 2026-11-16 |
| C-06 | Can a client bypass Cloudflare and talk to Render's proxy directly? | Not testable from outside: Render does not publish an origin address, and a public `onrender.com` hostname resolving to Cloudflare does not prove that no other route exists. Both (c) and (d) become forgeable if such a path exists. **Accepted as a residual operational risk, not as a demonstrated guarantee**, with four conditions: (1) the DNS resolution and the requests of §3.2 are supporting evidence, not proof that a bypass is impossible; (2) the derived IP is never used for authentication, authorization or tenant isolation (R-22); (3) if a route that forges the effective IP appears, the adoption of that source stops and the architecture is reviewed; (4) the conclusion of R-07 is limited to the topology and routes actually verified. No new infrastructure mechanism is added in the milestone without evidence of need. (d) is preferred over (c) because adding a proxy hop inside Render breaks (c) silently and does not affect (d). | Owner, 2026-11-16 (review of v1) |
| C-07 | Which limiter goes first? | Per client, then global (R2: with global first, one client sending 50 requests at 10/100 spends 90 global permits; with per client first, 10). The middleware tries a synchronous acquire and then an asynchronous one, and a fixed-window permit released by a later rejection is not returned. A global limiter plus an endpoint policy would put the global first, so both live in one chain. | Owner, 2026-11-16 (R2, exact prediction) |
| C-08 | Who pays the double charge now? | When the **global** limiter rejects, the client's per-client permits are charged twice for that request. It only happens when the class is already exhausted, and it hurts the client being rejected, not others. Accepted. | Mentor, 2026-11-16 |
| C-09 | Why classes, and why per class? | Owner's R4: Admin routes cost one database query even without a token, so they are limited; public and Admin traffic must not drain each other. Consequence accepted: one client gets both budgets. `/health` is excluded so fan traffic never makes Render's health check or the keep-alive look like an outage. | Owner, 2026-11-16 (R4) |
| C-10 | Shared networks and IPv6? | R5: at 60 per minute and 5 calls per view, a shared IPv4 (CGNAT) supports 12 views per minute, or 24 fans refreshing every 2 minutes; at 120, 24 and 48. IPv6 is grouped by `/64` (otherwise one client could rotate through 2^64 keys); different devices of one `/64` share a quota. | Owner, 2026-11-16 (R5) |
| C-11 | Are the thresholds final? | No. They are configuration with provisional defaults, not measured capacities of Render or Supabase. **Public: 120 per client and 600 global (5:1).** Three saturating addresses spend 360 permits and leave 240 for everyone else; exhausting the class needs five addresses at their full quota. 600 rather than 1,200 because the capacity of the API with its 10 database connections is not measured, and a fixed window does not spread load evenly: it can admit its whole quota in a few seconds. **Admin: 60 per client and 120 global (2:1), accepted provisionally** to bound the cost of Admin queries. **Availability risk, explicit and not mitigated:** two addresses, even without a token, can exhaust the `admin` class for every league for the rest of the window, and the real Admins with it. The four values stay configurable and are reviewed after the measurement of §8.2 step C and the pilot. | Owner, 2026-11-16 (review of v1) |
| C-12 | Counters across restarts and instances? | In memory, per instance; they reset on restart, on redeploy and when the free instance spins down. There is one instance. No distributed store. | Owner, 2026-11-16 (R5) |
| C-13 | Are CORS preflights limited? | No: CORS answers `OPTIONS` preflights before the limiter (R-14), without touching the database. | Mentor, 2026-11-16 |
| C-14 | Do paths without a route count? | Yes, as `publica` (R-09). They cost no database work, but counting them keeps a scanner from using them freely. | Mentor, 2026-11-16 |
| C-15 | Does the chosen source keep the scheme correct? | `X-Forwarded-Proto` is processed with the client-IP header by the same middleware. PR 2 reproduces in its tests the exact header shape captured in §8.2 (instance counts of `X-Forwarded-For`, `X-Forwarded-Proto` and the chosen header) and asserts both the IP and `https`. If the captured shape makes the middleware skip a header, PR 2 stops (Estado §4.2 rule 5). | Mentor, 2026-11-16 |

## 6. Tests

### 6.1 Part A (PR 1)

All Part A tests run the real application (`WebApplicationFactory<Program>`) in the `Production` environment, with a startup filter that sets the connection's remote IP to the simulated proxy `10.0.0.1` before any middleware, and with `Diagnostico:RedHabilitado` set as each test says.

| Test ID | Covers | Description | Must fail when |
| --- | --- | --- | --- |
| T-01 | R-01 | With the key `false`, and with the key absent: a valid Admin of the league calls the route → `404`. | The route is mapped regardless of the switch. |
| T-02 | R-01 | With the key `true`: unknown slug → `404`; known slug without token → `401` with `WWW-Authenticate: Bearer`; Admin of another league → `403`; Admin of the league → `200`. | The route is mapped outside the Admin group, or the order changes. |
| T-03 | R-02, R-03 | With the key `true`, an Admin sends `X-Forwarded-For: 198.51.100.1, 203.0.113.7` and `CF-Connecting-IP: 203.0.113.9`. The body has `antes.ipDeConexion = "10.0.0.1"`, `antes.encabezados["X-Forwarded-For"] = ["198.51.100.1, 203.0.113.7"]`, `antes.encabezados["CF-Connecting-IP"] = ["203.0.113.9"]`, `xForwardedForEntradas = ["198.51.100.1", "203.0.113.7"]`, `despues.ipRemota = "203.0.113.7"` and no other key in `encabezados`. | The snapshot is taken after the forwarded-headers middleware (then `ipDeConexion` would be `203.0.113.7`), or the entries are not split in order. |
| T-04 | R-02 | Two instances of `X-Forwarded-For` (`198.51.100.1` and `203.0.113.7`) appear as two items, in order; a 600-character value comes back cut at 512; 9 instances come back as 8. | Instances are merged, reordered or not bounded. |
| T-05 | R-03 | `Cache-Control` of the `200` contains `no-store`; a header in the allowlist that was not sent is absent from `encabezados`. | The header is missing, or absent headers appear as empty lists. |
| T-06 | R-04 | The Admin sends its token, `Cookie: s=abc`, `X-Personalizado: xyz` and the allowlisted headers. The raw response body contains neither the token, `abc`, `xyz`, the Admin's `sub` nor the league's id. | Any of them leaks into the body. |
| T-07 | R-05 | With a log capture provider that records every `GoalFlow` category at `Trace` and every other category at the levels of the committed `appsettings.json`, a call with sentinel values (`203.0.113.201` in every allowlisted header) answers `200` with the sentinel in its body (positive control: the value went through) and leaves no captured entry containing `203.0.113.201` or `10.0.0.1`. | The middleware or the endpoint logs an IP or a header value. |
| T-08 | R-06 | With the key `true`, `GET /{slug}/equipos` with the same forged headers returns the same status and body as with the key `false`. | The snapshot changes another route. |

### 6.2 Part B (PR 2; completed in v3 with the chosen source)

| Test ID | Covers | Description | Must fail when |
| --- | --- | --- | --- |
| T-10 | R-08 | Partition keys: `203.0.113.7` → itself; `::ffff:203.0.113.7` → `203.0.113.7`; `2001:db8:1:2:aaaa::1` and `2001:db8:1:2:bbbb::9` → the same `/64`; `2001:db8:1:3::1` → another; missing and `no-es-ip` → `sin-ip`. Pure function, `[Theory]`. | Normalization changes. |
| T-11 | R-09 | Classes: `/health` → `salud`; `/liga/admin/sesion` and `/liga/ADMIN/sesion` → `admin`; `/liga/equipos`, `/no/existe/ruta` → `publica`. `[Theory]`. | A path lands in another class. |
| T-12 | R-11 | An invalid value of each of the four limits stops the start; the defaults are exactly `Publica` 120/600 and `Admin` 60/120. | Validation is missing or a default changes. |
| T-13 | R-12 | With `Publica` exhausted for a client, `/health` from that client answers `200`. | `/health` is limited or consumes permits. |
| T-14 | R-13, R-14 | The rejected request from an allowed origin gets `429`, the body of R-13, `Retry-After` ≥ 1, `Access-Control-Allow-Origin` with the origin and `Retry-After` in `Access-Control-Expose-Headers`. | The status is `503`, CORS headers are missing, or `Retry-After` is not exposed. |
| T-15 | R-15 | With a counting double of the slug resolver, the calls counted after the quota is exhausted do not grow. | A rejected request reaches the endpoint. |
| T-16 | R-17 | Limits `Publica` 10/100. Client X sends 50 requests in one window (10 accepted, 40 `429`); then clients C1 to C9 send 10 each, all accepted (90); then the first request of C10 gets `429`. | The chain is global first (only C1 would be accepted; C2's first request would get `429`) or permits are counted otherwise. |
| T-17 | R-18, R-19 | Client X exhausted → client Y accepted; `publica` exhausted → an `admin` request accepted, and the reverse. Forged values of non-chosen headers do not move X to another partition (cases fixed in v3 from the evidence). | Partitions or classes leak into each other, or a non-chosen header moves the partition. |
| T-18 | R-16 | With a log capture provider, the rejections leave no entry containing the client IP. | A rejection is logged with the IP or the key. |
| T-19 | R-20 | After PR 2, with `Diagnostico:RedHabilitado = true`, the diagnostic route answers `404` to a valid Admin. | The route or the switch survives. |
| T-20 | R-07, ADR-05 R-15 | The forwarded-headers tests are rewritten for the chosen source, with the exact header shape captured in §8.2, asserting both the client IP and `https` (C-15). | The configuration takes another entry or loses the scheme. |
| T-21 | R-22 | Architecture rule over the IL of every production assembly: the only readers of `ConnectionInfo.RemoteIpAddress` are the limiter's partition function (and the diagnostic until PR 2 removes it). Canary: a fixture type in its own `Fixtures` sub-namespace that reads it must make the rule fail. | Another type reads the client IP, or the rule passes its canary. |

## 7. Out of scope

- A distributed or persistent limiter; limits per authenticated user (fans arrive in H20; the prediction game's limits and multiple-account detection belong to H23).
- Cloudflare rules or a WAF: the Cloudflare zone in front of Render is not ours.
- A load test. §8.2 step C is a bounded measurement, not a capacity test.
- Caching of public responses.

## 8. Definition of done

- [ ] PR 1 (Part A): T-01 to T-08 pass and none was modified to make it pass; merged and *Live*; evidence of §8.2 step A recorded; the switch turned off.
- [ ] Spec v3: the source chosen by the decision rule, with the evidence, and T-17 and T-20 completed.
- [ ] PR 2 (Part B): T-10 to T-21 pass; ADR-05 §2.4, R-15 and C-07 amended; the diagnostic removed; mutation audit done.
- [ ] §8.2 step B passed in production.
- [ ] Closing PR: Spec 002 §10 and `CLAUDE.md` exception closed; this Spec *Implemented* with §9 filled.

### 8.1 Delivery, evidence and audit

Each PR follows the Estado §4.2 rules. The PR 1 audit includes the three mutations the owner chose to predict (frozen after reading the Phase A code): **M1** the snapshot taken after the forwarded-headers middleware (T-03), **M2** the route mapped regardless of `Diagnostico:RedHabilitado` (T-01) and **M3** the connection IP written to a log (T-07). The PR 2 audit includes at least: the chain in the opposite order (T-16), the limiter before CORS (T-14), the default rejection code (T-14), `/health` limited (T-13), IPv6 not grouped (T-10) and the diagnostic left mapped (T-19).

### 8.2 Production checks (owner)

**Step A — evidence (after PR 1 is *Live*).**
1. Create the temporary league `liga-humo` and the membership of the smoke-test Auth user (decision of 2026-11-15: allowed only for a controlled test).
2. Set `Diagnostico__RedHabilitado=true` in Render (restart without a deploy).
3. Positive control: the Admin gets `200` from the route; without a token, `401`.
4. Run the evidence set of §3.2 from network A and network B, recording each truth and each body, without the token in any saved file.
5. Record the addresses the API hostname resolves to and whether they fall in Cloudflare's published ranges (C-06).
6. Remove `Diagnostico__RedHabilitado` from Render and confirm the route answers `404` to the Admin.
7. Keep `liga-humo` and the membership for step B.

**Step B — behaviour (after PR 2 is *Live*).**
1. Positive control: `GET /liga-humo/equipos` → `200` from network A.
2. From network A, send requests until the first `429`; its number must match the `Publica` per-client limit; `Retry-After` present.
3. While A is rejected: network B → `200`; network A with forged headers → still `429`; `/health` from A → `200`.
4. The diagnostic route → `404` to the Admin.
5. Clean up: delete the membership and `liga-humo`; verify 0 rows left in every `goalflow` table.

**Step C — bounded measurement.** Within step B, before exhausting the quota, 60 sequential requests to `GET /liga-humo/equipos` from network A; record p50 and p95 latency. The statistics endpoints are measured the same way after they are turned on. These numbers inform C-11; they do not prove capacity.

## 9. Validation (filled in the closing commit)

> While a PR is under review, this evidence lives in its description. After the production checks, a closing docs commit fills this table with real results, marks §8 and sets the status to Implemented. A box is never checked, and no result is written, before its evidence exists.

| Requirement | Test | Result | Evidence |
| --- | --- | --- | --- |
| R-01 | T-01, T-02 | | |
| R-02, R-03 | T-03, T-04, T-05 | | |
| R-04 | T-06 | | |
| R-05 | T-07 | | |
| R-06 | T-08 | | |
| R-07 | §8.2 A, T-20 | | |
| R-08 | T-10 | | |
| R-09 | T-11 | | |
| R-10, R-17 | T-16 | | |
| R-11 | T-12 | | |
| R-12 | T-13 | | |
| R-13, R-14 | T-14 | | |
| R-15 | T-15 | | |
| R-16 | T-18 | | |
| R-18, R-19 | T-17, §8.2 B | | |
| R-20 | T-19, §8.2 B | | |
| R-21 | closing PR | | |
| R-22 | T-21 | | |

## 10. Risks and open items

- **C-06:** a path to Render's proxy that skips Cloudflare would make the chosen source forgeable; not testable from outside.
- **C-11:** thresholds are provisional until measured. The `admin` class can be exhausted by two addresses without a token (availability risk accepted, not mitigated).
- **Shared addresses:** fans behind one CGNAT address share a quota (C-10); the frontend must keep its calls per view low (H19).
- The smoke-test Auth user and `liga-humo` exist in production between step A and step B; both are removed at the end of step B.

# Spec 010: Client IP source and rate limiting of public and Admin routes

- **Status:** **Implemented** (2026-11-19). PR #24 (Part A, merge `9c0d2ea`, CI #68) and PR #25 (Part B, merge `b75c536`, CI #72) are merged and *Live*; step A (2026-11-17) and steps B and C (reported by the owner on 2026-11-19) passed in production; both mutation audits ended with no survivor. §9 holds the evidence. History: Approved v5 (2026-11-18, Phase B.2 of PR 2). v5 only strengthens two tests after the PR 2 mutation audit: of 14 mutations, 12 were killed and two survived. **ME** (`/health` sharing the global quota of `publica`) survived because T-13 never exhausts that quota; **MF** (the per-client key without the class) survived because T-17 (b) only exhausts global quotas. T-13 gains part (b) and T-17 gains part (e), which killed ME and MF respectively. No requirement changes. History: Approved v4 (2026-11-18, Phase A.2). v4 adds only C-20: the Phase A of PR 2 stopped because an existing test that loads 190 match sheets through the Admin routes received `429`; the finding is a product constraint (bulk Admin loads must be paced), not a test defect, and the test that loads in bulk for another purpose sets its own Admin limits. Nothing else changes. History: Approved v3 (2026-11-18). The owner approved it the same day after checking current Microsoft, Cloudflare and Render documentation: C-16 option 1, C-06 as an explicit residual risk, C-17, C-18, R-23 with its trust risk documented (C-19), and T-17, T-20 and T-21 with the precisions below. The production evidence of §8.2 step A (2026-11-17) applied to the frozen rule of §3.2 chose **source (d) `CF-Connecting-IP`** (§3.2.1); C-06 is re-examined with that evidence and kept as a residual risk; the Phase A audit findings are incorporated (A3: R-02 and T-09; A4: T-08); T-17, T-20 and T-21 are completed; and the forwarded-headers middleware stops deriving the client address (R-23, C-16). Thresholds stay provisional (R-11, C-11). History: Approved v2 (2026-11-16), after the owner's review of v1: public global limit 600, Admin availability risk explicit, C-06 conditions, M1 to M3 selected. Draft v1 (2026-11-16), from scope option A, D6 and retos R1 to R6 (6 of 6 exact).
- **Date:** 2026-11-16 (v1 and v2); 2026-11-18 (v3, v4 and v5); 2026-11-19 (Implemented)
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
| R-02 | While the diagnostic is enabled, for a request whose path is the diagnostic route (its segments compared without regard to case), a middleware placed **before** the forwarded-headers middleware shall take a snapshot of: the connection's remote IP as Kestrel received it, and the values of the allowlisted headers only — `X-Forwarded-For`, `X-Forwarded-Proto`, `Forwarded`, `X-Real-IP`, `CF-Connecting-IP`, `CF-Connecting-IPv6`, `CF-Pseudo-IPv4` and `True-Client-IP`. Each header is kept as the list of its instances in arrival order (at most 8 instances; each value cut at 512 characters). For any other request the middleware does nothing. The snapshot lives only in the request's features. |
| R-03 | When an Admin of the league calls the route, the API shall answer `200` with `Cache-Control: no-store` and this body: `{ "antes": { "ipDeConexion": string \| null, "encabezados": { <allowlisted header present>: [string, …] } }, "xForwardedForEntradas": [string, …], "despues": { "ipRemota": string \| null, "esquema": string } }`. `antes` is the snapshot of R-02; `xForwardedForEntradas` is every `X-Forwarded-For` instance of the snapshot split by commas and trimmed, in order; `despues` is `HttpContext.Connection.RemoteIpAddress` and `Request.Scheme` as the endpoint sees them, after the forwarded-headers middleware. Headers that did not arrive are absent from `encabezados` (never an empty list). |
| R-04 | The response shall never contain `Authorization`, `Cookie`, any header outside the allowlist of R-02, the Admin's `sub`, the league's id or any database value. |
| R-05 | Neither the snapshot middleware nor the endpoint shall write a log entry, at any level, that contains an IP address or the value of an allowlisted header. |
| R-06 | The snapshot and the endpoint shall not change what any other route receives: with the diagnostic enabled, every existing route behaves exactly as with it disabled. |

### 3.2 Part B — Client IP source (decided in v3 from the evidence)

| ID | Requirement |
| --- | --- |
| R-07 | The API shall derive the client IP from exactly **one** source: the request header **`CF-Connecting-IP`** (source (d)), chosen in v3 by applying the decision rule below to the production evidence of §8.2 step A (§3.2.1). The choice holds only for the topology and the routes that the evidence covered (C-06). Only the per-client partition function reads it (R-22, C-16). PR 2 amends ADR-05 §2.4, R-15 and C-07 accordingly. |

**Decision rule (frozen in v1 and kept in v2, before any evidence; C-04).** The evidence set has at least four requests to the diagnostic route, with `curl -4`: network A without forged headers, network A with forged headers, network B without, network B with. "Forged headers" means `X-Forwarded-For`, `X-Real-IP`, `CF-Connecting-IP` and `True-Client-IP` all set to `203.0.113.66` (a documentation address). The **truth** of each request is the public IPv4 that `https://www.cloudflare.com/cdn-cgi/trace` reports (`ip=`) from the same machine and network, with `curl -4`, right before it (C-05). If the network offers IPv6, the same four requests are repeated with `curl -6` and the IPv6 truth; they are recorded and must not contradict the choice. If the edge rejects a forged request, its status is recorded and that request is repeated forging only `X-Forwarded-For` and `X-Real-IP`.

1. **Source (d) `CF-Connecting-IP`** is chosen if, in every request of the set, it arrives exactly once, is a valid IP literal, equals the truth, and is the same in the forged request as in the unforged one.
2. Otherwise, **source (c) "penultimate `X-Forwarded-For` entry"** (`ForwardLimit = 2`) is chosen if, in every request, `xForwardedForEntradas` has at least two entries, its penultimate entry equals the truth, and the forged request has exactly one more entry than the unforged one from the same network.
3. Otherwise **no source is chosen**: Part B stops before the per-client limiter, and the chat decides (for example, a global limit only).

(d) is preferred over (c) because it does not depend on the number of proxies inside Render (C-06). Sources (a) "last entry" and (b) "first entry" are excluded by R1: (a) names the edge, not the client, and (b) is forgeable.

#### 3.2.1 Evidence and result (2026-11-17)

The owner ran §8.2 step A on `9c0d2ea` (PR #24) from two networks with different public IPv4 addresses; neither network offered IPv6. Controls: switch off `200/200/404/404`, switch on `200/200/200/401`, switch removed `200/200/404/404` (health, session with token, diagnostic with token, diagnostic without token). The evaluator printed classes only; no address left the owner's machine.

| Observation (identical in A and B) | Value |
|---|---|
| Codes | `normal=200`; full forgery `403`; alternative (`X-Forwarded-For` and `X-Real-IP` forged) `200` |
| Truth stable during each capture | yes |
| Connection IP (`antes.ipDeConexion`) | private |
| Headers received | `X-Forwarded-For`, `X-Forwarded-Proto`, `CF-Connecting-IP` and `True-Client-IP`, one instance each; `X-Real-IP` absent even when the client sent it |
| `X-Forwarded-For` entries | normal: client > Cloudflare > private; forged: forged > client > Cloudflare > private |
| `CF-Connecting-IP` and `True-Client-IP` | the client, in every accepted request |
| `X-Forwarded-Proto` | `https` |
| Current configuration (`despues.ipRemota`, last entry) | private: **today the API takes a Render-internal address as the client** (the class does not tell whether it is the same address for every request) |
| DNS of the API hostname | 2 A records, outside Cloudflare's published ranges; no AAAA |

Applying the rule: **(d) holds** (one instance, equal to the truth, unchanged by the forgery that reached the app); **(c) does not hold** (the penultimate entry is Cloudflare in all four requests: the chain has three hops, not the two the non-official reports described). **Decision: source (d) `CF-Connecting-IP`.** The forgery that included `CF-Connecting-IP` and `True-Client-IP` never reached the app: it was rejected with `403` upstream (the token and membership were valid, so it was not the Admin filter). What was shown is that a forged `CF-Connecting-IP` cannot be delivered through the public hostname, not how Cloudflare would overwrite it.

### 3.3 Part B — Partition key and classes

| ID | Requirement |
| --- | --- |
| R-08 | The per-client partition key shall be (class, normalized client IP), where the client IP is the value of `CF-Connecting-IP` when the request carries **exactly one** instance of it and that instance is a valid IP literal: an IPv4 address as is; an IPv4-mapped IPv6 address as its IPv4; any other IPv6 address as its `/64` prefix. A missing header, more than one instance, or an unparsable value gives the single shared key `sin-ip`. |
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
| R-19 | Headers other than `CF-Connecting-IP` (`X-Forwarded-For`, `X-Real-IP`, `True-Client-IP`, `Forwarded`) and the connection address shall not change the partition of a request. |

### 3.5 Part B — Removal of the diagnostic and closing of the exception

| ID | Requirement |
| --- | --- |
| R-20 | PR 2 shall remove the diagnostic route, its snapshot middleware and the `Diagnostico:RedHabilitado` key. After PR 2, `GET /{slug}/admin/diagnostico/red` shall answer `404` even when that key is set to `true`. |
| R-21 | Once Part B is verified in production (§8.2, step B), the temporary exception of Spec 002 §10 and `CLAUDE.md` shall be closed in the closing PR, and the owner may then turn on `Estadisticas:EndpointsPublicosHabilitados` (Spec 009 R-37). |
| R-22 | The client IP shall be used only as the per-client partition key of the rate limiter. It shall never take part in authentication, authorization or tenant isolation. In production code, only the partition function shall reference the header name `CF-Connecting-IP`, and no code shall read `ConnectionInfo.RemoteIpAddress` (after PR 2 removes the diagnostic). |
| R-23 | The forwarded-headers middleware shall process only `X-Forwarded-Proto` (`ForwardLimit = 1`; trusted lists cleared in Production, as today). It shall no longer derive the connection address from `X-Forwarded-For`: the evidence shows that its last entry is a Render-internal address and its penultimate a Cloudflare edge, so neither is the client. `Request.Scheme` stays `https` behind Render. |

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
| C-04 | Why freeze the decision rule now? | So the choice cannot be fitted to the evidence after seeing it. If the evidence matches no branch, the rule says stop, not "pick the closest". **Applied on 2026-11-17 without changes:** (d) holds, (c) does not (§3.2.1). | Mentor, 2026-11-16; result 2026-11-18 |
| C-05 | What is the "truth" of a request? | The IPv4 Cloudflare reports from the same machine and network through `cdn-cgi/trace`, taken with `curl -4` like the request itself (both families must match). It is Cloudflare's view too, but from an endpoint we do not control, and it is the address any honest source must give. | Mentor, 2026-11-16 |
| C-06 | Can a client bypass Cloudflare and talk to Render's proxy directly? | **Re-examined with the evidence; still a residual operational risk, not a demonstrated guarantee.** The behavioural evidence shows that every accepted request through the public hostname crossed a Cloudflare edge (an edge entry in `X-Forwarded-For`, `CF-Connecting-IP` and `True-Client-IP` set to the client), that a forged `CF-Connecting-IP` was rejected upstream with `403`, and that a forged `X-Real-IP` was stripped. The DNS check did **not** give the supporting evidence the mentor expected: both A records are outside Cloudflare's published ranges. Non-official data explains it without implying a bypass: Render's ingress uses its own anycast prefix `216.24.57.0/24` (AS397273), whose only listed peer is Cloudflare, so Cloudflare fronts addresses that Render owns and that are not in Cloudflare's published lists. The check was the mentor's design error: it assumed the hostname resolves into those lists (C-18). The owner added on 2026-11-18 that Render's own documentation gives `216.24.57.1` as the address of its load balancer, which confirms that Render-owned addresses are expected; and that the `403` shows a rejection before GoalFlow's endpoint but does not identify which component of the infrastructure rejected it. **What each claim rests on:** `CF-Connecting-IP` equal to the client in both IPv4 networks, the `403` for the full forgery and a penultimate `X-Forwarded-For` entry that is not the client are **demonstrated** by step A; `216.24.57.0/24` belonging to Render is **supported externally**; that every possible route to Render goes through Cloudflare is **not demonstrated**. The four conditions of v2 stand: (1) the evidence supports, it does not prove; (2) R-22; (3) a route that forges the effective IP stops the adoption of the source; (4) the conclusion covers only the verified path, the public `onrender.com` hostname over IPv4. **Compensating controls**, independent of the client IP being true: the global limit per class bounds the load whatever the partitions are; a request without the header falls in the shared `sin-ip` partition (R-08). A forged IP through an unknown path could only evade the per-client limit, not the global one. | Owner, 2026-11-16 (conditions) and 2026-11-18 (accepted as an explicit residual risk); mentor, 2026-11-18 (evidence) |
| C-07 | Which limiter goes first? | Per client, then global (R2: with global first, one client sending 50 requests at 10/100 spends 90 global permits; with per client first, 10). The middleware tries a synchronous acquire and then an asynchronous one, and a fixed-window permit released by a later rejection is not returned. A global limiter plus an endpoint policy would put the global first, so both live in one chain. | Owner, 2026-11-16 (R2, exact prediction) |
| C-08 | Who pays the double charge now? | When the **global** limiter rejects, the client's per-client permits are charged twice for that request. It only happens when the class is already exhausted, and it hurts the client being rejected, not others. Accepted. | Mentor, 2026-11-16 |
| C-09 | Why classes, and why per class? | Owner's R4: Admin routes cost one database query even without a token, so they are limited; public and Admin traffic must not drain each other. Consequence accepted: one client gets both budgets. `/health` is excluded so fan traffic never makes Render's health check or the keep-alive look like an outage. | Owner, 2026-11-16 (R4) |
| C-10 | Shared networks and IPv6? | R5: at 60 per minute and 5 calls per view, a shared IPv4 (CGNAT) supports 12 views per minute, or 24 fans refreshing every 2 minutes; at 120, 24 and 48. IPv6 is grouped by `/64` (otherwise one client could rotate through 2^64 keys); different devices of one `/64` share a quota. | Owner, 2026-11-16 (R5) |
| C-11 | Are the thresholds final? | No. They are configuration with provisional defaults, not measured capacities of Render or Supabase. **Public: 120 per client and 600 global (5:1).** Three saturating addresses spend 360 permits and leave 240 for everyone else; exhausting the class needs five addresses at their full quota. 600 rather than 1,200 because the capacity of the API with its 10 database connections is not measured, and a fixed window does not spread load evenly: it can admit its whole quota in a few seconds. **Admin: 60 per client and 120 global (2:1), accepted provisionally** to bound the cost of Admin queries. **Availability risk, explicit and not mitigated:** two addresses, even without a token, can exhaust the `admin` class for every league for the rest of the window, and the real Admins with it. The four values stay configurable and are reviewed after the measurement of §8.2 step C and the pilot. **Step C (reported 2026-11-19):** 60 sequential requests to `GET /liga-humo/equipos` from one client in Colombia, p50 = 203 ms and p95 = 236 ms, on Render's free instance. It is a latency measure of one light endpoint, not a capacity: it does not validate 600 per minute. | Owner, 2026-11-16 (review of v1) |
| C-12 | Counters across restarts and instances? | In memory, per instance; they reset on restart, on redeploy and when the free instance spins down. There is one instance. No distributed store. | Owner, 2026-11-16 (R5) |
| C-13 | Are CORS preflights limited? | No: CORS answers `OPTIONS` preflights before the limiter (R-14), without touching the database. | Mentor, 2026-11-16 |
| C-14 | Do paths without a route count? | Yes, as `publica` (R-09). They cost no database work, but counting them keeps a scanner from using them freely. | Mentor, 2026-11-16 |
| C-15 | Does the chosen source keep the scheme correct? | Yes, and more simply than v2 expected: with R-23 the forwarded-headers middleware processes only `X-Forwarded-Proto`, which arrived as one instance (`https`) in every request. The client IP no longer goes through that middleware, so there is no list of `For` and `Proto` values to keep symmetric. T-20 asserts `https` with the captured shape. | Mentor, 2026-11-18 |
| C-16 | Where is `CF-Connecting-IP` read? | **Option 1, approved by the owner on 2026-11-18:** the partition function reads the header directly, and the forwarded-headers middleware keeps only `X-Forwarded-Proto` (R-23); `RemoteIpAddress` stays the connection address (Render's proxy) and nobody reads it (R-22). Option 2: configure the middleware with `ForwardedForHeaderName = "CF-Connecting-IP"` so `RemoteIpAddress` becomes the client, and have the partition function read `RemoteIpAddress`. Option 1 is chosen because: there is exactly one place where trust in the header is decided; the middleware **consumes** the header it processes, so after option 2 a request without the header is indistinguishable from one with it (both just have an address), and R-08's `sin-ip` could not be honoured without reading internal side effects; and R-22 becomes checkable by a literal scan (T-21). Its cost, **accepted as an intentional decision and not as an open defect**: `RemoteIpAddress` is not the fan's address, so any future use of the client IP must go through the same function and amend R-22. Option 2 would use ASP.NET Core's standard mechanism, but the middleware consumes the header it processes, so a missing header could not be told apart without preserving extra state first. | Mentor, 2026-11-18; approved by the owner, 2026-11-18 |
| C-19 | What trust remains in the forwarded-headers middleware? | With the trusted proxy lists cleared in Production (unchanged from ADR-05), ASP.NET Core accepts `X-Forwarded-Proto` from **any** source that reaches Kestrel; Microsoft's guidance discourages clearing them. It is kept because Render's proxy addresses are not fixed, the container is reachable only through Render's proxy (ADR-05 §2.4, an assumption of the same kind as C-06), and since R-23 the middleware no longer touches the client address: only `Request.Scheme` depends on it, and no authorization, tenant or limiter decision reads the scheme. If one ever does, this is revisited. Recorded in the ADR-05 amendment; it does not block PR 2. | Owner, 2026-11-18 |
| C-20 | Can an Admin load many match sheets at once? | Not faster than the `admin` limits allow. The baseline of stage 1 (Control 1, D0) loads every match already played as a real sheet: up to 190 in a 20-team round robin, two requests each if the loader reads before it writes. With the defaults, one Admin gets 60 requests per minute and the class 120 for every league together, so a bulk load **is paced**: about 3 to 7 minutes for 190 sheets, and two leagues loading at the same time share the 120. **Decision: the limits are not raised for bulk loads** (the `admin` class also bounds the database work of unauthenticated requests, C-09, C-11). The loader that the owner uses for the baseline must space its requests (at most 1 per second) and, on a `429`, wait the `Retry-After` and repeat the same request; repeating it is safe because a rejected request ran no endpoint code at all (R-15), so the repetition is its first execution. The Admin form (H24) works at human speed and is not affected. **Tests:** a test whose purpose is not rate limiting and that loads in bulk through the Admin routes sets high `admin` limits **in its own host** (Phase A of PR 2: only `EstadisticasPublicasTests.T17`, which saves 190 sheets to count database commands). The defaults in code and every other host stay untouched, and T-12 still asserts the defaults. | Mentor, 2026-11-18 (Phase A.2); owner's review |
| C-21 | What does the implementation do that the Spec did not say? | Recorded at closing ("what the code does and the Spec does not say goes into the Spec"): (1) `IPAddress.TryParse` accepts abbreviated IPv4 literals, so `CF-Connecting-IP: 1` is the key `0.0.0.1`; accepted, because on the verified path Cloudflare sets the header (a forged one got `403`) and on an unknown path any value would be forgeable anyway (C-06). (2) `ParticionDeFrecuencia` also exposes `ClaveDeParticion` (`{class}\|{key}`), used by the limiter; it lives in the only type allowed to name the header (R-22). (3) The `429` status is set both by `RejectionStatusCode` and in `OnRejected`; removing either alone is an equivalent mutant, so the audit removed both (MC). (4) The test `Handoff_la_ruta_se_captura_sin_distinguir_mayusculas` (T-09 in v3) was retired with the diagnostic in PR 2, as §8 foresaw. | Mentor, 2026-11-19 |
| C-17 | Why not `True-Client-IP`, which also carried the client? | R-07 allows exactly one source, and the frozen rule evaluates `CF-Connecting-IP`. The two headers are equivalent per Cloudflare; using one avoids two values that could ever disagree. | Mentor, 2026-11-18 |
| C-18 | What was wrong in the evidence design? | The evaluator classified the DNS addresses against Cloudflare's published ranges only. Render's ingress anycast addresses are its own, fronted by Cloudflare, so they appear as `OTRA`. The check could never have returned `CLOUDFLARE` for this hostname; it is recorded as a design error of the mentor, not as evidence against Cloudflare being in front. An optional local check by the owner (whether both A records fall in `216.24.57.0/24`, printed only as true or false) would turn the neutral result into a positive one. | Mentor, 2026-11-18 |

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
| T-08 | R-06 | With the key `true`, `GET /{slug}/equipos` with the same forged headers returns the same status and body as with the key `false`. | The snapshot short-circuits or breaks another route. (It cannot detect an altered forwarded header on another route: no endpoint reflects it; audit finding A4.) |
| T-09 | R-02 | With the key `true`, `GET /liga-a/ADMIN/Diagnostico/RED` by an Admin answers `200` with `antes.ipDeConexion = "10.0.0.1"` and the sent `X-Forwarded-For`. (Renamed from `Handoff_la_ruta_se_captura_sin_distinguir_mayusculas`, audit finding A3.) | The snapshot compares the path with case. |

### 6.2 Part B (PR 2)

| Test ID | Covers | Description | Must fail when |
| --- | --- | --- | --- |
| T-10 | R-08 | Partition function over the request headers, `[Theory]`: `CF-Connecting-IP: 203.0.113.7` → `203.0.113.7`; `::ffff:203.0.113.7` → `203.0.113.7`; `2001:db8:1:2:aaaa::1` and `2001:db8:1:2:bbbb::9` → the same `/64`; `2001:db8:1:3::1` → another; header missing, `no-es-ip`, and two instances (`203.0.113.7` and `203.0.113.8`) → `sin-ip`. | Normalization changes, or a missing, repeated or invalid header gets its own key. |
| T-11 | R-09 | Classes: `/health` → `salud`; `/liga/admin/sesion` and `/liga/ADMIN/sesion` → `admin`; `/liga/equipos`, `/no/existe/ruta` → `publica`. `[Theory]`. | A path lands in another class. |
| T-12 | R-11 | An invalid value of each of the four limits stops the start; the defaults are exactly `Publica` 120/600 and `Admin` 60/120. | Validation is missing or a default changes. |
| T-13 | R-12 | (a) With `Publica` exhausted for a client, `/health` from that client answers `200`, and ten earlier `/health` requests left its per-client quota intact. (b) With `Publica` limited to 10 per client and **3 global**: five `/health` requests answer `200`; then three public requests from three addresses are accepted and the fourth gets `429` (the global quota was intact); then `/health` still answers `200`. | `/health` is limited or consumes permits, per client **or in the global quota of a class** (audit survivor ME). |
| T-14 | R-13, R-14 | The rejected request from an allowed origin gets `429`, the body of R-13, `Retry-After` ≥ 1, `Access-Control-Allow-Origin` with the origin and `Retry-After` in `Access-Control-Expose-Headers`. | The status is `503`, CORS headers are missing, or `Retry-After` is not exposed. |
| T-15 | R-15 | With a counting double of the slug resolver, the calls counted after the quota is exhausted do not grow. | A rejected request reaches the endpoint. |
| T-16 | R-17 | Limits `Publica` 10/100. Client X sends 50 requests in one window (10 accepted, 40 `429`); then clients C1 to C9 send 10 each, all accepted (90); then the first request of C10 gets `429`. | The chain is global first (only C1 would be accepted; C2's first request would get `429`) or permits are counted otherwise. |
| T-17 | R-18, R-19 | (a) Client X (`CF-Connecting-IP: 198.51.100.10`) exhausted → client Y (`192.0.2.20`) accepted. (b) `publica` exhausted → an `admin` request accepted, and the reverse. (c) With X exhausted, X's next request with the **captured shape** plus forged values — `X-Forwarded-For: 203.0.113.66, 198.51.100.10, 104.22.17.40, 10.200.0.5`, `X-Real-IP: 203.0.113.66`, `True-Client-IP: 203.0.113.66` — still gets `429`; and a request with `CF-Connecting-IP: 192.0.2.20` and X's `X-Forwarded-For` and `True-Client-IP` is accepted. (d) Requests without the header share `sin-ip`: after the `sin-ip` quota is exhausted, another request without it gets `429` while X's partition is unaffected. (e) With **1 per client** and 10 global in both classes: address X makes a public request and then an Admin request, and address Y an Admin request and then a public one; all four are accepted, because the class is part of the per-client key. | Partitions or classes leak into each other, a header other than `CF-Connecting-IP` moves the partition, or **the per-client key loses its class** (audit survivor MF). |
| T-18 | R-16 | With a log capture provider, the rejections leave no entry containing the client IP. | A rejection is logged with the IP or the key. |
| T-19 | R-20 | After PR 2, with `Diagnostico:RedHabilitado = true`, the diagnostic route answers `404` to a valid Admin. | The route or the switch survives. |
| T-20 | R-23, ADR-05 R-15 | The forwarded-headers tests are rewritten for R-23 with the **captured shape**: connection `10.0.0.1`, `X-Forwarded-For: 198.51.100.10, 104.22.17.40, 10.200.0.5`, `CF-Connecting-IP: 198.51.100.10`, `True-Client-IP: 198.51.100.10`, `X-Forwarded-Proto: https`, in Production. Assertions: `Request.Scheme = https`; `RemoteIpAddress` stays `10.0.0.1` (no `X-Forwarded-For` entry is applied); the `X-Forwarded-For` header reaches the endpoint unchanged. The Development case keeps the scheme behaviour of today. | The middleware still applies an `X-Forwarded-For` entry, or loses `https`. |
| T-21 | R-22 | Architecture rule over the IL of every production assembly, after PR 2: (a) the string literal `CF-Connecting-IP` appears only in the partition function's type; (b) no method reads `ConnectionInfo.RemoteIpAddress`. Canary: a fixture type in its own `Fixtures` sub-namespace that does each of the two must make the corresponding rule fail. **Static control with known limits:** it cannot see a read through reflection, dynamic code or a header name built at run time; the two canaries and code review are its complementary controls. | Another type reads the client IP, or a rule passes its canary. |

## 7. Out of scope

- A distributed or persistent limiter; limits per authenticated user (fans arrive in H20; the prediction game's limits and multiple-account detection belong to H23).
- Cloudflare rules or a WAF: the Cloudflare zone in front of Render is not ours.
- A load test. §8.2 step C is a bounded measurement, not a capacity test.
- Caching of public responses.

## 8. Definition of done

- [x] PR 1 (Part A): T-01 to T-08 pass and none was modified to make it pass; merged and *Live*; evidence of §8.2 step A recorded; the switch turned off. (PR #24, merge `9c0d2ea`, CI #68; Phase B: 14 valid mutations, none survived; step A on 2026-11-17, §3.2.1.)
- [x] Spec v3: the source chosen by the decision rule, with the evidence, and T-17 and T-20 completed.
- [x] PR 2 (Part B): T-09 to T-21 pass (T-09 until the diagnostic is removed, then replaced by T-19); ADR-05 §2.4, R-15 and C-07 amended; R-23 applied; the diagnostic removed; mutation audit done. (PR #25, merge `b75c536`, CI #72; 994 tests; 14 mutations, 12 killed in Phase B and ME and MF killed in Phase B.2.)
- [x] §8.2 step B passed in production (reported by the owner on 2026-11-19: `PASO B: todo OK`; cleanup verified).
- [x] Closing PR: Spec 002 §10 and `CLAUDE.md` exception closed; this Spec *Implemented* with §9 filled.

### 8.1 Delivery, evidence and audit

Each PR follows the Estado §4.2 rules. **PR 2 removes the diagnostic and its tests (T-01 to T-09) before it applies R-23**, so no commit of the branch carries both T-03 (the middleware applies an `X-Forwarded-For` entry) and T-20 (it applies none); T-19 and T-20 replace their coverage. The PR 1 audit includes the three mutations the owner chose to predict (frozen after reading the Phase A code): **M1** the snapshot taken after the forwarded-headers middleware (T-03), **M2** the route mapped regardless of `Diagnostico:RedHabilitado` (T-01) and **M3** the connection IP written to a log (T-07). **PR 2 audit result (2026-11-18):** 14 mutations, 12 killed; ME and MF survived. Phase B.2 added T-13 (b) and T-17 (e); **confirmed:** both were green with the original code (994 of 994), ME made T-13 (b) fail in its step 1 and MF made T-17 (e) fail in its step 2, and no other test failed. No mutant survives. The owner's three frozen predictions (MA, MB, MF) were exact in number and names. The PR 2 audit includes at least: the chain in the opposite order (T-16), the limiter before CORS (T-14), the default rejection code (T-14), `/health` limited (T-13), IPv6 not grouped (T-10) and the diagnostic left mapped (T-19).

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
3. While A is rejected: network B → `200`; network A forging `X-Forwarded-For` and `X-Real-IP` → still `429`; network A forging `CF-Connecting-IP` → rejected upstream (`403`, as in step A) or `429`, never `200`; `/health` from A → `200`.
4. The diagnostic route → `404` to the Admin.
5. Clean up: delete the membership and `liga-humo`; verify 0 rows left in every `goalflow` table.

**Step C — bounded measurement.** Within step B, before exhausting the quota, 60 sequential requests to `GET /liga-humo/equipos` from network A; record p50 and p95 latency. The statistics endpoints are measured the same way after they are turned on. These numbers inform C-11; they do not prove capacity.

## 9. Validation (filled in the closing commit, 2026-11-19)

Evidence lives in the PR descriptions (#24 and #25) and, for production, in the owner's procedures; no address or credential is recorded here. Mutation names refer to the audits of PR 1 (M1 to M13b) and PR 2 (MA to MN).

| Requirement | Test | Result | Evidence |
| --- | --- | --- | --- |
| R-01 | T-01, T-02 | Pass (PR #24); retired in PR #25, replaced by T-19 | M2, M2b, M10 and M12 killed |
| R-02, R-03 | T-03, T-04, T-05, T-09 | Pass (PR #24); retired in PR #25 | M1, M5, M6, M7, M8, M9 and M11 killed |
| R-04 | T-06 | Pass (PR #24) | M4 killed |
| R-05 | T-07 | Pass (PR #24) | M3 (`Information`, middleware) and M13b (`Debug`, endpoint) killed; M13 invalid as written by the mentor |
| R-06 | T-08 | Pass (PR #24), initial green | Its limit is recorded (audit finding A4): it cannot see an altered forwarded header on another route |
| R-07 | §8.2 A (§3.2.1), T-10 | Pass | Step A, 2026-11-17: the frozen rule chose (d) `CF-Connecting-IP`; (c) failed (the penultimate entry is Cloudflare) |
| R-08 | T-10, T-17 (e) | Pass (PR #25) | MG, MH and MF (Phase B.2) killed |
| R-09 | T-11 | Pass (PR #25) | ML killed |
| R-10, R-17 | T-16 | Pass (PR #25): X 10 accepted and 40 rejected, C1 to C9 90 accepted, C10 `429` | MA killed. Production: 120 accepted and 10 with `429` from network A |
| R-11 | T-12 | Pass (PR #25); the defaults stay 120/600 and 60/120 | MN killed. No `LimiteDeFrecuencia__*` variable in Render |
| R-12 | T-13 (a, b) | Pass (PR #25) | MD killed; ME killed in Phase B.2. Production: `/health` `200` while network A was rejected |
| R-13, R-14 | T-14 | Pass (PR #25) | MB, MC and MK killed. Production: `429` with `Retry-After` |
| R-15 | T-15 | Pass (PR #25), both cases | — |
| R-16 | T-18 | Pass (PR #25) | MM killed |
| R-18, R-19 | T-17, §8.2 B | Pass | Production: network B `200` while network A got `429`; forged `X-Forwarded-For` and `X-Real-IP` → `429`; forged `CF-Connecting-IP` → `403` |
| R-20 | T-19, §8.2 B | Pass | Production: the diagnostic route answers `404` with and without a token; session `200` as positive control |
| R-21 | closing PR | Done | Spec 002 §10 and `CLAUDE.md` closed in this PR. Turning on `Estadisticas:EndpointsPublicosHabilitados` is now allowed; the owner decided to turn it on for H19 after this PR is merged |
| R-22 | T-21 | Pass (PR #25), both canaries | MJ killed |
| R-23 | T-20 | Pass (PR #25) | MI killed |

**Step C (C-11):** p50 = 203 ms and p95 = 236 ms over 60 sequential requests. **Cleanup:** `liga-humo` and its membership were removed in one block that checks its pre- and postconditions; afterwards every `goalflow` table except the migrations history had 0 rows. The smoke-test Auth user is kept, without memberships (owner's decision, 2026-11-19).

## 10. Risks and open items

- **C-06:** a path to Render's proxy that skips Cloudflare would make `CF-Connecting-IP` forgeable; not testable from outside. The global limit per class bounds the damage regardless (C-06, compensating controls).
- **C-20:** bulk Admin loads (the stage-1 baseline) are paced by the limits; the loader must honour `Retry-After`.
- **C-19:** with cleared trusted lists, `X-Forwarded-Proto` is accepted from any source that reaches Kestrel; only the scheme depends on it.
- **C-11:** thresholds are provisional until measured. The `admin` class can be exhausted by two addresses without a token (availability risk accepted, not mitigated).
- **Shared addresses:** fans behind one CGNAT address share a quota (C-10); the frontend must keep its calls per view low (H19).
- **Neither network offered IPv6** in step A: the choice is verified for IPv4 only (C-06, condition 4). R-08 already groups IPv6 by `/64`.
- The smoke-test Auth user is kept without memberships (owner's decision, 2026-11-19); `liga-humo` was removed after step B.

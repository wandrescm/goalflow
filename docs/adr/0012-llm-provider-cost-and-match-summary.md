# ADR-12: LLM provider, cost ceiling and the A-01 generation policy

- **Status:** Accepted (2026-11-11, S6D1, H13), by the owner after revision v2 and the two editorial adjustments to Spec 007, with the documented risks accepted: the reservation's input part is an estimate (C-20), and renames are not regenerated (O-01). History: v1 built from the owner's decisions of 2026-11-11 (P-09 and design challenges 1 to 5); v2 after the owner's review the same day (O-01 to O-04, C-03, C-08 and C-09 decided; corrections C-15 to C-22; three integration checks against the official documentation). **Operational enablement is still pending:** `IA:Habilitada` stays false until the owner confirms the account, the prepaid balance and that automatic reload is off (C-01).
- **Date:** 2026-11-11
- **Related:** ADR-02 (post-save pipeline; amended §2.3 and §2.10 with this ADR; closes C-08 and C-09), ADR-03 (module `IA`), ADR-05 (secrets and hosting), ADR-14 (multi-tenancy; §2.3 amended in wording only, C-14), Spec 004 (additive: targeted publication), Spec 005 (R-18 renames; R-34 exact sets), Spec 006 (C-14, C-17, §7.1), Spec 007

## 1. Context

A-01 writes a short match summary in Spanish from the current score sheet. It is the first consumer of `PartidoGuardado` (Spec 006 R-23) and the first component of GoalFlow that pays per call to an external service. The course milestone protects it (no cut), and the PRD and the course format promise two things about it: nothing generated is published without deterministic verification (H15), and the AI never exceeds US$3 per month.

Facts that shape the decision:

1. **Price and volume.** Claude Haiku 5.5 costs US$0.10 per million input tokens and US$0.50 per million output tokens for prompts up to 100,000 tokens; above that threshold every rate is five times higher. Verified by the owner on the official pricing page on 2026-11-11. The pilot plays about 15 matches a week (two leagues), about 65 a month; doubled for corrections and retries, about 130 calls of roughly 2,000 input and 400 output tokens: **≈ US$0.05 per month**. The US$3 ceiling is about 7,500 such calls. **Only a defect (a loop, a redelivery storm, a backfill run repeatedly) can reach it**, so the ceiling is a circuit breaker against defects, not a forecast.
2. **One account, one invoice, many tenants.** The provider bills one account. Every GoalFlow table is tenant-scoped; the post-save loop processes one tenant per scope and transaction, and no code may read another tenant's rows (ADR-14). A global sum of spending across tenants is therefore not readable by the worker.
3. **At-least-once delivery** (ADR-02 §2.1, fact 9). A worker can complete a paid call and die before recording it. On redelivery, the system cannot know whether the earlier call was billed.
4. **Concurrency.** During a deploy, two instances run for about 90 s (ADR-02 fact 1). Two workers can try to spend the last part of a budget at the same time.
5. **Corrections are frequent** and every new sheet version publishes one `PartidoGuardado` (Spec 006 C-17). Consumers re-read the current state; the event carries only identifiers and the version.
6. **The prompt contains personal data and untrusted text.** Player and team names are personal data (Ley 1581) and are typed by an Admin in the import; they may contain anything that fits the name rules of Spec 005 R-12, including text that reads like instructions. The sheet itself carries no free text (Spec 006 C-14).
7. **Names change without a new match version.** The import updates team, player and tournament names (Spec 005 R-18) without touching `partidos.version` and without publishing an event.
8. **Token accounting (official documentation, read on 2026-11-11).** The token-counting endpoint is free (with its own rate limit), accepts the same inputs as a message and returns the input tokens. Its count is documented as an **estimate** that may differ by a small amount from the input actually used, and it may include tokens the provider adds for itself, which are not billed. `max_tokens` is a hard limit on the output; on Claude Haiku 5.5 thinking tokens count toward it and are billed as output. Adaptive thinking is on by default on this model and can be disabled with `thinking: {"type": "disabled"}`. Structured JSON output is requested with `output_config.format` (generally available; no beta header). A response can begin with a thinking block, and safety classifiers can end it with `stop_reason: "refusal"`. The model's newer tokenizer counts the same text as about 30 % more tokens than Claude Haiku 4.5. Failed requests are not billed, but interrupted or timed-out requests may be (Help Center, verified by the owner).
9. **Current production state.** Production has zero leagues. The Anthropic account and its balance are not yet confirmed by the owner, so P-09 is decided but not operationally enabled.

## 2. Decision

### 2.1 Provider behind a port; disabled until the owner enables it

- **Provider:** Anthropic, model Claude Haiku 5.5 (`claude-haiku-5-5`), through the Messages API. Chosen by the owner for commercial data terms, structured output and prepaid credits without automatic reload (P-09, C-01).
- **Port:** `IGeneradorDeTexto` in `GoalFlow.Application.IA` has two operations: count the input tokens of an exact request (free endpoint), and generate, returning the text **and the token usage the provider reported**. The adapter lives in `GoalFlow.Infrastructure.IA` as a typed `HttpClient`. **No new package:** the Messages API is plain HTTPS and JSON, and an adapter we own keeps the timeout, the usage parsing and the error classification explicit. Structured output uses `output_config.format` with a JSON schema, and thinking is set explicitly (§2.7). Both were checked in the official documentation on 2026-11-11. The H14 handoff re-reads the endpoint paths, the version header and the error shapes before coding.
- **Configuration, not constants:** model identifier, prices, budget limits, output limits and timeouts (§2.10). The API key is an environment variable, never in the repository, never logged (ADR-05).
- **Kill switch:** `IA:Habilitada`, **false by default in every environment**. While it is false, the A-01 consumer is **not registered**, so `PartidoGuardado` creates no A-01 delivery rows (ADR-02 §2.3: an event with no consumer writes nothing) and nothing calls the provider. Deliveries that already exist when it is turned off keep their state and attempts: `Pendiente`, or `EnProceso` after an interrupted call. The loop only claims, fails or retries deliveries of registered consumers (ADR-02 §2.6, C-23), and they are processed when it is on again. Their calls that were reserved and never reconciled stay counted (C-21). The owner turns it on only after confirming the account, the balance and the absence of automatic reload (C-01). Turning it on later loses nothing: the backfill (§2.8) covers the matches saved while it was off.
- **Three layers of spending control,** from inside out: (1) the per-league budget with atomic reservations (§2.4); (2) the per-fingerprint call cap (§2.5); (3) the provider's prepaid balance without automatic reload, the only **global** hard limit (fact 2). Layer 3 is an owner operation, checked before enabling.

### 2.2 What A-01 produces for each state

A-01 classifies the current sheet into exactly one of three outcomes (owner's decisions on challenge 2, C-03):

| Current state | Outcome |
| --- | --- |
| `Programado` (never saved or withdrawn), `Suspendido`, `Cancelado` | **None.** No summary, no call. |
| `Adjudicado` (with or without a sporting score) | **Template**: a deterministic administrative text built from the official score and the coded sporting reason. |
| `Jugado` with **no named event** (every goal unattributed, or 0-0, and no cards) | **Template**: a deterministic text built from the score. |
| `Jugado` with **at least one named event** (a goal of type `Jugador`, an own goal or a card) | **Narrative**: one call to the model. |

- A named event is one that carries a player's name. This rule generalizes the owner's five cases (C-03): 0-0 with cards is narrative; 2-1 with every goal unattributed is a template.
- Templates cost nothing and cannot invent anything: they are pure functions of the input. They never touch the budget.
- **Templates never interpolate a missing value.** `Jugado` always has a sporting score and `Adjudicado` always has an official score (Spec 006 R-11 a and c). An `Adjudicado` sporting score, if present, is ignored by its template. A sheet that breaks either rule cannot have passed Spec 006 validation; if one reaches A-01, the delivery ends as a permanent failure `planilla_incoherente`, with no text and no score invented (C-16).

### 2.3 Current version only, fingerprint, and derived visibility

- **Stale deliveries do nothing.** A delivery whose version is lower than the match's current version completes without any call or write (the owner's challenge 1). The newer version has its own delivery.
- **Input fingerprint.** The input of a summary is a canonical JSON document built from the current sheet (Spec 007 R-08): exactly the data that the model or the template receives, in a fixed order, with no identifiers. Its **fingerprint** is the SHA-256 of its UTF-8 bytes. Whatever reaches the model is in the fingerprint by construction, so no field can change the text without changing the fingerprint. This includes `tarjetasInformadas`, as the owner required (C-04). Lists are ordered **only by the values they emit**. Registration codes are not emitted and take no part in the order, so changing which registration holds a name changes nothing the model sees and nothing in the fingerprint. Two players with the same name on the same side are therefore indistinguishable in the input; that is accepted (C-18).
- **One summary per (match, fingerprint).** A version whose fingerprint already has a summary (for example, a redelivery after a recorded success) completes without calling.
- **Visibility is derived, never stored** (CLAUDE.md "derive, do not accumulate"). A summary is shown for a match **if and only if** its fingerprint equals the fingerprint of the match's current sheet (and, from H15, it passed verification). Consequences:
  - after a correction that changes the input, the old text disappears immediately, even before the worker runs (the owner's answer to challenge 1);
  - after a version whose input equals an earlier one (a correction reverted to earlier content, or a scorer swapped between two players with the same name), the existing summary is shown again without a new call. The rectification reason cannot change on its own (Spec 006 R-14);
  - after a withdrawal (`Programado`) nothing is shown, because no summary is produced for that state.
- **Rejected:** "shown if its source version equals the current version" (the owner's hypothesis in ADR-02 C-09). Combined with the fingerprint rule, a version whose input equals an earlier one (a reverted correction) would never produce a new summary, and the existing one would be **hidden forever** (C-05). Version equality and content identity are different questions; visibility follows content.

### 2.4 Budget: per league, monthly, reserved atomically before every call

- **Per-league ceiling (owner's choice A, C-06).** Each league has a monthly ceiling in micro-dollars (`IA:TopeMensualPorLigaMicroUsd`, initially 1,500,000 = US$1.50). The global ceiling (`IA:TopeMensualGlobalMicroUsd`, 3,000,000 = US$3) is enforced **by construction**: before any reservation, the worker checks, through the tenant-source port that already reads the league identifiers (ADR-14 §2.3, second exception), that `leagues × per-league ceiling ≤ global ceiling`. If not, nothing is reserved (fail closed). No table without a tenant is added and no new read site appears. ADR-14 §2.3, however, names the post-save loop as the only caller of the tenant enumeration, so it is amended in wording (C-14): the same port, reading the same identifiers, may also be called by the A-01 backfill (to open one scope per tenant) and by the A-01 budget check (to count leagues).
- **Counter row with compare-and-swap.** Table `presupuestos_ia`, one row per (tenant, month), holding the ceiling copied at creation, `reservado` and `consumido` in integer micro-dollars. A reservation is one `ExecuteUpdateAsync` whose `WHERE` requires `reservado + consumido + amount <= tope` and which adds the amount to `reservado`; it succeeds only if it affected exactly one row. This is the same compare-and-swap as the ADR-02 claim, with no lock and no raw SQL. Owner's prediction for two concurrent reservations of 700 with 1,000 left: one succeeds (C-07).
- **Reserve before calling, reconcile after.** In one short transaction: the counter compare-and-swap and the insert of a call row in `llamadas_ia` (state `Reservada`, with the reserved amount and the call number of §2.5). The call to the provider happens **after** the commit, with no transaction open. When the outcome is **certain** (a response was received, success or error), one short transaction moves the call row to `Conciliada` with the reported tokens and the real cost, and on the counter subtracts the reservation from `reservado` and adds the real cost to `consumido`.
- **Uncertain calls keep their reservation for the rest of the month.** A timeout, a broken connection, a cancellation or a crash leaves the call row `Reservada`. Nothing can ever reconcile it, because the usage is unknown, so its estimated maximum stays counted against the ceiling (the owner's challenge 4: "uncertain calls keep their reservation until reconciliation", which for them means never). This matches the provider's terms: an interrupted or timed-out request may be billed (fact 8).
- **The reserved amount: a contractual limit for output, a documented estimate for input (C-20).** Before reserving, the worker counts the input tokens of the **exact** request (model, system text, user message, `output_config.format` and the thinking setting) with the counting endpoint. Amount = ⌈(N × (1 + `MargenDeEntradaPorcentaje`/100) + `MargenDeEntradaTokens`) × input price⌉ + ⌈`MaxTokensDeSalida` × output price⌉, in micro-dollars, each part rounded up.
  - **Output part:** a bound by contract. `max_tokens` caps every billed output token, thinking included.
  - **Input part:** **not** a mathematical bound. The count is documented as an estimate, and the margin covers a small difference. The design does not assume the bound holds: a reconciliation always records the real cost, even above the reservation, logs a `Warning`, and the overrun stays in `consumido`, so later reservations see it and fail closed.
  - **The guarantee, stated exactly:** per league, `reservado + consumido + amount ≤ tope` holds at the moment of every reservation. Real spending can exceed the ceiling only by the input-estimation error of calls already made, which metric 2 reports (overruns per month). The global hard limit is still the prepaid balance.
  - **Size limits:** a canonical input above `MaximoDeBytesDeEntrada` is not even counted, and a count above `MaximoDeTokensDeEntrada` (8,000) is never sent. Both store the deterministic draft with reason `entrada_excedida`, without reserving. This keeps every request far below the 100,000-token price threshold (fact 1).
- **Month key (owner's decision, C-08).** The month is the UTC year-month of the instance clock (injected `TimeProvider`) when the reservation is made. This is a label, not a compared timestamp. At a month boundary two instances may put a reservation in different months for a few seconds; that is accepted and documented. The call row stores its month, and its reconciliation updates **that** month's counter even when it happens after the month changed. Spec 007 T-30 tests the boundary. All stored times still come from the database clock (column defaults).
- **Budget exhausted.** The match gets the deterministic draft (§2.6) with reason `presupuesto`, and the delivery completes. It does not retry: retrying would only spend attempts until the next month.

### 2.5 Bounded calls per fingerprint

- Each call row carries a **call number** within (match, fingerprint), unique per (tenant, match, fingerprint, number) and between 1 and `IA:MaximoDeLlamadasPorHuella` (initially **3**). The number is assigned as "existing calls + 1" in the reservation transaction; two concurrent reservations for the same fingerprint collide on the unique index, the loser's whole transaction rolls back (counter included), and it retries through the outbox.
- When the cap is reached, no call is made: the match gets the deterministic draft with reason `limite_de_llamadas`, and the delivery completes.
- **Effect on the owner's predictions (C-09):** for one delivery whose every call is billed and lost, the worst case is no longer 5 (the delivery attempts) or 10 (with H15's verifier retry), but **3 billed calls per fingerprint**, whoever makes them (redelivery, another version with the same input, the backfill or the verifier retry of H15). The cap is counted in the database, so it survives restarts and deploys.

### 2.6 Outcomes, retries and drafts

No in-memory retries and **no Polly** (closes ADR-02 C-08, C-10). Durable retries already exist in the outbox. In-memory retries would multiply billed calls inside one attempt and hide uncertain calls from the budget ledger. Timeouts are nested and validated at startup: HTTP timeout (`IA:TimeoutDeLlamada`, 60 s) < consumer timeout (`Mensajeria:TimeoutDelConsumidor`, 2 min) < lease (5 min).

| Provider outcome | Certain? | Ledger | Delivery |
| --- | --- | --- | --- |
| Success and valid output | Yes | Reconciled with the reported usage | Summary stored; completes |
| Success but invalid output (not the schema, truncated by `max_tokens`, out of length bounds, control characters) | Yes | Reconciled | Retryable failure; the next attempt makes the next numbered call (§2.5) |
| Success with `stop_reason` `refusal` | Yes | Reconciled | Draft with reason `rechazo_del_modelo`; completes |
| `429`, `500`, `529` or another `5xx` | Yes | Reconciled with the usage reported, or 0 if none | Retryable failure |
| `400`, `404`, `413` (our request is wrong) | Yes | Reconciled (0 if no usage) | Draft with reason `solicitud_invalida`; completes |
| `401`, `403` (credentials) | Yes | Reconciled (0) | Permanent failure `credencial_invalida` (`Fallida`, visible in SQL) |
| Timeout, connection lost, crash, shutdown | **No** | Stays `Reservada` | Retryable failure (crash and shutdown: ADR-02 rules, nothing written) |
| The counting request fails (any of the cases above) | — | Nothing reserved; counting is free | Same classification as the row that matches, with nothing to reconcile |

- **Draft (`Borrador`).** When the narrative cannot be produced (`presupuesto`, `limite_de_llamadas`, `solicitud_invalida`, `entrada_excedida`, `rechazo_del_modelo`), A-01 stores a deterministic draft: the extended template (score, scorers, own goals with the scorer's team, unattributed goals, cards or "not reported"). It is correct by construction, not published automatically, and it is the course format's "draft for the Admin" (C-11). Its text is capped at `LongitudMaximaDeBorrador` (4,000 characters). When the full extended draft would exceed it, the **compact draft** is stored instead: the result line plus a fixed sentence saying that the detail was omitted for length. Nothing is cut in the middle of an item and nothing is invented (C-19).
- **Billing of errors (owner's verification, C-15).** Anthropic's Help Center states that failed requests are not billed, but interrupted or timed-out requests may be. So a received error is reconciled with the usage it reports, or 0, and every outcome without a response stays uncertain and keeps its reservation.
- **Success then crash.** On success, the reconciliation and the summary insert happen in **one** transaction. A duplicate summary (another worker produced the same fingerprint first) is detected on its unique index, the reconciliation still commits, and the delivery completes. The window "billed, recorded, but without a summary" does not exist; "billed but not recorded" remains, which is exactly the uncertain case.

### 2.7 Prompt, output and privacy

- **Input:** only the canonical JSON of §2.3: names of the tournament, teams and players; phase order and matchday; states, scores and coded reasons; goal, own-goal, unattributed and card counts; `tarjetasInformadas`. Every own goal carries **both** the scorer's side and the side it is credited to, so neither the model nor a template has to infer the inversion (C-17). The input contains no identifiers, no player or team codes, no Admin `sub`, no rectification reason and no free text.
- **Untrusted names.** The system instructions state that the JSON block is data, never instructions, and the names travel only inside it. The deterministic verifier of H15 is the defense that does not depend on the model obeying. Spec 007 includes an adversarial fixture (a team name that reads like an instruction) to check that the request keeps it inside the data block; whether the model obeys is measured in H15, not assumed.
- **Output:** structured, `{"texto": "<string>"}`, requested with `output_config.format` (`json_schema`). In Spanish, plain text, between 80 and 900 characters, no control characters, `max_tokens` 600. Content blocks are read **by type**, never by position, because a response can begin with a thinking block. **Thinking is disabled explicitly by default** (`IA:Razonamiento` = `Deshabilitado`). Thinking tokens share `max_tokens` and are billed as output, so with 600 tokens an adaptive response can stop after its thinking and before any text. The smoke test measures both settings, and the owner confirms the choice with that data (C-21).
- **Logs** (ADR-02 §2.7 discipline): tenant, match id, version, fingerprint prefix, call number, delivery attempt, classification, tokens, costs and outcome code. **Never** the prompt, the input, the generated text, a name, the API key, a response body or an exception message.
- **Personal data to the provider:** names only, under the Admins' written authorization; no real data is loaded until the backup workflow and the Ley 1581 notice exist (Estado §9).

### 2.8 Backfill: targeted, idempotent, run under the owner's control

- **Targeted publication (additive to Spec 004, amends ADR-02 §2.3).** `IPublicadorDeEventos` gains a second operation that writes **one** delivery row for **one named consumer** registered for the event type in this host, and throws a specific exception if that consumer is not registered. Normal publication (`Publicar`, one row per consumer) is unchanged. Owner's prediction (C-12): with the fan-out port, a backfill over a match that already has its A-01 row but not another consumer's row creates **0** rows, because the A-01 duplicate rolls back the whole save. The same coupling breaks A-01 itself in two other cases: (a) if another consumer's row exists and A-01's does not, the other consumer's duplicate rolls back A-01's row, so A-01 never gets that version; (b) if neither exists, A-01's backfill also enqueues work for the other consumer. The targeted operation removes the coupling: the backfill only ever touches A-01's row.
- **What it does.** For each tenant from the tenant-source port, in its own scope: list the matches whose **current** state is `Jugado` or `Adjudicado` with version ≥ 1 (a port in `Torneos.Contracts`), and for each match, in its own DI scope and transaction, publish `PartidoGuardado(tenant, match, current version)` **only for A-01**, catch exactly the duplicate-delivery exception, discard the scope and count the match as already done (ADR-02 §2.3, idempotent publication).
- **When it runs (owner's decision).** Not on every start, not through any endpoint. When `IA:Habilitada` and `IA:Backfill:EjecutarAlArrancar` are both true, one hosted service runs it **once per process start**, logs the counts (tenants, created, already present) and never stops the host on error. The owner sets the flag for one deploy and removes it. Running it again, or in two overlapping instances, is safe.
- **Limit (accepted).** The backfill cannot regenerate a summary for a version that already has an A-01 delivery row: that row's key exists. Regenerating existing versions (after a rename, a prompt change or a credential fix) is out of the milestone (§2.11, O-01).

### 2.9 Three metrics

All three are read from `llamadas_ia` and `resumenes_partido` with SQL as `postgres` during the milestone (no endpoint):

1. **Fidelity:** the share of model texts rejected by the verifier, and 100 % of published texts with every number and name present in the input (H15; until then, the rate of invalid output).
2. **Cost:** `consumido + reservado` per league and month, real cost per generated summary, and **overruns** (reconciliations whose cost exceeded the reservation) per month. Any overrun means the input margin is too small.
3. **Amplification:** billed calls per model summary (target ≤ 1.2) and the number of uncertain calls (`Reservada` older than the lease). A value above target is the early signal of a loop, long before the ceiling.

### 2.10 Initial values (configuration)

| Setting | Initial value |
| --- | --- |
| `IA:Habilitada` | `false` |
| `IA:Modelo` | `claude-haiku-5-5` |
| `IA:PrecioEntradaMicroUsdPorMTok` / `IA:PrecioSalidaMicroUsdPorMTok` | 100,000 / 500,000 |
| `IA:TopeMensualPorLigaMicroUsd` / `IA:TopeMensualGlobalMicroUsd` | 1,500,000 / 3,000,000 |
| `IA:MaximoDeLlamadasPorHuella` | 3 |
| `IA:MaxTokensDeSalida` | 600 |
| `IA:MaximoDeBytesDeEntrada` (canonical input, before counting) / `IA:MaximoDeTokensDeEntrada` (counted) | 16,384 / 8,000 |
| `IA:MargenDeEntradaPorcentaje` / `IA:MargenDeEntradaTokens` | 10 / 50 |
| `IA:Razonamiento` | `Deshabilitado` (to be confirmed by measurement) |
| `IA:LongitudMaximaDeBorrador` | 4,000 characters |
| `IA:TimeoutDeLlamada` | 60 s |
| `IA:Backfill:EjecutarAlArrancar` | `false` |

With these values, a typical narrative (2,000 counted input tokens) reserves 525 micro-dollars and really costs about 400. At the maximum input (8,000 tokens) it reserves 1,185, so a league can make at least 1,265 reservations a month.

### 2.11 Placement and out of scope

- **Placement.** Module `IA` (ADR-03): pure logic and ports in `GoalFlow.Application.IA`; adapters and mappings in `GoalFlow.Infrastructure.IA`; the read ports it needs in `GoalFlow.Application.Torneos.Contracts`, implemented in `GoalFlow.Infrastructure.Torneos`. `IA → Torneos.Contracts` is already allowed; ADR-03 is not amended. `IA` uses the shared namespaces `Mensajeria` and `Persistencia`.
- **New tenant-scoped tables:** `resumenes_partido`, `llamadas_ia`, `presupuestos_ia`, with the `Tenant` filter, RLS and composite foreign keys to `partidos` (ADR-14 §2.11), in one migration.
- **Out of scope:** the verifier and publication (H15, Spec 008); any read endpoint for summaries (H19); an Admin view of drafts; regenerating existing versions; A-02; RAG; Polly; an alert channel.

## 3. Rejected alternatives

- **One global budget table without a tenant:** a third exception to ADR-14 §2.3, this time for writes from `goalflow_app`. The per-league ceiling gives the same global bound with no new exception (C-06).
- **Provider limit only:** no per-league fairness, and a defect in one league's data would stop the other league's summaries.
- **Check then insert:** two concurrent reservations both pass (owner's prediction for (i): 2 calls).
- **Release the reservation of an uncertain call:** under-counts what may have been billed.
- **Version equality as the visibility rule:** hides valid summaries forever after a reverted correction (§2.3).
- **One summary per match, whatever the version (ADR-02 C-09, first hypothesis):** a corrected scorer would keep a wrong name visible.
- **Polly with retries:** extra billed calls invisible to the ledger; the outbox already retries durably.
- **An SDK package:** a new dependency for one POST; the typed client keeps usage and errors explicit.
- **Fan-out backfill with `Publicar`:** loses deliveries of other consumers (C-12).
- **Backfill on every start or through an endpoint:** rejected by the owner; no SuperAdmin endpoints exist (CLAUDE.md).

## 4. Consequences

- **(+)** Spending is bounded per league and per fingerprint, with a hard global limit at the provider, and every possibly billed call is counted.
- **(+)** A loop or a redelivery storm shows up in the amplification metric long before it reaches the ceiling.
- **(+)** Corrections never leave a wrong text visible, and never hide a valid one.
- **(+)** Templates cover half the cases at zero cost and with no possible hallucination.
- **(−)** Three more tenant-scoped tables and one more migration; the exact sets of Spec 005 R-34 grow.
- **(−)** A summary disappears after every correction that changes the input until the worker runs. While the free Render service sleeps there is no upper bound (ADR-02 §2.8).
- **(−)** A rename through the import hides the summaries of that team's matches, and nothing regenerates them in the milestone (accepted by the owner, O-01).
- **(−)** Each narrative needs one more HTTP request (the free count) before reserving. A failed count reserves nothing and retries through the outbox.
- **(−)** A fingerprint that reached its call cap or ended as a draft is never retried automatically.
- **(−)** Uncertain calls consume budget for the rest of the month even when the provider did not bill them.

## 5. Verification

Spec 007 turns this ADR into EARS requirements and tests. The critical ones: the compare-and-swap with two gated reservations; uncertain calls keep their reservation; an overrun of the input estimate is recorded in full and fails closed; the month boundary; own goals credited to the right team; the fingerprint ignores registration codes; stale deliveries make no call; the fingerprint includes `tarjetasInformadas`; visibility follows content; a version that restores an earlier input reuses its summary; the call cap holds under redelivery; the targeted backfill is idempotent in sequence and under concurrency and does not touch other consumers; isolation of the three tables.

## 6. Clarifications

- **C-01** *Provider (P-09)?* Claude Haiku 5.5, prices verified by the owner on the official page on 2026-11-11. Decided but **not operationally enabled**: `IA:Habilitada` stays false until the owner confirms the account, the balance and the absence of automatic reload. Owner, 2026-11-11.
- **C-02** *Does a corrected match get a new summary (ADR-02 C-09)?* Only for its current version and only if its input changed (§2.3). Owner's prediction for the five-version sequence: 2 calls (v2 and v4); 1 if v5 is saved before v4 is processed. Both correct. Owner, 2026-11-11.
- **C-03** *Which states reach the model?* Owner's five cases: (a) LLM, (b) template, (c) administrative template, (d) administrative template from the current official result, (e) nothing; prediction 1 of 5, correct. Generalized by the mentor into "`Jugado` with at least one named event" (§2.2); **accepted by the owner** in the review of 2026-11-11, including 0-0 without cards as a template (O-02).
- **C-04** *Must `tarjetasInformadas` be part of the input?* Yes (owner). The mentor makes it structural: the fingerprint is computed over exactly what reaches the model (§2.3).
- **C-05** *Visibility by version or by content?* By content. Raised by the mentor on 2026-11-11 while comparing the owner's answer to challenge 1: "same input → no call" plus "shown only for the same version" hides a valid summary forever.
- **C-06** *Global or per-league ceiling?* Per league, with the global bound by construction (option A). Owner, 2026-11-11.
- **C-07** *How is a reservation made safe under concurrency?* Compare-and-swap on one counter row. Owner's prediction: (i) check then insert, 2 calls; (ii) compare-and-swap, 1. Correct.
- **C-08** *Which clock defines the month?* The UTC month of the instance clock, as a label only, with the boundary risk documented and tested (§2.4). Proposed by the mentor; **accepted by the owner** on 2026-11-11, on condition that the month change is tested (Spec 007 T-30).
- **C-09** *How many billed calls can one input cause?* Owner's predictions for challenge 4: (a) 1, (b) 5, (b) plus the verifier retry 10. Correct. The owner adopted (b) with durable bounded retries; the mentor proposed the per-fingerprint cap (§2.5). **Approved by the owner: 3 calls per match and fingerprint, persisted in the database** (O-03).
- **C-10** *Polly?* No (owner accepted the mentor's proposal). Closes ADR-02 C-08.
- **C-11** *What does "draft for the Admin" mean in H14?* A deterministic extended template stored as `Borrador` with a reason code; no UI in this milestone.
- **C-12** *How does the backfill avoid other consumers?* Targeted publication for A-01 only, an additive operation of `Mensajeria`. Owner's prediction with fan-out: 0 rows, and the other consumer never receives v3. Correct. The mentor adds the two cases where fan-out breaks A-01 itself (§2.8); they are the ones Spec 007 T-27 (d) and M4 check. The owner chose a controlled run after a deploy, safe to repeat, never on every start and never through an endpoint.
- **C-13** *Is the withdrawal in challenge 1 (v5) valid under Spec 006?* Yes: `Jugado` → `Programado` is an exceptional transition with a required rectification reason (Spec 006 R-11 d, R-14, C-12). Raised by the owner.
- **C-14** *Does counting leagues need an ADR-14 exception?* No new exception: the tenant-source port already reads only the identifiers of `ligas` without a transaction. Its stated callers grow from the post-save loop to also the A-01 backfill and the A-01 budget check; ADR-14 §2.3 and its C-15 are amended in wording in the same PR. Raised by the mentor on 2026-11-11 while checking ADR-14.

- **C-15** *Are error responses billed?* Failed requests are not billed; interrupted or timed-out requests may be. Verified by the owner in Anthropic's Help Center on 2026-11-11 (O-04). The certain/uncertain distinction of §2.6 stays as designed.
- **C-16** *What does a template do with a missing score?* It never interpolates one: `planilla_incoherente`, a permanent failure with no text (§2.2). Raised by the owner (review, correction 1).
- **C-17** *How are own goals represented?* With the scorer's side and the credited side, both explicit in the input; the extended draft lists the own goal under the credited team and names the scorer's team (§2.7; Spec 007 R-08, R-11, T-04). Raised by the owner (review, correction 2).
- **C-18** *Sorting by registration code contradicts "a code change does not change the fingerprint".* It did, in v1: two namesakes on one side with different quantities, swapped, changed the bytes. v2 sorts only by emitted values (§2.3). Namesakes on one side are indistinguishable in the input, which is accepted. Raised by the owner (review, correction 3).
- **C-19** *Can a draft exceed the column limit?* In v1, yes: a sheet with many scorers and cards produced more than 2,000 characters. v2 caps drafts at 4,000 and falls back to the compact draft (§2.6). Raised by the owner (review, correction 6).
- **C-20** *Is the reservation a proven maximum?* No. v1 claimed a byte-based bound that the documentation does not support. v2 counts with the official endpoint, adds a margin, and states the guarantee as it is: output is bounded by contract, input by a documented estimate, and overruns are recorded, measured and fail closed (§2.4). Raised by the owner (review, correction 5).
- **C-21** *Thinking on or off?* Off by default, and measured in the smoke test (adaptive versus disabled: valid outputs, output tokens, latency and the owner's reading of the texts) before the owner confirms. Raised by the owner (integration check).
- **C-22** *What happens to existing A-01 deliveries when IA is turned off?* They keep their state and attempts and are not claimed; they are processed when it is on again (§2.1; Spec 007 T-28). Raised by the owner (review, correction 4).

### Decided in the owner's review (2026-11-11)

- **O-01** *Renames:* accepted. A rename hides the affected summaries; regeneration is out of the milestone.
- **O-02** *0-0 without cards:* a deterministic template.
- **O-03** *Call cap:* 3 per match and fingerprint, persisted in the database.
- **O-04** *Error billing:* confirmed in the Help Center (C-15).

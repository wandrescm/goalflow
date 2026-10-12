# Spec NNN: <feature name>

- **Status:** Draft | Approved | Implemented | Superseded by NNN
- **Date:** YYYY-MM-DD
- **Related:** ADR-XX, Spec NNN
- **Effort:** Medium (default) | High

> A Spec states **what** must be true, not how to build it. Design decisions live in ADRs.
> Write it before the code. If behavior changes later, update this file in the same PR.

## 1. Context

Why this exists, in 3–6 lines. What the system looks like today.

## 2. Goal

One sentence. How we know it is done.

## 3. Requirements (EARS)

Each requirement is numbered, testable, and uses one EARS pattern:

- Ubiquitous: `The <system> shall <response>.`
- Event-driven: `When <trigger>, the <system> shall <response>.`
- State-driven: `While <state>, the <system> shall <response>.`
- Unwanted behavior: `If <condition>, then the <system> shall <response>.`
- Optional feature: `Where <feature is included>, the <system> shall <response>.`

| ID | Requirement |
| --- | --- |
| R-01 | |

## 4. Non-functional constraints

Each one must map to a requirement or a test. If it cannot be checked, it is not a constraint.

## 5. Clarifications

Questions raised before implementation and the decision taken. Nothing is "implicitly assumed".

| # | Question | Decision | Decided by / date |
| --- | --- | --- | --- |
| C-01 | | | |

## 6. Tests

| Test ID | Covers | Description | Must fail when |
| --- | --- | --- | --- |
| T-01 | R-01 | | |

Rules that guard other rules (architecture, security) also need a **canary**: a fixture with a deliberate violation that must make the rule fail.

## 7. Out of scope

What this Spec explicitly does not do.

## 8. Definition of done

- [ ] All tests above pass and none was modified to make it pass.
- [ ] Every requirement maps to at least one test (see validation table).
- [ ] No new warnings; no secrets in the diff.
- [ ] ADRs updated if a decision changed.
- [ ] PR description contains the validation table with real results.

## 9. Validation (filled at PR time)

| Requirement | Test | Result | Evidence |
| --- | --- | --- | --- |
| R-01 | T-01 | pass / fail | link to CI run or command output |

## 10. Risks and open items

Known gaps, with the Spec or session that will close them.

# ADR-09: Frontend hosting and build

- **Status:** Accepted
- **Date:** 2026-10-28
- **Related:** ADR-05 (backend hosting), ADR-03 (architecture)

## 1. Context

The frontend is a single-page application built with Vue.js (Composition API), TypeScript, Vite and Pinia, as stated in `CLAUDE.md`. The framework choice is not reopened here; this ADR decides where and how the built files are served.

Facts checked against Cloudflare documentation on 2026-10-27:

1. Cloudflare recommends Workers with Static Assets for new projects and focuses new features there. Pages remains fully supported.
2. Requests to static assets are free and unlimited. Limits are 20,000 files per version and 25 MiB per file.

The SPA has no server-side logic at the edge: all business logic lives in the API (ADR-05).

## 2. Decision

- The frontend is deployed as an **assets-only Cloudflare Worker** (Workers Static Assets, no Worker script), served from a `workers.dev` subdomain until a custom domain exists.
- SPA routing uses the assets setting `not_found_handling: "single-page-application"`, so deep links (for example `/standings`) return `index.html`.
- Build: `npm ci`, `npm run test` (Vitest) and `npm run build` (Vite). Output: `dist/`.
- Deploy: a GitHub Actions job on pushes to `main`, after the frontend build and tests pass, runs `wrangler deploy`. The Cloudflare API token and account ID are repository secrets.
- The API base URL is injected at build time through `VITE_API_BASE_URL`. No secret is ever embedded in the bundle: anything in it is public.
- `CLAUDE.md` changes "Cloudflare Pages" to "Cloudflare Workers (Static Assets)" in the same PR that adopts this ADR.

## 3. Requirements (EARS)

- **R-01** The frontend shall be served as static assets by a Cloudflare Worker without a Worker script.
- **R-02** When a path that matches no file is requested, the host shall return `index.html`.
- **R-03** When a commit reaches `main` and the frontend build and tests pass, CI shall deploy the frontend.
- **R-04** If the frontend build or tests fail, CI shall not deploy.
- **R-05** The production bundle shall contain no secrets; the only environment value it embeds is the public API base URL (and other values that are public by design).
- **R-06** The frontend's production origin shall be the one the API allows in CORS (ADR-05, R-12).

## 4. Consequences

**Positive**
- No hosting cost for static requests and no cold starts: files come from Cloudflare's edge.
- Follows the provider's current recommendation for new projects.
- Same pipeline style as the backend: deploy only from a green `main`.

**Negative / risks**
- Two providers to operate (Render and Cloudflare).
- The API and the frontend live on different origins, so CORS must be kept in sync.
- Changing the domain later (custom domain) requires updating the CORS origin and the Supabase Auth redirect URLs.

## 5. Alternatives considered

- **Cloudflare Pages:** fully supported and equivalent for a static SPA, but not where the provider is investing; no feature we need is exclusive to Pages.
- **Render static site:** possible, but puts frontend and backend on one provider's free limits and gives up Cloudflare's edge.
- **Serving the SPA from the API:** couples frontend deploys to backend restarts and wastes the API's limited memory on static files.

## 6. Clarifications

- **C-01** *Does this ADR choose the frontend framework?* No. Vue, TypeScript, Vite and Pinia are already fixed in `CLAUDE.md`.
- **C-02** *When is this implemented?* When the frontend project exists. Until then, this ADR only fixes the decision and the CORS origin pattern used by ADR-05.
- **C-03** *Is the Supabase anonymous key in the bundle a secret?* No: it is public by design. The frontend uses it only for Supabase Auth. It cannot reach application data because the Data API is disabled and the tables live in a private schema (ADR-05 §2.3). Service-role keys never reach the frontend.

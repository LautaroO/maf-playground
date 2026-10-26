# Local Agent Studio web workspace

Standalone Angular 22, signals, routing, no SSR. The Precision UI discovers entities,
profiles, schemas and capabilities from the CLI Studio host. There are no sample ID
switches, per-entity components or generated entity input/output types.

## Toolchain and commands

Verified Node **24.22.1**, npm **11.19.1**, Angular **22.2.3**, CLI/build **22.2.4**,
TypeScript **6.0.3**, Vitest **5.0.5** and jsdom **30.1.3**. All direct packages are
exact-pinned and `package-lock.json` is committed. Use `npm ci` for repeatable installs.
The workspace was scaffolded with:

```sh
npx --yes @angular/cli@22.2.4 new studio --directory src/MafPlayground.Studio.Web --routing --style css --ssr false --skip-git --package-manager npm --defaults
```

From the repository root:

```sh
dotnet restore
dotnet build src/MafPlayground.CLI -m:1 -p:UseSharedCompilation=false -nr:false
npm --prefix src/MafPlayground.Studio.Web ci
npm --prefix src/MafPlayground.Studio.Web run generate:contracts
npm --prefix src/MafPlayground.Studio.Web run check:contracts
npm --prefix src/MafPlayground.Studio.Web run typecheck
npm --prefix src/MafPlayground.Studio.Web run test:unit
npm --prefix src/MafPlayground.Studio.Web run build
npm --prefix src/MafPlayground.Studio.Web run check:format
```

The Angular CLI's generated `@angular/build:unit-test` runner uses Vitest/jsdom.
Tests cover API credentials/errors/reset mapping, fragmented SSE/reconnect/gaps,
selection/reset/cancel races, dynamic schemas and safe chat rendering. They make no
provider calls. Test-only fixtures are excluded from the application TypeScript project.

Playwright Chromium is configured by `playwright.config.ts`; `npm run test:e2e`
runs the journeys added in Tasks 8/9 against `STUDIO_E2E_URL` (default
`http://127.0.0.1:4200/studio/`). This task intentionally adds no browser journeys;
until they exist, Playwright reports no tests. Do not duplicate the unit failure
matrix in browser tests.

## Development and browser authentication

Run these in two terminals from the repository root:

```sh
dotnet run --project src/MafPlayground.CLI --no-build -- studio --no-open --url http://127.0.0.1:5051
npm --prefix src/MafPlayground.Studio.Web start
```

Open `http://127.0.0.1:4200/studio/`. Follow **Open secure sign-in**, enter the one-time
code printed by the CLI, and submit the normal `/studio/login` form. The backend
issues an HttpOnly SameSite=Strict cookie. No bearer/code is put in browser storage,
URLs or frontend configuration. A 401 offers this login flow again. No model must
be configured to inspect unavailable entities and schemas.

`proxy.conf.cjs` proxies only `/api/studio/**` and `/studio/login`. It rewrites Host
and an existing Origin to the explicitly configured loopback backend authority.
This preserves same-origin cookies in development without weakening the backend
Host/Origin policy. Both servers bind loopback. To use another backend port:

```sh
STUDIO_BACKEND_URL=http://127.0.0.1:5052 npm --prefix src/MafPlayground.Studio.Web start
```

The proxy rejects non-loopback authorities, credentials and non-HTTP targets.
Do not expose the dev server publicly. Angular's dev server retains its own host
checks; no wildcard allow-list is configured. The proxy rejects a browser Origin
that differs from the incoming dev-server authority before rewriting it. Two
Node unit tests cover this proxy boundary alongside the Angular suite.

## Contracts and state

`generate:contracts` builds the current CLI with `--no-restore`, exports schemas to
a temporary directory with `studio --export-contracts`, and uses pinned
`json-schema-to-typescript` **16.0.0** on **transport only**. Entity schemas/examples
are never compiled into TypeScript. Numeric enum values/names are exported from
actual backend enums. `check:contracts` repeats the operation, compares bytes and
fails on drift without writing the checked-in generated file. Temporary export
files are removed in either mode. A restore must precede these scripts in a fresh checkout.

- `StudioApiService`: cookie-only HTTP DTO mapping and safe errors; inspection
  methods include topology, event pages, spans, payloads and terminal results.
  `entities(profileId?)` requests selected-profile availability through HTTP and
  the shared HTTP/MCP catalog. `topology(entityId, profileId)` sends both IDs as
  query parameters to `/api/studio/v1/topology`; percent escapes are decoded once
  by HTTP query binding, never manually. The unreleased path-based route was removed.
- `StudioRunStore`: reactive catalog/profile/session/run snapshots, selected IDs,
  bounded per-run event lists, gap notices and text projections. `selectEntity`,
  `selectProfile`, `selectSession`, `selectRun`, `submit`, `resetSession`,
  `cancelSelected`, `reconnect` are the shared workspace actions. Session reset
  selects the replacement ID returned by HTTP 200 only when its initiating
  entity/profile/session selection generation is still current. Same-host late
  new/reset sessions remain in history. A host epoch rejects stale restart data.
  `selectProfile` returns a promise; selection updates immediately and
  `availabilityLoading` gates execution/session creation until its guarded catalog
  refresh completes. Input drafts survive availability refreshes. `submit` captures
  and rechecks the complete selection after async validation. Cancellation ack
  merges only its flag into the latest snapshot, preserving terminal data.
- `StudioEventClient.watch(runId, afterSequence, signal)`: fetch-based SSE;
  reconnects from the accepted sequence with bounded backoff, checks `/instance`
  before reopening, ignores duplicates, and keeps watching after terminal events
  for late spans. Aborting observation never calls cancellation. Journal gaps
  trigger authoritative snapshot refresh and a visible incomplete-history notice.
- `/instance` exposes the existing `StudioRunStore.HostInstanceId` through the
  existing run service, without a second registry. On change, stale run/session
  IDs, cursors and captured event projections are discarded.

Input is unknown JSON plus the runtime schema. `vscode-json-languageservice` **5.7.2** (stable release line)
validates nested constraints, produces editor locations and rejects external schema
fetches. Input/schema text is capped at 2 MiB. `JsonView` renders output as escaped,
selectable JSON; chat is escaped text. No model text is interpreted as HTML.

## Assets, visual system and packaging handoff

`src/styles/tokens.css` and `shared/ui` define Precision surfaces, readable 13px UI,
12px code, status labels, focus, reduced motion and responsive panels. DM Sans and
IBM Plex Mono are bundled from pinned Fontsource packages; OFL licenses ship under
`public/licenses`. Monaco **0.57.0** uses its explicit package ESM exports, bundled
JSON/editor module workers and emitted codicon font. No runtime CDN workers/fonts
or remote schema fetching are used. Plain JSON editing remains available if the
enhanced editor cannot initialize.

Production output is `dist/studio/browser`, with `/studio/` base href and lazy
feature/Monaco chunks. Task 10 must package this directory in the existing CLI
host and provide authenticated `/studio/*` deep-link fallback after access
middleware. Node is build-time only. The backend currently serves its explicit
missing-assets page; this task does not claim packaged-host integration.

The real dev UI was exercised under a narrow CSP: scripts/workers same-origin,
styles same-origin plus `unsafe-inline` for Angular/Monaco style elements and
layout attributes, `base-uri 'self'`, no `unsafe-eval`. The dev CSP permits the
loopback HMR WebSocket. The packaged host currently has `base-uri 'none'` and no
style/worker directives: Task 10 must make the narrow equivalent style/worker/base
integration and verify the real packaged host. Do not disable CSP or enable
`unsafe-eval`; production does not need the HMR WebSocket allowance.

Task 8 extends `RunWorkspace`'s main-column/inspector composition with graph and
timeline selection. Task 9 adds comparison and rerun. No fake graph, metrics or
provider billing claims are shown; usage/cost remain unavailable without evidence.

Pinned transitive patches resolve the install audit: DOMPurify 3.4.17 fixes
GHSA-cg2g-xh64-v4q7 / GHSA-mrv2-3pqr-j5jr, Vite 8.3.5 fixes the 8.3.0–8.3.2
launch-editor/filesystem advisories, and Ajv 8.20.0 fixes GHSA-2g4f-4pwh-qvx6 in
build tooling. Ajv is not the runtime validator. `npm audit` reports zero findings.

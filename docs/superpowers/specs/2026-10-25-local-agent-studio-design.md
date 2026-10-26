# Local Agent Studio design

Status: agreed direction, including CLI-hosted tooling clarification.
Implementation has not started.

## Intent and agreed requirements

Build a local development studio for experimenting with the repository's MAF
agents and workflows. The user and their usual Claude Code or Codex session must
be able to inspect and operate the same executions. This is a playground feature
with a complete debugging experience, not a reduced replacement chat screen.

Agreed requirements:

- Separate Angular/TypeScript frontend and ASP.NET Core backend.
- Start the studio through `MafPlayground.CLI -- studio`, analogous to `devui`.
  This command hosts the local debugging tool; it does not require or modify the
  `MafPlayground.AspNetCore` example backend.
- Local, single-user operation; no shared remote deployment.
- Catalog, conversational agents, typed workflow input, streaming, cancellation,
  detailed traces, and an interactive workflow graph in the first delivery.
- Capture available model messages, responses, tool arguments/results, executor
  inputs/outputs, validation feedback, failures, retries, timing and usage by
  default in this development host. Exclude credentials.
- Keep using the user's existing coding-assistant session through MCP; do not
  embed or operate an assistant conversation inside the studio.
- Keep executions in memory. No history across host restarts is required.
- Include comparing executions and rerunning cases with explicit variations.
- Discover entities and schemas from the application runtime registrations. Adding
  an agent/workflow must not require edits to Studio backend or Angular code.
  Studio must not reference concrete sample implementations or maintain a second
  entity allowlist/catalog.
- Treat visual design and usability as first-delivery requirements. Establish a
  deliberate visual direction and review representative mockups before building
  Angular screens; a functional but visually unfinished interface is not enough.

Implementation defaults below are proposals, not additional user requirements.

## Approaches considered

1. Adopt an existing observability product plus a small MAF adapter. Aspire,
   Phoenix and Workshop cover valuable telemetry/MCP features, but verified
   end-to-end typed MAF execution and workflow topology are still missing here.
2. Build a TUI over a local inspection host. It supports terminal workflows, but
   the interactive graph and extensive payload inspection fit a web UI better.
3. **Selected direction: build a local web studio with shared execution and
   inspection services in a development host started by the CLI, exposed through
   HTTP and MCP.** This provides a useful experiment in MAF debugging while
   leaving a future TUI possible. The host belongs to the tool, not the example
   application.

Existing DevUI remains available. OTLP remains available for independent checks
in Aspire or another collector; adopting another viewer is not a prerequisite.

## Existing architecture and reusable evidence

- .NET 10; MAF 1.16.0. DevUI/Hosting packages are preview/alpha. Do not upgrade MAF
  to obtain studio features.
- `MafPlayground.AI.Abstractions` defines `IAIEntity<TInput,TOutput>`, descriptors,
  `IAIEntityRegistry`, `IAIEntityInvoker` and execution/caller context.
- `MafPlayground.AI.Runtime` implements entity resolution, invocation, agent
  sessions, provider-neutral model construction and MAF workflow execution.
- `MafPlayground.AI.AspNetCore` already maps typed entity invocations to HTTP,
  including validation, authorization and stable failure responses. Its current
  request/response endpoint is not a streaming studio job API.
- `MafPlayground.AI.Samples` registers basic agent and translation entity wrappers.
  Basic RAG and repository Help also run in CLI/DevUI, but do not yet have the
  same typed wrapper registrations. Do not assume these surfaces are identical.
- CLI `LocalEntityCatalog` has four entities and schema/examples/topology helpers.
  `InteractiveAgentConsole` handles streaming and `WorkflowExecutionConsole`
  renders native workflow events. Those renderers are not reusable API contracts.
- The current `IMafWorkflowRunner` returns a final typed output. Translation also
  has a native streaming path. Reuse the actual workflow and validators in both
  paths; do not create an alternative studio translation pipeline.
- Current instrumentation includes runtime, agent, model/tool, workflow and cost
  telemetry. The CLI DevUI bridge is transport-specific and listens to fewer
  sources than the complete studio requires. It is not the studio collector.

## Components and ownership

Proposed layout:

- `src/MafPlayground.CLI/Commands/StudioCommand.cs`: new CLI entry point and local
  development composition root, analogous to `DevUICommand`. Starts one ASP.NET
  Core host containing studio services, HTTP/SSE and MCP endpoints and compiled
  Angular assets. The example backend is not started or changed.
- `src/MafPlayground.Studio/`: a non-executable, development-only library for run
  coordination, immutable inspection contracts, event journal, memory store,
  comparison, catalog inspection and endpoint/MCP adapters. Reuse the host's DI
  container, entity registry, sessions and configured providers. Do not build a
  second runtime/service provider inside this process. Split transport from core
  later only when an
  actual dependency boundary needs it; keep provider SDKs out.
- `src/MafPlayground.Studio.Web/`: Angular workspace. In development its proxy
  forwards API/events to the backend; packaged output is served by the host.
- New backend tests live in dedicated `MafPlayground.Studio.Tests` and
  `MafPlayground.Studio.IntegrationTests` projects. Frontend unit/component tests
  live with Angular. Existing runtime/CLI regression tests remain with their
  owning projects; Studio's feature suite does not expand `MafPlayground.Tests`.

The Studio library references Abstractions/Runtime, not AI.Samples, concrete
providers or CLI. Sample assembly references and registration remain in CLI
composition. Catalog and execution consume the existing registry and generic
typed invoker. Replace the CLI's hardcoded catalog with runtime registration
metadata where they overlap. Register examples and optional capabilities alongside
the entity's normal application registration; Studio must not contain sample-ID
switches, concrete casts, per-entity factories or a second catalog.
Add typed wrappers for legacy RAG/Help in Samples without moving repository Help
instructions or CLI capability implementations into reusable core libraries.

## Discovery and extension contract

First-delivery discovery is of explicitly registered application entities at
host startup. It is not arbitrary DLL loading, filesystem scanning by class name
or hot discovery of newly compiled code. After adding an entity to normal host
composition, rebuild/restart as needed; Angular needs no source changes or
entity-specific type generation. Preserve existing duplicate-ID validation.

The catalog enumerates `IAIEntityRegistry.Entities`; each descriptor supplies
identity, kind and typed input/output. Generate JSON schemas from those actual
types and serializer options. The frontend renders those dynamic schemas and
arbitrary result JSON using generic input/inspection components. Only Studio's
transport protocol DTOs are generated as fixed TypeScript types.

Runtime registration may advertise optional neutral streaming, session,
native-topology and outcome-classification capabilities. The standard MAF
registration path supplies these by associating native agent/workflow sources
with the entity at its normal registration, not through a Studio-specific
adapter added for each new agent. Keep MAF-specific source binding in Runtime;
neutral contracts and application libraries must not depend on Studio.
Registration factories must respect the execution scope and immutable profile.

An arbitrary typed entity always has discovery, validated input, execution,
output and available traces. Optional features require actual capability support:
the schema cannot reveal a workflow graph or invent chat streaming/session
semantics. Native workflow graphs come from the registered workflow source; a
custom entity without one remains usable with a clear topology-unavailable state.
Agents expose observed call trees rather than a fabricated declared topology.

Studio never infers domain success from fields named `isValid`, `success` or a
translation-specific output type. A generic run can complete successfully at
the execution level while its domain outcome is unclassified. Optional typed
outcome classifiers registered by the application can express partial failure;
the translation classifier belongs with the translation sample, not Studio.
Absence of examples, classifiers or enhanced views must not prevent execution.

Acceptance includes registering previously unknown test entities with novel
nested contracts and a native workflow with a changed graph. They must appear,
execute and expose schemas/results/topology through both HTTP and MCP with no
Studio code changes. An Angular fixture with unknown IDs/types must also render
them without a frontend rebuild.

Responsibility classification:

| Responsibility | Owner |
| --- | --- |
| Inputs, schema, comparison, run lifecycle, retention | Deterministic C# services |
| Agent decisions and tool selection | Existing MAF agents |
| Branching, validation, retries and aggregation | Existing native MAF workflows/services |
| Model/tool capture and execution correlation | MAF telemetry plus narrow runtime instrumentation |
| Provider/model/profile selection | Host composition and configured adapters |
| HTTP, SSE and MCP | Host adapters over the same studio services |
| Rendering and navigation | Angular |

No new agent is needed to run the studio. No custom agent loop, workflow engine,
retry engine or conversation memory layer is introduced.
The studio host invokes library implementations directly. It is not a proxy to
the example backend and does not alter that backend's authentication/policies.
This supersedes both the initial standalone-executable proposal and the later
proposal to add tooling to the example backend.

## Startup and process boundaries

The user runs `dotnet run --project src/MafPlayground.CLI -- studio` and opens the
local browser UI. Compiled Angular is served by this one tool process; frontend
development additionally uses the Angular dev server and a proxy. Only required
external dependencies (for example Ollama or PostgreSQL) run separately.

The CLI `studio` command composes existing library agents/workflows into its own
local testing runtime, just as `devui` does today. It neither starts an application
backend nor creates an OS process per agent. The normal coding-assistant session
connects to this tool process through MCP.

First delivery captures executions submitted to studio HTTP or MCP. Existing
standalone CLI invocations and runs inside the example backend are other
processes and do not appear automatically. Attaching to external applications or
adding a CLI client mode is future work, not an implicit delivery requirement.

## User experience

The workspace has an entity catalog, run list, input/chat area, interactive graph,
timeline and a detail inspector. Panels share selection and can be resized.

### Visual design and usability

The user selected **Precision** as the visual direction: a dark, compact
investigation workspace with persistent navigation and connected graph, timeline
and inspector panels. Use the [visual design note](2026-10-25-local-agent-studio-visual-design.md)
as the implementation reference. Preserve this direction across catalog/input,
workflow investigation, failed invocation inspection and comparison screens.

The initial [interactive visual exploration](../mockups/studio-visual-directions.html)
contains two proposals with identical simulated data: **Precision**, a dark,
compact investigation workspace with persistent navigation; and **Fieldwork**,
a light, spacious workspace with contextual navigation. Both include catalog,
typed input, workflow investigation and comparison views. These are design
mockups, not Angular implementation or evidence of backend/MCP connectivity.
Precision is the selected base. Fieldwork remains an alternative explored during
design, not a second theme required for the first delivery. The choice establishes
the visual direction rather than approving every mockup pixel or simulated datum.

The selected direction defines typography, color/contrast, spacing, component
density, navigation, panel borders/surfaces, iconography and restrained motion.
Translate these choices into shared design tokens and reusable controls before
implementing feature screens. Distinguish body text, labels, IDs and code with
clear hierarchy; make long JSON, traces and error detail readable.

Maintain consistent loading, empty, unavailable, error, partial-result, running
and cancelled states. Status must use labels/icons as well as color. Graph and
timeline selection, focus and cross-panel navigation must be discoverable.
Preserve scroll/zoom/selection during streaming instead of moving the workspace
unexpectedly. Use motion to communicate changes, not to animate every token.

Optimize the investigation experience for desktop. Verify representative
1440x900 and 1024x768 viewports with resizable/collapsible panels, without requiring
a full mobile debugger in the first delivery. Include visible keyboard focus,
usable keyboard alternatives and WCAG AA contrast for text and controls.

Visual acceptance requires reviewing the real running interface with realistic
data, including long identifiers, nested payloads, parallel/repeated nodes and
failure/empty states. Keep screenshots as review evidence when useful; do not
turn this into an extensive screenshot-snapshot or browser-matrix test suite.
The functional test strategy remains unchanged.

- Entity details include input/output schemas, examples, description and declared
  capabilities. JSON editing with validation is required; automatically generated
  forms for arbitrary schemas are not required for the first delivery.
- Agents stream responses and preserve explicit MAF conversation sessions.
- Workflow requests use their typed public contract, without DevUI chat envelopes.
- The graph displays native workflow topology. Runtime overlays distinguish
  active/completed/failed invocations and branches not taken. Selecting a node
  lists all its invocations; retries are never collapsed into one final status.
- Selecting a span selects its executor when correlated; selecting an executor
  filters the timeline. Uncorrelated spans remain visible and labeled.
- Inspect JSON/text payloads, messages, validation feedback and stable errors;
  search/filter by operation, branch, status and time.
- Agents display their observed call hierarchy, clearly distinct from a declared
  native workflow graph. Do not invent an agent's future execution graph.
- Two completed runs can be compared by input, output, outcome, executor
  invocations, errors, timing, model calls, usage and available cost estimates.
  Differences are structural/textual; do not add live-model evaluation by default.
  First-delivery comparison is limited to runs in the same host lifetime, such as
  input/profile variations. Rebuilding/restarting after a code change loses the
  baseline. Cross-restart code comparisons and snapshot export/import are future
  work, consistent with the decision not to preserve history.

## Run model, sessions and execution isolation

Use immutable IDs for execution, session, graph node, executor invocation and
span. Each run has a monotonic event sequence and a topology snapshot. A trace
ID alone is not the application run identity.

Lifecycle: `queued -> running -> completed | partially_failed | failed |
cancelled | timed_out`. Cancellation requested is a separate flag until execution
actually stops. A successful transport does not imply every workflow branch
succeeded. Application-registered classifiers classify domain outcomes
deterministically; translation's classifier uses branch validation results and
belongs with that sample. Studio consumes the common classification contract;
unknown domain outcomes remain explicitly unclassified.

Reuse existing MAF sessions/session-store ownership. UI and MCP use the same
host-owned local caller identity. Reject overlapping turns for the same session;
allow independent sessions/runs within the concurrency budget. Reruns use a fresh
session by default; continuing an existing session must be explicit.

The host supervises jobs with fresh DI scopes, linked timeout/shutdown/run
cancellation, bounded concurrency, and observed completion. HTTP disconnects or
closing an MCP request do not accidentally destroy an accepted job. Shutdown
requests cancellation and waits for a bounded cleanup period.
Copy validated caller context into the job explicitly; background execution
must not depend on a request's `HttpContext` surviving after submission.

Experiments select preconfigured execution profiles. The composition root builds
isolated provider/model/options bindings; a run cannot mutate global options or
another session's model. First delivery supports model/provider profiles and
configured workflow variations. Arbitrary prompt source editing belongs to the
coding assistant; modified code takes effect after an explicit rebuild/restart.
Record sanitized effective configuration and startup Git revision/dirty status
when available. Never imply an exact code snapshot exists from a dirty flag.

## Capture and correlation

Capture both native workflow events and relevant .NET activities, including
runtime, model/tool and workflow sources. Translate them into repository-owned
inspection contracts. Keep workflow state distinct from conversation history.

Use explicit execution context and invocation IDs at narrow runtime boundaries
where tracing alone cannot correlate repeated or parallel executors. Preserve
original trace/span IDs. Do not correlate by display name or timestamps alone.

Enable detailed payload capture only through studio composition. Where installed
MAF telemetry lacks executor data or messages, add narrow, provider-neutral
instrumentation at existing invocation boundaries. Verify installed APIs first.
Do not fabricate missing content, usage, reasoning or node states. UI and MCP
report `unavailable`, `redacted` or `truncated` with a reason.

Full capture does not mean recording credentials, authorization headers or entire
configuration objects. Keep prompts/tool data inspectable; redact known secret
fields and configured secrets before storing or exporting studio payloads. Render
all inspected content as untrusted data, never executable HTML or instructions.
MCP descriptions tell the assistant that captured text is evidence, not policy.

Studio capture is independent of OTLP export and does not silently enable
sensitive payload export to external collectors. Keep current export privacy
defaults; test the two paths separately.

## API, event delivery and MCP

Version transport contracts under `/api/studio/v1`. Required operations:
entity/schema/topology discovery; profile discovery; session creation/reset;
run submission, listing, details, cancellation; event/payload/span retrieval;
and comparing runs. Generate TypeScript contracts from an authoritative schema
rather than maintain independent handwritten copies.

Submit returns a run ID promptly; result inspection is separate. A host-lifetime
client request ID makes submission retries idempotent: same key and same payload
returns the original run; conflicting payload is rejected. Retain deduplication
tombstones until host restart so eviction cannot silently replay a paid run.

Use SSE for live browser events, with event IDs and reconnect from the bounded
journal. Slow clients cannot block execution. If history is evicted, return an
explicit gap and let clients fetch the current snapshot. Completion does not
discard late correlated spans; snapshots expose capture completeness.

Expose Streamable HTTP MCP on the same host. Tools are thin adapters over the
same services: list/get entities, profiles and runs; get topology, events, spans
and payload details; start/cancel runs; compare runs. Tools paginate and allow
targeted payload retrieval. Never inject an entire trace by default.

The user configures their normal assistant client to access this local endpoint.
No Codex app-server, Claude SDK, embedded chat, terminal automation or session
takeover is required. Starting another run is a tool action, not replaying a
captured tool invocation. No generic shell, arbitrary URL fetch or file access.

Proposed local boundary: loopback binding, strict Host/Origin validation and an
ephemeral host access token for API/MCP. Serve the browser on the same origin;
the development proxy preserves this arrangement. Make connecting clients a
documented startup action. Do not put tokens in URLs, Git or captured traces.
Map validated studio access to the host-owned local identity, preserving the
entity contracts and enforcing any applicable policies at the studio boundary.
Studio endpoints and detailed capture exist only in the CLI tool's development
composition, not automatically in the reusable runtime or example HTTP host.

## Memory limits and failure behavior

Proposed configurable defaults: four concurrent runs, 100 retained completed
runs, 128 MiB capture budget and 2 MiB per payload. A bounded journal and small
metadata budget accompany these limits. Capacity checks reject new runs when
active executions cannot fit; evict oldest completed runs first. Never evict
active run ownership/cancellation state. Deduplication metadata also has a cap;
reject new submissions if it fills instead of forgetting keys.

Large payloads are visibly truncated. Missing provider usage/cost stays unknown,
not zero. Store error category and useful development details after redaction.
Inspection and transport failures do not trigger a second model/tool execution.
Workflow retries remain governed by existing typed policies.

No database, checkpoint persistence, restart recovery, team accounts or remote
hosting is introduced. A restarted host reports a new host instance ID so clients
discard stale run IDs. Re-running a case is a new execution, not deterministic
replay, time travel or resuming an arbitrary node.

## Verification and acceptance

First delivery includes all four current entities: basic, Basic RAG, repository
Help and translation. Missing retrieval/provider setup disables the affected
capability with an actionable reason rather than obscuring the whole catalog.

Testing is layered by responsibility. Backend unit tests verify run lifecycle,
deduplication, limits, redaction, mapping and comparisons with controlled clocks
and dependencies. Backend component tests exercise real MAF orchestration and
tools with fake semantic dependencies. In-memory HTTP/MCP contract tests verify
handlers, serialization and shared-state parity without treating them as unit
tests or browser E2E. Angular unit/component tests verify event projection,
clients, forms, selection, graph adapters and safe payload rendering.

Browser E2E is deliberately limited to three complete journeys: typed translation
investigation with graph/traces and rerun comparison; agent conversation with a
real deterministic tool and session continuation/reset; and workflow cancellation
followed by another execution. Use real Studio HTTP/events and MAF with scripted
model responses. Do not multiply browsers, providers, validation permutations or
individual component checks into an E2E matrix. Add a future browser regression
only when a browser-specific failure cannot be covered at a cheaper layer.

Keep loopback/packaging integration and live-provider checks explicit and
separate. Real-model smoke checks do not imply a new model-quality evaluation
suite. The default fast tests need no live model, external infrastructure or
network. The implementation plan enumerates test classes and exact assertions;
there is no arbitrary test-count or coverage-percentage target.

Required evidence:

1. A previously unknown agent and native workflow registered only in test host
   composition appear and execute through HTTP/MCP. Novel schemas, outputs and
   workflow branches render without Studio/Angular edits. The Studio assembly
   does not reference Samples or concrete providers; optional capabilities have
   explicit fallbacks, and duplicate entity IDs still fail deterministically.
2. A typed translation run executes through HTTP and MCP without chat adaptation.
3. Angular displays a real native graph and updates individual parallel/repeated
   executor invocations; selection links to actual captured traces.
4. Deterministic fake clients exercise tool arguments/results, invalid structured
   output, validation feedback, bounded retry and partial failure.
5. Agent text streams in order; sessions preserve context and reject overlapping
   turns. UI/MCP observe the same run, session, output and cancellation state.
6. Timeout, cancellation races, shutdown, parallel isolation, submission retries,
   SSE reconnect/gaps and retention limits have deterministic tests.
7. Prompts/messages and executor payloads are inspectable where supported;
   credentials never enter the capture store, browser or MCP output. Missing or
   truncated data is explicit. External telemetry privacy defaults remain intact.
8. The three selected browser E2E journeys cover complete user flows, including
   graph navigation, payload inspection, sessions, cancellation and comparisons.
   Focused in-memory contracts and opt-in real MCP transport tests verify MCP;
   these are not additional browser E2E. Registration tests alone are insufficient.
9. Opt-in real-model checks exercise representative agents/workflows. Provider
   swapping changes composition/configuration, not studio or workflow contracts.
10. Review the selected visual direction and the real implementation at the
   specified desktop sizes. Verify hierarchy, readability, consistent states,
   keyboard focus and contrast; passing functional tests alone is insufficient.
11. Run repository formatting/build/tests, frontend checks/build and relevant
   integration checks. Update canonical manuals and generated CLI reference if
   commands change; document repository-help re-ingestion needs.

## Dependencies, sequencing and implementation discretion

New frontend dependencies are expected: Angular/tooling, a graph layout/rendering
library and a JSON editor. An official .NET MCP server package is justified for
protocol handling if compatible. Choose supported versions and graph/editor
libraries during planning after checking official docs, licenses, Angular
compatibility and actual MAF 1.16 APIs. No MAF upgrade is authorized.

Build in vertical slices: shared execution/capture contracts; typed execution
and streaming; graph and inspection; MCP parity; comparison and failure-path
verification. These are implementation checkpoints, not cuts to first-delivery
scope. Precise endpoint/tool naming, graph/editor dependency selection and
project splitting are implementer choices within these boundaries. Angular
feature screens follow the selected Precision direction and linked design note.

The user approved the CLI-hosted tool direction after clarifying the process
boundary. Prepare the implementation plan against this corrected design using
the repository workflow before writing runtime code or adding dependencies.

## Design review

A fresh builder review identified one scope ambiguity: code-change comparisons
versus ephemeral history. Resolved by limiting first-delivery comparisons to the
same host lifetime. The assistant can inspect a failure and edit code, but after
rebuild/restart it cannot query the old run in the studio. No persistence or
snapshot transfer is implied. Other implementation choices remain as listed.
Subsequent user clarification establishes that the ASP.NET Core example remains
independent: studio is started by the CLI and owns its local debugging host.
User testing feedback establishes dedicated Studio backend test projects,
explicit unit/component coverage and only three selected browser E2E journeys.
User feedback also makes visual quality an explicit requirement; visual direction
and representative mockups were reviewed before frontend screen implementation.
The user prefers Precision; it is the selected base for implementation.
User clarification makes dynamic registry-based discovery and freedom from
per-entity Studio/frontend changes a hard requirement. Concrete sample factories
and domain outcome classification must stay outside Studio.

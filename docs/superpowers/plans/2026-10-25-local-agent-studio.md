# Local Agent Studio Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a local Angular studio with typed MAF execution, detailed capture, interactive topology, comparisons and MCP access from the user's normal coding-assistant session.

**Architecture:** The existing CLI gains `studio`, which starts one development-only ASP.NET Core host and serves the compiled frontend. A non-executable Studio library supplies execution supervision, memory storage, inspection, HTTP/SSE and MCP over the same state. Existing agents/workflows remain in their reusable libraries; the ASP.NET Core example remains independent.

**Tech Stack:** .NET 10, pinned MAF 1.16.0, Angular/TypeScript, Cytoscape.js, Monaco Editor, the official MCP C# HTTP SDK, SSE, dedicated Studio xUnit projects, frontend unit/component tests and three selected browser E2E flows.

**Spec:** [Local Agent Studio design](../specs/2026-10-25-local-agent-studio-design.md)

## Global constraints

- Start through `dotnet run --project src/MafPlayground.CLI -- studio`; no new backend executable and no dependency on the example backend.
- First delivery includes all four entities, detailed traces, interactive graph, HTTP/MCP parity, comparison and cancellation.
- MAF 1.16.0 stays pinned. Provider SDKs remain in adapters/composition roots.
- One service provider per tool process. Do not mutate singleton options or share mutable execution state between profiles.
- Default full local capture excludes credentials; external OTLP payload export keeps existing privacy defaults.
- In-memory history only. Comparisons are within one host lifetime; no replay/time-travel or external-process attachment claims.
- Proposed defaults: four concurrent runs, 100 completed runs, 128 MiB capture and 2 MiB per payload. Limit metadata and event journals too.
- Existing CLI, DevUI and example HTTP behavior stay supported. Core libraries do not reference Studio.
- English code/documentation. Honor existing untracked documentation and user changes.
- Put new Studio backend tests in `MafPlayground.Studio.Tests` and external transport checks in `MafPlayground.Studio.IntegrationTests`. Existing-library regression tests stay with their owning projects.
- Prefer backend unit/component tests and frontend unit/component tests. Browser E2E covers only the three full journeys below; do not reproduce the validation/failure matrix in browsers.
- Visual quality is a delivery requirement. Review visual directions/mockups before implementing frontend screens; validate the running interface visually in addition to functional tests.
- Precision (direction A) is the selected visual base; follow the [visual design note](../specs/2026-10-25-local-agent-studio-visual-design.md). Fieldwork is not an additional theme requirement.
- Studio discovers all normally registered entities dynamically. No Samples/provider references, sample-ID switches, per-agent Studio factories, second registry or entity-specific Angular components/type generation.

## Testing strategy and ownership

The test boundary is the behavior being verified, not a target test count. Tests assert meaningful outcomes and invariants, not DI registration, private methods, snapshots of entire pages or exact LLM wording. Use theories for equivalent input classes rather than duplicate scenarios across layers. No live models, database, containers or network are required for the default backend/frontend suites.

| Suite | Location | What runs | Default fast verification |
| --- | --- | --- | --- |
| Backend unit | `tests/MafPlayground.Studio.Tests/Unit/` | Studio services with fake executors, clocks and capture sinks | Yes |
| Backend component | `tests/MafPlayground.Studio.Tests/Component/` | Real MAF workflows/agents and Studio adapters; only semantic/model/external dependencies replaced | Yes |
| HTTP/MCP contracts | `tests/MafPlayground.Studio.Tests/Contract/` | Real route handlers, serialization, middleware and MCP SDK over in-memory HTTP/TestServer | Yes |
| Frontend unit/component | `src/MafPlayground.Studio.Web/src/app/**/*.spec.ts` | Angular state, HTTP/event clients, components and graph/editor adapters with controlled services/events | Yes |
| Local transport integration | `tests/MafPlayground.Studio.IntegrationTests/` | Real loopback HTTP/SSE/MCP and packaged CLI startup with fake models | Explicit opt-in |
| Browser E2E | `src/MafPlayground.Studio.Web/e2e/` | Three complete browser-to-backend-to-MAF flows below | Separate explicit command |
| Live-model smoke | `tests/MafPlayground.Studio.IntegrationTests/LiveModel/` | Representative configured provider checks, no LLM-as-judge | Separate opt-in |

`MafPlayground.Studio.Tests` references Studio and the relevant AI libraries, not the CLI executable. `MafPlayground.Studio.IntegrationTests` may reference CLI for tool composition/startup. Use xUnit conventions from the repository. Introduce `Microsoft.AspNetCore.TestHost` as a test-only dependency for in-memory transport checks; pin a .NET 10-compatible patch centrally. Neither suite uses an alternative copy of Studio business logic.

Only add/extend tests in `MafPlayground.Tests` when production changes affect that existing owner: CLI parsing, neutral runtime factory overloads, existing session contracts or executor instrumentation. Do not move unrelated tests or add Studio's feature matrix there.

### Backend test inventory

Paths are relative to `tests/MafPlayground.Studio.Tests/`. Each listed assertion belongs in this layer, not in a browser scenario.

| Test class | Specific behavior and assertions | Task |
| --- | --- | --- |
| `Unit/StudioCatalogTests` | Four unique logical entities; serialization-compatible schemas; one unavailable retrieval setup leaves other entries usable; discovery makes zero model/tool calls | 1 |
| `Component/StudioDiscoveryTests` | Register unknown test-assembly entities with nested/new input/output shapes and a native workflow only in host composition; generic execution, schemas and graph work without a Studio adapter; duplicate IDs rejected, optional capabilities absent remain usable | 1, 3 |
| `Component/WorkflowTopologyMapperTests` | Real translation graph includes configured branches, start/output nodes and retry edges; mapper preserves stable IDs and cycles without executing the workflow | 1 |
| `Unit/StudioRunServiceTests` | Duplicate submission before/after completion starts once; changed input with same key conflicts; different sessions may run concurrently; fifth active run observes the configured capacity policy | 2 |
| `Unit/StudioRunLifecycleTests` | Cooperative cancellation, timeout and shutdown produce the right terminal outcome; cancellation losing a completion race does not overwrite success; viewer disconnection does not cancel accepted work | 2 |
| `Unit/StudioRetentionTests` | Oldest completed eviction, active control-state preservation, byte/payload limits, bounded tombstones, duplicate request after eviction and explicit journal-gap metadata | 2 |
| `Component/StudioEntityExecutorTests` | Actual typed adapters run all four entities; invalid input makes zero external calls; scripted model requests a real deterministic tool; one streaming execution produces both deltas and final output; translation retries/partial failure retain typed results | 3 |
| `Component/StudioProfileIsolationTests` | Concurrent models receive their own requests, tool follow-up and validation calls; profile-specific workflow options and executors do not leak; invalid profile is rejected before execution | 3 |
| `Component/StudioSessionTests` | A second turn receives prior history; reset creates fresh history; overlapping turns and profile mismatch are rejected; failed/cancelled turn does not commit a successful-looking partial state | 3 |
| `Unit/StudioCaptureTests` | Normalize messages/tool data, attributes, usage and missing usage; preserve original trace hierarchy and late-span completeness; capture/serialization failure does not fail the underlying run | 4 |
| `Component/StudioCorrelationTests` | Parallel and repeated executors get distinct invocation IDs; events/spans reference the actual executor; unknown correlation stays unknown rather than creating an invented edge | 4 |
| `Unit/StudioCapturePrivacyTests` | Known credential fields/values removed before storage; multibyte payload capped at 2 MiB; missing/truncated content has a reason; Studio capture does not silently enable sensitive external OTLP export | 4 |
| `Contract/StudioHttpTests` | Endpoint payload/status/schema contract, SSE sequence/cursor/gap mapping, cancellation route, invalid/unknown IDs, pagination and safe ProblemDetails | 5 |
| `Unit/StudioAccessPolicyTests` | Reject remote/credentialed URL, wrong Host/Origin and invalid bootstrap/bearer credentials; bootstrap code is single-use; local identity is explicit | 5 |
| `Contract/StudioMcpContractTests` | Official SDK discovers bounded tools, validates arguments and serializes results; HTTP and MCP operate on the same run; retry request starts once; access failure rejects tool execution | 6 |
| `Unit/StudioRunComparerTests` | Ignore object-property ordering, retain array ordering, expose input/profile/output changes and unmatched invocations; missing metrics remain unknown; evicted/cross-host baseline is rejected | 9 |

In-memory HTTP and MCP tests are contract/component tests, not unit tests and not browser E2E. They verify the transport glue once; exhaustive lifecycle/privacy/input cases remain in the services' tests.

### Frontend test inventory

Use Angular TestBed and the generated runner. Stub the API/event source for UI behavior; use real reducers/projections and templates under test. Avoid asserting Cytoscape/Monaco internals.

| Test file(s) | Specific behavior and assertions | Task |
| --- | --- | --- |
| `core/studio-run.store.spec.ts` | Ordered/idempotent event projection, concurrent-run isolation, host restart reset, terminal state and preservation of selection after updates | 7 |
| `core/studio-api.service.spec.ts` | Correct typed requests and HTTP error mapping using Angular's HTTP testing utilities | 7 |
| `core/studio-event-client.spec.ts` | Incremental SSE decoding, split/multibyte frames, reconnect cursor, gap refresh, duplicate events and unsubscribe without cancelling the run | 7 |
| `catalog/entity-catalog.component.spec.ts`, `input/run-input.component.spec.ts` | Disabled-entity explanation, profile selection, valid/invalid JSON/schema diagnostics and prevention of invalid submissions | 7 |
| `core/dynamic-entity.spec.ts` | An unknown entity ID, nested schema/new properties, arbitrary output JSON and changed graph arrive from discovery; generic components display/submit them with no entity-specific frontend types or branches | 7, 8 |
| `chat/agent-chat.component.spec.ts`, `runs/run-controls.component.spec.ts` | Append streamed content, preserve explicit session, reset, in-flight controls and cancellation of only the selected run | 7 |
| `graph/graph-adapter.spec.ts`, `graph/workflow-graph.component.spec.ts` | Stable IDs, retry cycles, multiple invocations per node, selection events, preserving pan/zoom and teardown; provide a fake graph driver | 8 |
| `traces/trace-timeline.component.spec.ts`, `traces/span-inspector.component.spec.ts` | Parent hierarchy, node/span linking, filters, repeated invocations and missing metrics represented as unknown | 8 |
| `traces/payload-viewer.component.spec.ts` | Lazy retrieval, escaped malicious markup and visible redacted/truncated/unavailable states | 8 |
| `comparison/run-comparison.component.spec.ts` | Render server-provided diffs, unknown metric deltas and unavailable baseline; rerun makes a new request/session by default | 9 |

### Exactly three browser E2E journeys for the first delivery

1. `translation-investigation.spec.ts`: select translation, submit typed input, follow parallel branches and a scripted retry/partial failure, select an executor and inspect its real captured payload, rerun with a corrected case and compare results. Check a basic graph pan/zoom interaction here.
2. `agent-session.spec.ts`: select Basic agent, stream a response involving the real deterministic date/time tool, inspect its arguments/result, send a second turn using the same session and reset the session.
3. `cancel-execution.spec.ts`: start a cooperatively blocked workflow, observe a running node, cancel, verify the terminal state and ability to start a subsequent execution.

Use built Angular, the real Studio backend and real MAF execution. Replace only the model/external calls with scripted fakes; do not mock HTTP, the run store, graph data or event capture. A test-only host fixture under `src/MafPlayground.Studio.Web/e2e/fixtures/StudioFixture` supplies these fakes; do not add fake-model/test switches to the product CLI or API. These tests validate wiring and user journeys, not model quality.

Run one desktop Chromium configuration initially. No per-model/provider/browser matrix, validation-error permutations, snapshot suite or extra E2E per component. Backend packaging, raw MCP, access-policy and edge-case SSE checks have their own cheaper suites. Add a browser regression only for a proven browser-only failure that lower layers cannot catch; document that reason rather than grow this suite automatically.

### Commands and gates

- Fast backend: `dotnet test tests/MafPlayground.Studio.Tests/MafPlayground.Studio.Tests.csproj`.
- Frontend: `npm --prefix src/MafPlayground.Studio.Web run test:unit`, plus `run typecheck`, `run check:contracts` and `run build`.
- Local transport: `RUN_STUDIO_INTEGRATION_TESTS=true dotnet test tests/MafPlayground.Studio.IntegrationTests --filter Category=StudioTransport`.
- Browser: `npm --prefix src/MafPlayground.Studio.Web run test:e2e`; starts its dedicated fixture and runs only the three files above.
- Live models: separate `RUN_STUDIO_LIVE_MODEL_TESTS=true` and provider configuration, with `--filter Category=StudioLiveModel`; never enabled by the local-transport switch.
- Extend `scripts/verify.sh` to include the dedicated fast Studio backend suite. Keep browser, local-network and live-model commands separate and explicit; frontend checks have their own documented entry point.

The implementer reports which suites actually ran and their results. No percentage coverage target or arbitrary unit-test count is a delivery requirement.

## Review focus

1. Concurrent profiles cannot reuse a singleton agent/client created for the first model; test both client selection and model/tool follow-up turns (Task 3).
2. A repeated executor and parallel branches cannot be correlated by display name; test distinct invocation IDs and an explicit unknown correlation state (Task 4).
3. Browser/MCP request retries cannot execute a model/tool twice; test duplicate submissions before completion and after result eviction (Task 2).
4. Full capture can contain secrets, malicious markup or oversized Unicode data; verify redaction before storage, byte-aware truncation and safe rendering (Tasks 4, 8).
5. Disconnecting a viewer is not cancellation, and cancelled work can still finish; test reconnect gaps and cancellation/completion races without false terminal states (Tasks 2, 5).

## Dependency and API evidence

- Installed workflow XML confirms `ReflectExecutors`, `ReflectEdges`, `StartExecutorId`, `ExecutorInvokedEvent(message)` and `ExecutorCompletedEvent(result)` in MAF 1.16.0. Inspect concrete edge/event properties before coding the mapper; do not parse Mermaid for topology.
- Current agent/entity paths return final outputs; translation has a native streaming path. Add neutral streaming/capture seams instead of executing twice to obtain both stream and result.
- Current Basic agent, RAG/Help and translation registrations include singletons. Per-run profile isolation requires explicit factories; a new DI scope alone is insufficient.
- Angular's [compatibility table](https://angular.dev/reference/versions) lists Angular 22 with Node ^24.15.0 and TypeScript >=6.0 <6.1. The inspected machine has Node 24.22.1. Pin actual published compatible patch releases and the npm lockfile in Task 7.
- Use [Cytoscape.js](https://js.cytoscape.org/) directly through a component adapter, including pan/zoom/selection. Use its built-in layouts initially; support retry cycles explicitly, without another Angular graph wrapper.
- Use [Monaco Editor](https://github.com/microsoft/monaco-editor) directly, lazy-loaded with bundled workers and JSON schema validation. Avoid an Angular wrapper version dependency.
- Use the [official MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/getting-started.md) for HTTP transport. Published package versions, not repository `main`, determine registration APIs and protocol compatibility. Task 6 must test a real client handshake.
- No new packages are installed during planning. MCP protocol handling and frontend graph/editor rendering justify their respective production dependencies. Pin packages centrally/on the frontend lockfile, document licenses and transitive MAF/MEAI compatibility, and reject an implicit MAF upgrade.

## Contract and file map

Create `src/MafPlayground.Studio/MafPlayground.Studio.csproj` as a library with an ASP.NET Core framework reference and references to Abstractions/Runtime, not Samples, concrete provider adapters or CLI. Only CLI composition references/registers the actual sample agents and workflows. Add Studio and the dedicated `tests/MafPlayground.Studio.Tests/MafPlayground.Studio.Tests.csproj` and `tests/MafPlayground.Studio.IntegrationTests/MafPlayground.Studio.IntegrationTests.csproj` projects to `MafPlayground.slnx`. Keep Studio's feature tests out of the pre-existing general test projects.

Discovery is startup enumeration of normal runtime registrations, not arbitrary assembly loading or hot code reload. Entity input/output schemas are fetched dynamically; generated TypeScript describes the fixed Studio protocol, not a union of current sample entity types. New entity code may require rebuilding/restarting the CLI host, never editing/rebuilding Angular solely to recognize that entity. Optional native topology/streaming/session/classification capabilities come from runtime registration and have explicit unavailable/unsupported fallbacks.

Organize this library into `Contracts/`, `Catalog/`, `Execution/`, `Capture/`, `Comparison/`, `Http/` and `Mcp/`. Each class listed in a task has a same-named file; records sharing one aggregate can share its contract file. Do not put all behavior in endpoint handlers.

Contract names used across tasks:

- `StudioRunRequest(string RequestId, string EntityId, string ProfileId, JsonElement Input, string? SessionId)`.
- `StudioRunReceipt(string HostInstanceId, string RunId)`.
- `StudioExecutionContext(string RunId, string EntityId, string ProfileId, string? SessionId, AICallerContext Caller)`.
- `StudioEvent(string RunId, long Sequence, DateTimeOffset Timestamp, string Kind, string? ExecutorId, string? InvocationId, string? SpanId, JsonElement Data)`; IDs are explicit, not inferred from display text.
- `StudioRunSnapshot`: receipt IDs, immutable submitted input/profile metadata, lifecycle, cancellation flag, typed-result JSON, outcome classification, topology, totals, capture completeness and journal range.
- `StudioEntityDescriptor`: existing entity identity/kind/description plus input/output schema, example, capabilities and availability reason.
- `StudioTopology`: stable node IDs, edge IDs/source/target/kind/label and start node; distinguish native topology from observed calls.
- `StudioSpan`: original trace/span/parent IDs, operation, times, status, attributes and optional executor/invocation IDs.
- `StudioPayload`: ID, content type, content, original/stored byte counts and `available | unavailable | redacted | truncated` state/reason.
- `StudioEventPage`: ordered events, next cursor, earliest retained sequence and explicit gap indicator. Other lists use opaque bounded cursors.
- `StudioComparison`: run IDs, input/config/output differences, invocation/error differences and optional metric deltas; missing values stay unknown.

## Task 1: Reusable catalog and native topology

**Files:** Create Studio project, `Contracts/StudioEntityDescriptor.cs`, `Contracts/StudioTopology.cs`, `Catalog/IStudioCatalog.cs`, `Catalog/StudioCatalog.cs`, `Catalog/WorkflowTopologyMapper.cs`; extend Runtime `Registry/AIEntityRegistration.cs` and `AIBuilder.cs` with optional registered MAF-source/inspection capabilities; modify CLI `Inspection/LocalEntityCatalog.cs`, `Inspection/EntityInputRenderer.cs`; add `tests/MafPlayground.Studio.Tests/Unit/StudioCatalogTests.cs`, `Component/WorkflowTopologyMapperTests.cs` and `Component/StudioDiscoveryTests.cs` plus project references and the dedicated test project.

**Interfaces:** `IStudioCatalog.ListAsync(CancellationToken) -> ValueTask<IReadOnlyList<StudioEntityDescriptor>>`; `GetTopologyAsync(string entityId, string profileId, CancellationToken) -> ValueTask<StudioTopology>`; schemas derive from actual typed contracts using the current serializer conventions. Metadata augments `IAIEntityRegistry`, not a separate competing registry. Associate the existing `IMafAgentSource`/`IMafWorkflowSource` factories with normal entity registration and expose capabilities through Runtime, rather than resolving concrete sample sources inside Studio. Topology absence is an explicit result state, not a discovery failure.

- [x] Write failing tests asserting each of the four logical IDs appears once, schemas match serialization, and missing retrieval configuration disables only the affected entities.
- [x] Add unknown test-assembly entity types with nested records, arrays, optional fields and enum values; register them only through the standard runtime builder. Assert generic discovery/schema/results work, duplicate IDs fail and missing examples/capabilities do not exclude the entity. Check the Studio production assembly has no Samples or concrete provider references.
- [x] Add failing topology tests asserting configured language branches, start node, aggregation, labels and retry cycles; catalog inspection must invoke no model or tool.
- [x] Run `dotnet test tests/MafPlayground.Studio.Tests --filter 'FullyQualifiedName~StudioCatalogTests|FullyQualifiedName~WorkflowTopologyMapperTests|FullyQualifiedName~StudioDiscoveryTests'`; confirm failures express missing behavior.
- [x] Implement catalog/schema/topology mapping entirely over runtime registrations/capabilities. Move existing examples/topology bindings to normal sample/CLI registration and remove duplicate CLI entity lists where applicable. Store repository Help definition and command-help implementation at CLI composition, not in Studio core.
- [x] Run the targeted tests and existing entity-inspection tests; require passing assertions before checkpointing this task.

## Task 2: Bounded run supervision and journal

**Files:** Create contracts `StudioRunRequest.cs`, `StudioRunSnapshot.cs`, `StudioEvent.cs`, `Execution/IStudioRunService.cs`, `IStudioEntityExecutor.cs`, `IStudioEventSink.cs`, `StudioRunService.cs`, `StudioRunStore.cs`, `StudioEventJournal.cs`, `StudioOptions.cs`; add `StudioRunServiceTests.cs` and `StudioRetentionTests.cs` under `tests/MafPlayground.Studio.Tests/Unit/`, plus `StudioRunLifecycleTests.cs`.

**Interfaces:** `IStudioRunService.StartAsync(StudioRunRequest, AICallerContext, CancellationToken) -> ValueTask<StudioRunReceipt>`; `GetAsync(string runId, CancellationToken) -> ValueTask<StudioRunSnapshot?>`; `CancelAsync(string runId, CancellationToken) -> ValueTask<bool>`; `ReadEventsAsync(string runId, long after, int limit, CancellationToken) -> ValueTask<StudioEventPage>`; `WatchAsync(string runId, long after, CancellationToken) -> IAsyncEnumerable<StudioEvent>`. Define `IStudioEntityExecutor.ExecuteAsync(StudioRunRequest, StudioExecutionContext, CancellationToken) -> ValueTask<JsonElement>` and `IStudioEventSink.Record(StudioEvent)` now; use a fake executor until Task 3. Add bounded list/payload/span queries to this inspection service as their contracts land.

- [x] Write tests for exactly one execution under concurrent duplicate requests, conflict on changed input, duplicate lookup after eviction, same-session overlap rejection, four-run capacity and independent sessions.
- [x] Write fake-TimeProvider tests for timeout, shutdown, cancel/complete races, retention (100 completed/128 MiB), metadata exhaustion and a slow subscriber with an explicit journal gap. Cancellation must not mark a run terminal until execution observes it.
- [x] Run `dotnet test tests/MafPlayground.Studio.Tests --filter 'FullyQualifiedName~StudioRunServiceTests|FullyQualifiedName~StudioRunLifecycleTests|FullyQualifiedName~StudioRetentionTests'`; observe expected failures.
- [x] Implement a bounded supervised queue/worker service, per-run async DI scopes, idempotency tombstones, cancellation ownership and immutable snapshots. Use `TimeProvider`, await job completion and preserve active run control state under capture pressure. Do not bind accepted jobs to HTTP request lifetimes.
- [x] Repeat targeted tests; require zero duplicate executions and deterministic lifecycle assertions.

## Task 3: Generic typed execution, registered capabilities and isolated profiles

**Files:** Implement Task 2's executor contract in Studio `Execution/StudioEntityExecutor.cs`; create `StudioExecutionProfile.cs`; modify Runtime `Providers/AIChatClientFactory.cs`, `Agents/MafAgentFactory.cs`, `Registry/AIEntityRegistry.cs`, registration capabilities and `Workflows/MafWorkflowRunner.cs` for narrow neutral profile/streaming seams; modify Samples agent/workflow factories and registrations as needed; add typed RAG/Help wrappers and the translation outcome classifier in their sample directories. Add `Component/StudioEntityExecutorTests.cs`, `Component/StudioProfileIsolationTests.cs` and `Component/StudioSessionTests.cs` in `MafPlayground.Studio.Tests`; extend existing runtime tests in `MafPlayground.Tests` only for changed neutral contracts.

**Interfaces:** `IStudioEntityExecutor.ExecuteAsync(StudioRunRequest request, StudioExecutionContext context, CancellationToken) -> ValueTask<JsonElement>` delegates to generic `IAIEntityInvoker` and optional runtime streaming capabilities. Stream events through the run-scoped event sink, not a second invocation. Runtime-owned registration factories create execution-scoped instances using immutable `StudioExecutionProfile` values translated to neutral runtime options at the boundary. Extend neutral client/agent factory methods with explicit `ResolvedAIEntityRuntimeOptions` overloads while retaining default configured behavior. Studio must not construct samples or select adapters by entity ID. Application-owned classifiers return neutral domain outcomes; missing classification remains unknown.

- [x] Write fake-client tests proving all four entities execute their existing logic, malformed input fails before external calls, tool execution returns real values, and streaming concatenates to the final response from exactly one invocation.
- [x] Extend discovery tests so a fifth previously unknown typed agent and a novel workflow execute through the same generic invoker, including optional-capability fallbacks. Verify domain results with fields called `success` or `isValid` are not interpreted without an explicitly registered classifier. Existing samples are examples, not a hardcoded supported-entity set.
- [x] Write concurrent profile tests with two distinct model clients and validator/repair follow-up calls; assert no crossover and no provider-specific SDK types in contracts. Test fresh workflow executors and per-profile session identity.
- [x] Write session tests for context preservation, reset, rejected overlapping turns and profile mismatch on continuation. Cancellation/failure must not save an apparently successful partial turn.
- [x] Run `dotnet test tests/MafPlayground.Studio.Tests --filter 'FullyQualifiedName~StudioEntityExecutorTests|FullyQualifiedName~StudioProfileIsolationTests|FullyQualifiedName~StudioSessionTests'` and observe the new failures.
- [x] Implement execution-scoped, profile-aware source factories in the runtime/application registration path. Studio invokes the common contracts only. Reuse prompt definitions, validators, guards, session storage and native translation execution; create no sample-specific Studio adapter, child service provider, parallel business pipeline or new orchestration loop. Register translation's partial-outcome classifier with that sample.
- [x] Run targeted tests plus existing Basic agent, RAG/Help and translation tests; require unchanged non-Studio contracts and failure behavior.

## Task 4: Full capture and reliable executor correlation

**Files:** Create Studio `Contracts/StudioSpan.cs`, `StudioPayload.cs`, `Capture/StudioCaptureScope.cs`, `StudioActivityListener.cs`, `StudioPayloadStore.cs`, `StudioPayloadRedactor.cs`, `WorkflowEventMapper.cs`; add narrow transport-neutral observation seams under Runtime `Execution/` only if pinned MAF events cannot provide the required correlation. Modify translation `Executors.cs` only at invocation/message-send boundaries if needed. Add `Unit/StudioCaptureTests.cs`, `Component/StudioCorrelationTests.cs` and `Unit/StudioCapturePrivacyTests.cs` in `MafPlayground.Studio.Tests`.

**Interfaces:** `IStudioEventSink.Record(StudioEvent)` is synchronous and nonblocking; sequencing/storage ownership remains in the run journal. `StudioCaptureScope` carries immutable run context through asynchronous calls. `StudioPayloadStore.Capture(object? value, string contentType) -> StudioPayload`. Observers normalize MAF data into these contracts; runtime seams never reference Studio.

- [x] Write tests capturing model request/response, intermediate tool arguments/results, streaming completion, executor input/output, validation feedback and cost/usage when supplied. Include final spans emitted after output and missing usage.
- [x] Write tests for repeated same-node invocations, parallel language branches and an uncorrelated span. Assert identity-based graph linkage, preserved parent/span IDs and no deduced false edge traversal.
- [x] Write tests proving secrets are absent before storage/MCP/export, Unicode truncation respects 2 MiB, malformed payload serialization does not fail execution, and disabled Studio capture does not enable external sensitive OTLP export.
- [x] Run `dotnet test tests/MafPlayground.Studio.Tests --filter 'FullyQualifiedName~StudioCapture|FullyQualifiedName~StudioCorrelation'`; confirm expected failures.
- [x] Implement native-event/activity collection and narrow observation seams. Collect all relevant sources including Runtime, preserve unavailable reasons, use serializer depth/size bounds and keep event payloads out of metric labels.
- [x] Run targeted tests and existing observability/translation tests; verify model calls are not duplicated and providers are unchanged.

## Task 5: HTTP/SSE and CLI startup

**Files:** Create Studio `Http/StudioEndpoints.cs`, `StudioAccessPolicy.cs`, `StudioContractSchema.cs`, `StudioServiceExtensions.cs`; create CLI `Commands/StudioCommand.cs`; modify CLI project, `ParserTests.cs`, `appsettings.json` and `.env.example` only for nonsecret defaults; add `Contract/StudioHttpTests.cs` and `Unit/StudioAccessPolicyTests.cs` in `MafPlayground.Studio.Tests`. CLI command/parser tests stay in `MafPlayground.Tests/CLI/`; process-startup checks belong to `MafPlayground.Studio.IntegrationTests`.

**Interfaces:** `AddStudio(IServiceCollection, IConfiguration)` registers services in the CLI host; `MapStudio(IEndpointRouteBuilder)` maps `/api/studio/v1`. CLI options: `--url` (default `http://localhost:5051`), `--model` (fallback `AI_MODEL`), `--no-open` and `--export-contracts <path>` for offline contract-schema export without providers/model calls. Discovery can show disabled entities even with no configured model.

- [x] Write parser tests in the existing CLI suite, access-policy unit tests and in-memory HTTP contract tests in the dedicated Studio suite for catalog, sessions, `POST /runs` (202), result/list queries, `POST /runs/{id}/cancel`, payload/span lookup and ordered `GET /runs/{id}/events` SSE. Use fake models, real handlers and TestServer; reserve loopback/process startup for the transport suite.
- [x] Test reconnect cursors/gaps, disconnect without job cancellation, size limits and stable 400/404/409/429 responses. Test non-loopback/credentialed URLs, foreign Host/Origin and missing tokens rejected.
- [x] Run `dotnet test tests/MafPlayground.Studio.Tests --filter 'FullyQualifiedName~StudioHttpTests|FullyQualifiedName~StudioAccessPolicyTests'` and `dotnet test tests/MafPlayground.Tests --filter 'FullyQualifiedName~StudioCommandTests|FullyQualifiedName~ParserTests'`; confirm expected failures.
- [x] Implement one WebApplication in `StudioCommand`, local caller/policy mapping, route adapters and lifecycle shutdown. Bootstrap the browser through a one-time form POST that exchanges a console-displayed code for an HttpOnly same-origin cookie; MCP uses a bearer token. Never put access credentials in URLs or browser local storage. Return ProblemDetails with run correlation and redacted details.
- [x] Export JSON schemas for the Studio transport DTOs as well as entity contracts using existing `AIJsonUtilities`; no new OpenAPI production dependency is needed. Support a contract-only offline command path.
- [x] Run targeted tests plus existing endpoint/CLI tests. Confirm the example backend is untouched and `studio` does not start it.

## Task 6: MCP access to the exact same runs

**Files:** Create Studio `Mcp/StudioMcpTools.cs`, `StudioMcpExtensions.cs`; modify centralized packages and Studio project for `ModelContextProtocol.AspNetCore`; add `tests/MafPlayground.Studio.Tests/Contract/StudioMcpContractTests.cs` and `tests/MafPlayground.Studio.IntegrationTests/StudioTransportTests.cs` and needed references. Pin the compatible published SDK and record it in the Studio README.

**Interfaces:** Map `/mcp`; expose `studio_list_entities`, `studio_get_topology`, `studio_list_profiles`, `studio_start_run`, `studio_list_runs`, `studio_get_run`, `studio_get_events`, `studio_get_spans`, `studio_get_payload`, `studio_cancel_run`, `studio_reset_session`. Task 9 adds `studio_compare_runs` together with its actual implementation. Tool parameters/results reuse the HTTP service contracts, including request IDs and cursors. Session creation is available through start/new-session semantics and reset returns a new explicit session ID.

- [x] Write fast in-memory MCP contract tests with the official SDK client for discovery, typed/malformed arguments, bounded pagination, token rejection and HTTP/MCP parity. Assert identical run IDs/results and one model call under duplicate submission. These belong to `MafPlayground.Studio.Tests`, not browser E2E.
- [x] Include unknown registered entities in HTTP/MCP contract checks. Their dynamic schemas, inputs/results and registered native topology must be available with no new routes or tools. A missing streaming/topology capability must return a clear capability state while generic execution still works.
- [x] Add two opt-in `Category=StudioTransport` checks in `MafPlayground.Studio.IntegrationTests`: a real loopback MCP client reads/submits the same run as HTTP, and an SSE viewer reconnects to the same accepted job. These confirm actual transport behavior without repeating the service's lifecycle/error matrix.
- [x] Run `dotnet test tests/MafPlayground.Studio.Tests --filter FullyQualifiedName~StudioMcpContractTests` and `RUN_STUDIO_INTEGRATION_TESTS=true dotnet test tests/MafPlayground.Studio.IntegrationTests --filter Category=StudioTransport`; confirm failures before implementing adapters.
- [x] Pin and integrate the official SDK, register tools explicitly, bound result sizes, mark read/write semantics and describe captured content as untrusted evidence. Do not expose sampling, shell/file access, generic service clients or assistant-session takeover.
- [x] Repeat in-memory contract and real transport checks; verify package resolution leaves MAF/MEAI versions compatible. Manual assistant configuration is documented, not silently written to user configuration.

## Frontend visual design checkpoint (before Tasks 7–9 screens)

**Deliverable:** A selected visual direction, representative mockups and a compact design-system note alongside the design document. This is a design milestone, not another runtime subsystem or browser test suite.

- [x] Prepare two coherent visual directions using the same realistic workflow/error data: Precision and Fieldwork in the interactive exploration.
- [x] Show catalog/input, workflow investigation, failed invocation/payload detail and comparison. Inspect the prototype in-browser at 1440x900 and 1024x768; real panel resizing remains an implementation requirement.
- [x] Record the user's preferred direction: Precision. The linked visual design note defines the token baseline and implementation defaults for typography, density, navigation, state and motion. Keep visible focus, keyboard alternatives and WCAG AA contrast during implementation.
- [x] Define reusable frontend tokens and primitives for panels, buttons, input/editor containers, status badges, tabs and detail rows. Specify loading, empty, unavailable, error, partial, running and cancelled states as part of the same visual language.
- [x] Use populated mockups to check long IDs, nested JSON, parallel/retry nodes and streaming updates. Do not replace the three agreed functional E2E journeys with visual snapshots or add E2E per screen.

## Task 7: Angular workspace, generated contracts and execution workspace

**Files:** Create `src/MafPlayground.Studio.Web/` Angular workspace (`package.json`, lockfile, `angular.json`, proxy config, contract generation script); feature files under `src/app/catalog/`, `runs/`, `chat/`, `input/`, `core/`; generate `core/studio-api.generated.ts` from Task 5 schemas; add frontend unit tests.

**Interfaces:** `StudioApiService` implements catalog/profiles/session/run/inspection HTTP methods using generated DTOs. `StudioRunStore` owns reactive client snapshots and selection. `StudioEventClient.watch(runId, afterSequence)` handles authenticated streaming/reconnect; it does not own execution cancellation.

- [x] Scaffold standalone Angular with routing/signals, supported pinned toolchain, no SSR, npm lockfile and test/build/type-generation scripts. Use the Angular CLI's generated test runner and add Playwright for E2E. Record exact commands in the frontend README.
- [x] Implement the selected design system in `src/styles/` tokens and reusable `src/app/shared/ui/` primitives before assembling the feature screens. Follow the approved mockups, including responsive panel behavior and all interaction states; do not substitute default library styling for the design.
- [x] Write failing frontend unit/component tests from the frontend inventory: API mapping, event decoder/store projection, entity/profile selection, JSON schema diagnostics, chat streaming, reset and selected-run cancellation. Use controlled services/events and assert observable behavior; these are not browser E2E tests.
- [x] Add `core/dynamic-entity.spec.ts` with an unknown ID and novel nested schema/output supplied by the API fixture. Assert the catalog, JSON editor, validation and generic output inspector work without sample-specific TypeScript DTOs/components. Keep input/output as dynamic JSON plus runtime schemas; only generate transport types.
- [x] Implement catalog/input/chat/run-list views, Monaco JSON schema editor and SSE state application. Use fetch-based SSE for explicit reconnect/gap handling; no token query parameters. After a host-instance change discard stale IDs and show the reset.
- [x] Add npm scripts `generate:contracts`, `check:contracts`, `typecheck`, `test:unit`, `build`, `start` and `test:e2e`. `generate:contracts` uses offline schemas and a pinned schema-to-TypeScript dev dependency; `check:contracts` fails on drift without modifying tracked files.
- [x] Run `npm --prefix src/MafPlayground.Studio.Web run check:contracts`, `run typecheck`, `run test:unit` and `run build`; require successful outputs and no network-loaded editor workers/assets.

## Task 8: Interactive graph, trace timeline and payload inspector

**Files:** Create Angular `graph/workflow-graph.component.ts`, `graph/graph-adapter.ts`, `traces/trace-timeline.component.ts`, `traces/span-inspector.component.ts`, `traces/payload-viewer.component.ts` and associated templates/styles/tests; add browser fixtures under `e2e/fixtures/`.

**Interfaces:** Graph consumes `StudioTopology`, invocation states and selected IDs from `StudioRunStore`; emits node/invocation selection. Trace timeline consumes spans/events and emits span selection. Payload viewer fetches `StudioPayload` on demand. Link selection by IDs through one store, never by rendered labels.

- [x] Write frontend unit/component tests for retry cycles, distinct repeated invocations, parallel branches, nodes not taken, cancelled work and unknown correlation. Assert selection propagation using real projections and a fake graph driver.
- [x] Extend the unknown-entity frontend fixture with changed node/edge IDs and an additional workflow branch; assert generic rendering, selection and capability-unavailable states without entity-ID conditionals.
- [x] Add frontend component tests for lazy payload loading, malicious HTML rendered as text, keyboard node-list navigation, truncation/unavailability and unknown metrics. Keep these permutations out of Playwright. Cover real graph-library pan/zoom/selection wiring inside the single translation investigation E2E journey.
- [x] Implement Cytoscape integration with stable IDs, cycle-capable layout, resize handling and teardown. Update styles/state incrementally so new events do not reset zoom or selection. Present declared workflow topology separately from agent observed call hierarchy.
- [x] Implement a timing-based span timeline with parent hierarchy, validation/tool details, searchable captured content and lazy payload loading; use safe Angular text bindings.
- [x] Run frontend unit/type/build checks. Implement the graph/inspection portion of `translation-investigation.spec.ts` against the real fake-model Studio fixture, and add the `agent-session.spec.ts` and `cancel-execution.spec.ts` journeys. The comparison portion of the first journey lands in Task 9. Do not add separate E2E files per UI feature.
- [x] Review the actual graph/timeline/inspector against the selected mockups at 1440x900 and 1024x768. Check typography, density, resize behavior, focus, contrast, long data and stable selection during streaming; fix visual defects even when functional tests pass.

## Task 9: Structured comparisons and reruns

**Files:** Create Studio `Comparison/StudioRunComparer.cs`, `Contracts/StudioComparison.cs`; extend HTTP/MCP comparison adapters; add Angular `comparison/run-comparison.component.ts` and tests; create `tests/MafPlayground.Studio.Tests/Unit/StudioRunComparerTests.cs`.

**Interfaces:** `StudioRunComparer.Compare(StudioRunSnapshot baseline, StudioRunSnapshot candidate) -> StudioComparison`; HTTP `POST /comparisons` accepts two run IDs. Rerun submits a new `StudioRunRequest` with a new request ID and explicit profile/input changes; fresh agent session unless continuation is selected.

- [x] Write failing tests for reordered JSON properties, changed arrays/text, repeated invocations, changed profiles and missing cost/token data. Do not equate null with zero or attribute semantic correctness to textual similarity.
- [x] Add backend unit tests rejecting unavailable/evicted/cross-host baselines and showing capture incompleteness, plus frontend component tests for the comparison view. Extend the existing translation investigation E2E journey with its rerun/comparison steps; verify MCP parity in the in-memory contract suite rather than another browser test.
- [x] Implement deterministic structural/text differences and duration/usage deltas; matching repeated invocations must disclose the matching rule and unmatched entries rather than invent causal equivalence.
- [x] Run `dotnet test tests/MafPlayground.Studio.Tests --filter 'FullyQualifiedName~StudioRunComparerTests|FullyQualifiedName~StudioMcpContractTests'`, frontend comparison unit/component checks and the completed translation investigation journey.

## Task 10: Packaging, operational docs and complete verification

**Files:** Add `src/MafPlayground.Studio/README.md`, frontend README and reproducible asset-copy/build scripts; modify CLI project packaging, root README, CLI README, canonical repository-help manual/reference, test READMEs and `scripts/verify.sh`; preserve the independent ASP.NET Core sample.

**Interfaces:** A documented frontend build produces local static assets consumed by the CLI `studio` host. Startup must show an actionable message if assets are absent, with a `--no-open` API/MCP-only path still usable. Browser bootstrap and MCP token setup are explicit local actions; do not auto-edit Codex/Claude configuration.

- [x] In the dedicated local transport project, verify packaged CLI hosting a built Angular route/deep link and static workers without Node at runtime. Verify missing assets/configuration through focused startup/contract tests; do not add a browser matrix for these cases or require actual PostgreSQL failure to test unavailable-capability projection.
- [x] Document startup, frontend development proxy, profiles, payload limits, token setup, ephemeral history and limits of correlation/replay. Include examples of assistant requests to investigate a branch and launch a comparison case.
- [x] Regenerate CLI reference using the repository's documented command; describe explicit repository-help re-ingestion without running it or migrating retrieval storage automatically.
- [x] Add separately opted-in `Category=StudioLiveModel` smoke checks in `MafPlayground.Studio.IntegrationTests/LiveModel/` for representative agent/tool and translation paths. Do not introduce Studio semantic-evaluation datasets or LLM-as-judge checks in this delivery.
- [ ] Environment-dependent verification: compare one exported trace in Aspire with Studio, preserving external payload privacy defaults. Unverified: no configured model or collector, and Docker daemon is unreachable. Both live smoke checks explicitly skip without configuration.
- [x] Extend `./scripts/verify.sh` with the dedicated fast Studio backend suite and document the separate frontend checks. Run the fast backend and frontend gates; then explicitly run the three browser journeys and opted-in fake-model transport suite for final validation. Real-model smoke checks remain separate and may be reported unverified when no provider is configured. Record exact pass/fail evidence by suite.
- [x] Perform a final visual/usability review of the real Angular application with populated, empty, failed and running cases. Capture review evidence where useful and resolve deviations from the selected design; do not create a screenshot-regression matrix to replace this review.
- [x] Perform mandatory whole-diff/untracked-file review, secret scanning, `git diff --check`, provider-boundary review and cleanup of unused registrations/generated artifacts. Confirm CLI/DevUI regression checks, preserved example-backend behavior, documented unsupported capabilities and browser verification before claiming completion.

## Execution checkpoints and handoff

Every task ends with its stated checks and a reviewable checkpoint. Commit only task-owned files when committing is authorized and available; do not include the pre-existing documentation-wiki files. Use the selected execution skill and keep all first-delivery requirements through intermediate slices.

Plan self-review: tasks cover catalog/topology (1), lifecycle/retention (2), four entities/profiles/sessions/streaming (3), payloads/correlation/privacy (4), HTTP/CLI (5), MCP (6), Angular/input/chat (7), interactive graph/traces (8), comparisons (9), packaging/docs/end-to-end verification (10). The five review-focus risks are assigned to their owning tests. Implementation tasks 1–10 are complete and independently reviewed. Final whole-branch review is pending. Actual live-provider and external Aspire/OTLP delivery remain unverified as noted above.


Implementation notes: native MAF 1.16 topology lacks public edge labels, which remain explicitly unavailable; comparison consumes separately inspected bounded journal artifacts. The example composition selects its original Basic/Translation entities explicitly to preserve behavior after shared registration expanded; it has no Studio dependency. The browser-only fixture remains outside production publishing. Final commands, evidence and limitations are documented in the Studio and test READMEs.

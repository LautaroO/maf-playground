# MafPlayground.Studio

Development-only inspection services over the application's normal AI runtime
registrations. This library references Abstractions and Runtime, with no Samples,
CLI or concrete-provider dependency. Register `AddStudioCatalog()` in the same
service collection as `AddAIRuntime(...)`, then resolve `IStudioCatalog` in a scope.

The catalog reads `IAIEntityRegistry.Entities` and optional Runtime inspection
metadata. Input/output JSON schemas and examples use `AIJsonSerializerOptions.Default`,
matching the generic typed invoker. Missing examples or native capabilities never
exclude an entity. Configuration-only availability checks can disable a particular
entity with a reason, without relaxing the application's strict startup validation.
Catalog enumeration does not construct entities, agents, or native sources.

Native workflow topology comes from the normally registered `IMafWorkflowSource`.
The opaque profile ID is forwarded to its factory with the current service scope;
the host owns profile binding. Source construction must be safe for inspection:
no model/tool execution or eager client construction. Agents and workflows lacking
a native source return an explicit topology-unavailable state.

MAF 1.16 public `ReflectExecutors`, `ReflectEdges`, and `StartExecutorId` expose
structural graph metadata, including fan-out, fan-in and cycles. `EdgeInfo` omits
labels and runtime edge identities. Studio derives stable structural edge IDs,
coalesces indistinguishable structural connections, and reports labels as
unavailable. The native Mermaid export retains available labels as an opaque
export; Studio does not parse it or reflect private framework fields.


Run supervision is deterministic infrastructure. Register `AddStudioCatalog()`
and `AddStudioRuns()` in the same host. `AddStudioRuns` installs the generic scoped
`StudioEntityExecutor` unless the host already registered its own executor.
It delegates to `IAIEntityInvoker` and normal application registration capabilities;
Studio contains no sample adapters or switches on entity IDs. The hosted singleton implements `IStudioRunService` and
`IStudioEventSink`. Accepted jobs copy caller/input state and use fresh async DI
scopes. Submission cancellation only governs admission. Explicit cancellation,
timeout and shutdown converge on a job-owned cancellation source; cancellation
requested remains separate from terminal status until execution and scope cleanup
finish. Cancellation admission closes atomically when the executor returns; all
accepted callback work finishes before scoped resources begin disposal.
Cancellation requests during that final cleanup return false while the session
lease remains held until terminal publication. A successful transport leaves
domain outcome `Unclassified` until an application classifier supplies stronger
evidence.

`StudioOptions` defaults to four workers and a queue of 16 waiting jobs. This small
burst buffer prevents unbounded pending input/scope accumulation while keeping
interactive submissions responsive. Limits also include 100 retained completed
runs, 128 MiB of serialized UTF-8 capture, 2 MiB per JSON payload, 2,048 events per
run, 10,000 request records and a conservative 8 MiB metadata reservation. The
metadata reservation includes identity strings and fixed per-run/tombstone
allowances; it is separate from the serialized capture budget and is not a CLR
heap measurement. Runs default to a five-minute timeout and ten-second graceful
shutdown. Shutdown waits within its grace period; an uncooperative executor keeps
its worker, scope and session lease until it actually exits.

Host-lifetime SHA-256 request fingerprints preserve retry identity after result
and journal eviction. Changed entity/profile/session/input/caller conflicts;
metadata exhaustion rejects new submissions rather than forgetting old IDs.
Completed runs are evicted oldest first. Active input/control state is never
evicted: capture pressure drops journal data or replaces oversized result/event
JSON with an explicit truncation marker. Input exceeding its payload limit is
rejected before acceptance. Event metadata is bounded too. Capture byte totals
include submitted input, effective profile metadata, topology, result and retained event envelopes.

Event sequences are assigned by the host. Bounded pages expose earliest retained
sequence, next cursor and explicit gaps. Watchers read one retained event at a
time and emit `journal_gap` when their cursor falls behind, without allocating a
private subscriber queue or blocking execution. They remain open for late spans
and end when cancelled or the run is evicted. `CaptureComplete` indicates whether
capture has lost/truncated data so far; it does not promise that correlated spans
have all arrived. Missing run snapshots return null, while event-page retrieval
for unknown/evicted runs throws `KeyNotFoundException`. Transport adapters should
map these outcomes without resubmitting work.


Register immutable `StudioExecutionProfile` instances with DI to select chat models,
guard profiles or model-call timeouts. `StudioOptions.DefaultProfileId` names the
host's unchanged configured defaults (the CLI uses `cli-default`). Profile IDs are
opaque to Runtime; they never select sample implementations. Unknown profiles fail
explicitly. Entities lacking profile factories still execute with default settings
and expose unavailable profile/streaming capabilities; explicit configuration
overrides require a registered profile factory.

Before topology construction the supervisor binds the requested profile and saves
`StudioExecutionProfileSnapshot` with effective model selector, guard and timeout.
The Runtime scope caches those same options for execution. Null fields mean
unavailable/not applicable; credentials and provider configuration are excluded.
Failed runs retain the bound snapshot within the same capture limits. The snapshot
is independent of later configuration changes.

The typed executor returns `StudioEntityExecutionResult(Output, Classification)`.
Application classifiers alone determine domain status, independently of event
capture or payload truncation. Neutral `text_delta`, `workflow_event` and
`capability_unavailable` events are emitted from the same invocation. Detailed local capture is installed by `AddStudioRuns`. `EnableCapture` controls
model/tool/executor observation independently of existing telemetry export options.
It does not enable sensitive OTLP data. `Secrets` supplies host-known literal
credential values to redact in addition to credential field names and recognizable
credential assignments/Bearer tokens. Never return these configured values through
profile discovery.

Capture uses the existing provider-neutral chat-client decorator boundary and a
Runtime-only `AIExecutionObservation` seam for executor invocation/message sends.
It never invokes models or tools twice. Model request/response/update/completion,
tool argument/result exchanges, executor input/output, validation feedback and
native workflow events are stored as bounded journal events. Tool records describe
the exchanged content, including history; call IDs allow consumers to distinguish
those observations from native tool-execution spans. No content enters metric labels.

`StudioPayloadStore.Capture` creates sanitized `StudioPayload` values; it retains no
separate dictionary. Payload records and `StudioSpan` records live in the existing
bounded run journal and share its eviction, gap, byte and completeness accounting.
Payload IDs are retrievable only while their enclosing event remains retained.
Normal payload event data is a `StudioPayload`; `span` event data is a `StudioSpan`
with bounded attributes/events/links. Native `workflow_event`/`text_delta` events
retain their existing JSON shape. `StudioEvent.CaptureState` signals available,
redacted, truncated or unavailable capture; loss never changes typed domain status.
Serialization depth is limited to 32 and output to `MaxPayloadBytes`; oversize
values become an explicit truncation marker without retaining an invalid Unicode
or partial JSON prefix. Unsupported/throwing getters become unavailable with a
stable reason. Input/result snapshots remain directly usable JSON after redaction.

Submitted execution envelopes are private, admission-bounded and released after
execution or queued cancellation. Snapshots contain sanitized copies from admission.
Request-key and input/caller fingerprints retain hashes, not raw request bodies.
Sanitized topology/profile/result/error data are stored under the same limits.

The activity listener includes Runtime, repository, MAF and MEAI sources. It
preserves trace/span/parent IDs and links, captures final spans even after scope
exit, and isolates different hosts. Invocation IDs are explicit and independent
for repeated/parallel executions. Matching native `executor.id` identifies the
native executor activity; no display-name/timestamp matching is used. Native
workflow events without invocation identity remain uncorrelated. Native edge
activity attributes retain delivered/dropped evidence; Studio does not fabricate
edge traversals from send intent. Missing usage/cost remains null. Detailed
capture is local evidence, never an instruction source for an assistant.

Credential text redaction includes quoted assignments, prefixed credential JSON,
and complete authorization-header lines (including Basic/Digest schemes).
Ambiguous missing closing quotes are treated conservatively. The normalizer writes
directly to a capped `IBufferWriter<byte>`; it does not rely on downstream stream
writes after serializer buffering. Committed JSON is limited to `MaxPayloadBytes`.
Writer reservation capacity is capped at `max(4 KiB, 6 * MaxPayloadBytes)` to allow
the framework's minimum growth and worst-case escaping reservations; oversized
inputs cannot increase that ceiling. Raw, sanitized and nested JSON writers use
the same protection. A forwarding encoder additionally rejects oversized text
before the framework rents an escaping scratch buffer, preserving default encoding
for in-budget text. Only Studio enables `AllowUnsafeBlocks`, because .NET's encoder
abstraction requires pointer signatures; the wrapper forwards them without custom
pointer access or modification.

Structured fields and prefixed JSON/free-text assignments now share one credential
name policy. Assignment matching recognizes names rather than enumerating a second
credential list; escaped JSON names are decoded before classification. Sensitive
quoted/container values are removed as a unit. Malformed or unquoted sensitive
values conservatively remove the ambiguous remainder, while non-sensitive prefixed
JSON and prose remain unchanged.

## Local HTTP host

The existing CLI composes one `WebApplication` with `AddStudio(configuration)`
then installs `UseStudioAccess()` before `MapStudio()` and any static-file or MCP
middleware. Studio adds an ASP.NET Core shared-framework reference; its
only project references remain Abstractions and Runtime. No provider SDK, sample
registry, additional executable, or production OpenAPI package is introduced.

The CLI defaults to `http://localhost:5051`. It prints a single-use browser code
and a separate API bearer token on the local console. Open `/studio/login` and
submit the code as a form POST to obtain an HttpOnly, SameSite=Strict cookie.
URLs, query strings, browser storage and JSON responses never carry access
credentials. Host, Origin, scheme and remote-loopback checks apply to every
Studio endpoint. This is one trusted local caller (`studio-local`), not a
multi-user server. Restart the host to replace credentials and clear history.

Routes under `/api/studio/v1`:

| Method and route | Result |
| --- | --- |
| `GET /instance` | Stable host lifetime ID, including an empty host; changes on restart |
| `GET /entities?profileId=cli-default` | Dynamic catalog and typed schemas with selected-profile availability (profile is optional) |
| `GET /topology?entityId=review&profileId=cli-default` | Native topology or explicit unavailable state |
| `GET /profiles` | Allowed host-configured profile IDs |
| `GET /sessions` / `POST /sessions` | List/create bounded session handles (`entityId`, `profileId`) |
| `POST /sessions/{id}/reset` | 200 new explicit `StudioSession`; clear native history; queued and active turns conflict |
| `GET /runs` / `POST /runs` | Retained run snapshots / accepted receipt (202) |
| `GET /runs/{id}` / `GET /runs/{id}/result` | Snapshot / terminal snapshot; pending result returns conflict |
| `POST /runs/{id}/cancel` | Request job cancellation (202); terminal/finalizing run returns conflict |
| `GET /runs/{id}/event-page?after=0&limit=100` | Bounded retained journal page (limit 1–1000) |
| `GET /runs/{id}/events` | Ordered SSE; `after` or `Last-Event-ID` resumes after that sequence |
| `GET /runs/{id}/payloads/{payloadId}` | Sanitized retained payload, including span attributes/events/links |
| `GET /runs/{id}/spans/{spanId}` | Sanitized retained span |

SSE frames use `id` = sequence, `event` = capture/lifecycle kind and `data` = the
web-serialized `StudioEvent`. A `journal_gap` event identifies missed retention;
fetch a snapshot when this occurs. There is no subscriber queue: events are read
incrementally and flushed with backpressure. Disconnect cancels only the watcher.
The stream stays open for late spans until disconnect, run eviction or host stop.
Embedded `JsonElement` capture DTOs retain their existing PascalCase property names;
outer transport DTOs use web camelCase. Enums use the existing numeric JSON format.

Errors return fixed-category ProblemDetails with correlation ID and a safe run ID
when present: 400 invalid request/limit/profile, 401 missing credential or invalid
bootstrap code, 403 nonlocal Host/Origin/connection, 404 unknown/evicted capture,
409 incompatible state/unavailable entity, 429 bounded capacity, 500 unexpected
failure. Raw exception messages and submitted input are never included in problems.
Payload/span lookup never re-executes or reconstructs evicted data.

`Studio:Profiles` is an array of `{ Id, Model?, GuardProfile?, ModelCallTimeout? }`.
Model values use `provider:model`; credentials, URLs, query strings and whitespace
are rejected. Profile IDs are allowlisted and immutable during a session. The
`cli-default` profile inherits configured entity/default settings. Limits bind
from `Studio:Limits`. Configured credential values and host access credentials
are added to local capture redaction before any run executes.

`StudioSessionService` and `StudioInspectionService` live in `Execution` and are
shared transport adapters. Run submission must use inspection preflight followed
by `StudioSessionService.StartAsync`; session creation similarly validates the
entity/profile/session capability before `CreateAsync`. All transports sharing
sessions must use this adapter's admission/reset gate. The run service continues
to own execution, cancellation, idempotency and retention. Session handles are
limited to 1,000 per host lifetime; reset clears native history but keeps the
handle's entity/profile identity. `IStudioRunService.ListAsync` reads only retained
snapshots under the existing store lock.

`studio --export-contracts <path>` writes transport and entity JSON schemas using
`AIJsonUtilities` without building/starting a host or registering providers. The
`/studio` UI serves compiled local assets when installed, otherwise actionable
build instructions; MCP is mapped with the HTTP API. The [Angular workspace](../MafPlayground.Studio.Web/README.md) provides the development UI via a strict loopback proxy. Transport export also includes backend-derived numeric enum schemas for frontend generation.

Access and error protection is middleware, not an endpoint filter.
`UseStudioAccess()` wraps all downstream routes before minimal API argument
binding, service activation and result execution. Install it before static-file
handlers or any additional transport middleware in the dedicated Studio host.
The bootstrap form is the only credential-free route and still requires local
Host/Origin/connection checks. Required-query binding failures use
`RouteHandlerOptions.ThrowOnBadRequest` and become sanitized 400 ProblemDetails;
authentication denial takes precedence. Unexpected activation/execution failures
become sanitized 500 responses. If a response has already started, the connection
is aborted rather than forwarding private diagnostics to a development error page.
Development hosting and DI/options startup validation remain enabled. Do not add
a developer exception page inside this protected pipeline.

## MCP access to shared runs

`AddStudio` registers the official `ModelContextProtocol.AspNetCore` **1.4.1**
SDK; `MapStudio` maps `/mcp` in the same CLI process and service provider as HTTP
and SSE. This production dependency supplies protocol handshake, tool argument
binding, discovery and Streamable HTTP framing. It is not an execution engine.
Its transitive MCP/Core packages remain 1.4.1; the net10 package minima (MEAI
Abstractions 10.5.2 and Extensions 10.0.7) fit this repository's MEAI 10.7.0,
Extensions 10.0.10 and MAF 1.16.0. SDK 2.0/2.3 require newer MEAI; upgrading the
framework to consume those packages is not part of this transport. Studio resolves
MEAI 10.7.0. The CLI already resolves MEAI 10.8.0 through the unchanged
`OllamaSharp` 5.4.30 adapter dependency; MCP does not raise that version. Central
MAF/MEAI/Extensions declarations and provider adapter references remain unchanged.

Transport configuration is stateless Streamable HTTP, with no MCP conversation
state or legacy SSE transport. Sampling, elicitation and roots are disabled by
SDK stateless mode. Accepted Studio jobs and explicit agent sessions still belong
to the shared run/session services. Each profile-sensitive tool operation creates
a fresh async service scope. Disconnecting an MCP client never cancels an accepted
job; use `studio_cancel_run` explicitly. No shell, file,
ambient service client or assistant-session takeover tools are registered.

Install `UseStudioAccess` before endpoint binding. `/mcp` requires the ephemeral
local **bearer** token on every request; a valid browser cookie alone is rejected.
The same loopback remote-address, exact Host/Origin and scheme rules apply.
MCP POST bodies are bounded before SDK parsing to `MaxPayloadBytes + 4096` bytes,
including chunked requests without a content length. Accepted run input remains
subject to the independent `MaxPayloadBytes` limit. Requests and accepted work
use the same trusted `studio-local` caller as HTTP.

| Tool | Arguments and result |
| --- | --- |
| `studio_list_entities` | `after=0`, `limit=20`, optional `profileId`: `{items: StudioEntityDescriptor[], nextCursor}` with dynamic schemas/examples/capabilities |
| `studio_get_topology` | `entityId`, `profileId`: `StudioTopology`, including explicit unavailable state |
| `studio_list_profiles` | `after=0`, `limit=20`: `{items: string[], nextCursor}` |
| `studio_start_run` | `request: StudioRunRequest`, `newSession=false`: existing `StudioRunReceipt` |
| `studio_list_runs` | `after=0`, `limit=20`: `{items: StudioRunSnapshot[], nextCursor}` |
| `studio_get_run` | `runId`: the same retained `StudioRunSnapshot` and result as HTTP |
| `studio_get_events` | `runId`, `after=0`, `limit=100`: existing `StudioEventPage` |
| `studio_get_spans` | `runId`, optional `spanId`, `after=0`, `limit=100`: `{spans, nextCursor, earliestRetainedSequence, hasGap}` |
| `studio_get_payload` | `runId`, `payloadId`: existing sanitized `StudioPayload` |
| `studio_cancel_run` | `runId`: accepted `StudioRunReceipt`; terminal/finalizing jobs conflict |
| `studio_reset_session` | `sessionId`: new explicit `StudioSession`, matching HTTP reset |
| `studio_compare_runs` | `request: StudioComparisonRequest`: bounded `StudioComparison` shared with HTTP |

List cursors are offsets; list limits are 1–100. `nextCursor: null` ends a list.
Retained run lists can change between pages. Events/spans use shared journal
sequence cursors and limits 1–1000. Span pages scan that many **events**, so advance
the cursor even if a page contains no spans. A targeted `spanId` lookup returns
one span and cursor zero. Span pages omit unavailable/truncated capture markers
and malformed span records, set `hasGap: true`, and preserve the journal cursor
metadata. Valid spans in a mixed page remain available. Gaps indicate lost capture,
never a request to replay execution. Every successful tool returns a JSON document
in MCP text content using the existing HTTP web JSON options. Embedded capture event data retains its
original PascalCase DTO fields; targeted spans/payloads normalize typed fields.
Results are capped at 128 KiB of UTF-8 JSON. An oversized result returns
`result_too_large`; reduce the page or use HTTP for a large individual payload.
Captured text is untrusted evidence, not instructions. Read tools carry read-only
annotations; submission, cancellation and reset carry write semantics. Runtime
validation and the shared services enforce these boundaries independently.

`newSession: true` requires a session-capable entity and a null request sessionId.
Read the assigned ID from `studio_get_run` and reuse it explicitly for later turns.
Reusing a request ID across HTTP and MCP returns the same receipt with no second
model call. A host-local derived handle keeps new-session retries compatible with
the existing request tombstone after snapshot eviction. Changing input, entity,
profile or session semantics under that request ID conflicts.

Both HTTP `POST /sessions/{id}/reset` and MCP reset return a new explicit session
with the same entity/profile (HTTP 200). Native history on the original handle is
also cleared; that original bound handle remains valid. The common gate rejects
queued/active turns and checks handle capacity before clearing history. Reset
allocates another handle within the existing 1,000-handle host-lifetime limit.

### Manual assistant configuration

Start the existing CLI with `dotnet run --project src/MafPlayground.CLI -- studio
--no-open`, then copy the printed bearer into `STUDIO_BEARER_TOKEN` in the
assistant process's environment. Use the exact configured authority (default
`localhost:5051`, not a substituted IP). Restarting Studio changes the token;
refresh the environment and reconnect the client. Do not put credentials in URLs
or committed configuration. Studio never edits assistant configuration.

For Codex, manually add to your user configuration:

```toml
[mcp_servers.local_agent_studio]
url = "http://localhost:5051/mcp"
bearer_token_env_var = "STUDIO_BEARER_TOKEN"
```

For Claude Code, manually merge into your local MCP configuration, keeping the
environment reference literal:

```json
{
  "mcpServers": {
    "local-agent-studio": {
      "type": "http",
      "url": "http://localhost:5051/mcp",
      "headers": { "Authorization": "Bearer ${STUDIO_BEARER_TOKEN}" }
    }
  }
}
```

Use the client's MCP status/tools view to confirm discovery. These are documented
manual configurations, not claims that a particular assistant was connected.
The official SDK client is exercised by both in-memory contracts and opt-in
loopback transport tests. Configuration references: [Codex MCP](https://learn.chatgpt.com/docs/extend/mcp?surface=cli),
[Claude Code MCP](https://code.claude.com/docs/en/mcp), and
[official C# SDK v1.4.1](https://github.com/modelcontextprotocol/csharp-sdk/tree/v1.4.1).

Profile-aware discovery uses the same request-scoped profile binder as execution
preflight; omitting `profileId` preserves default availability. HTTP and MCP
`studio_list_entities` share this metadata path, including unknown-profile rejection.
Topology IDs travel in query parameters to avoid encoded-slash path ambiguity;
no manual URI decoding or old-route compatibility shim is applied. The runtime
builder still limits registered IDs to letters, digits, hyphens, underscores and
periods. Opaque slash/percent transport fixtures test query binding only; they do
not broaden the runtime registration contract.

## Deterministic retained-run comparison

`POST /api/studio/v1/comparisons` and MCP `studio_compare_runs` accept the same
`StudioComparisonRequest`: `hostInstanceId`, `baselineRunId`, `candidateRunId`.
The host ID comes from `/instance` or a retained snapshot. Both adapters use
`StudioComparisonService`, existing access/caller policy and bounded request
handling. Missing/evicted runs return `not_found_or_evicted`, a mismatched host
returns `invalid_request`, and nonterminal runs return `state_conflict`.
Comparison never submits work, invokes a model or recovers evicted capture.

The comparer reads the retained journal separately from snapshots, freezes each
read at its snapshot sequence and rechecks retention before returning. A later
comparison can include late spans. JSON object order is ignored; arrays retain
order. Differences cover input/result, requested profile and captured effective
profile. Typed status/outcome and nullable duration/usage/cost are returned
explicitly. Removed/added structure differs from unavailable result capture.
Text uses a linear first-changed-scalar window. `textOffset` is a UTF-16 code-unit
offset to the start of that Unicode scalar. Preview start/end never split a surrogate
pair; the window contains at most 160 UTF-16 units plus an ellipsis when clipped.
This does not promise grapheme-cluster boundaries or use an edit-distance matrix;
empty text windows remain empty strings.

Limits are 48 changes, 64 invocation rows, 8192 visited JSON nodes, 2048 properties
per object, depth 32, 2048 journal events per run, 160-character path/value previews
and a 96 KiB serialized response. Truncation and incomplete/gapped evidence are
explicit. `hasRedactions` is independent of `captureComplete` and `journalHasGap`;
redaction alone does not claim lost history. Duplicate JSON properties are
ambiguous and mark the diff truncated.
Property previews may be shortened; original retained values remain inspectable.

Invocations group by recorded invocation ID and match exact executor ID plus
occurrence in journal sequence order. Missing/conflicting executor correlation
stays unmatched; original IDs are preserved. This is not causal equivalence.
Metrics use unique `(traceId, spanId)` observations only from
`MafPlayground.Studio.Model`, excluding native/agent duplicates. These are observed
attempts, including guard-rejected attempts, not billed-call counts. Token and
cost coverage counts are exposed; missing, partial, redacted or gapped totals
remain unknown.
Cost deltas require known values in the same reported currency. Differences do
not judge semantic correctness or quality. No persistence or migration is added.

Capture state is based on actual credential-policy mutations, not raw JSON byte
changes caused by timestamp or string escaping. Benign JSON embedded in activity
strings keeps its original text formatting; embedded credentials still pass through
the same bounded redactor. The existing payload/depth/allocation limits remain.
`hasRedactions` reports retained event flags; snapshots themselves remain sanitized
JSON and do not expose original private execution input or infer secret markers.

## Build, publish and start

From the repository root, with the pinned .NET SDK and Node 24/npm 11 available:

```sh
./scripts/build-studio.sh
dotnet run --project src/MafPlayground.CLI -- studio --no-open
# Or publish once and run on a machine with the matching .NET runtime:
dotnet publish src/MafPlayground.CLI -c Release -o artifacts/studio -m:1 -p:UseSharedCompilation=false -nr:false
dotnet artifacts/studio/MafPlayground.CLI.dll studio --no-open
```

`build-studio.sh` runs `npm ci` and the pinned production build. The CLI project
copies `dist/studio/browser` into `studio/` on build and publish, including local
workers/fonts and DM Sans, IBM Plex Mono, Monaco/codicon notices. Publish to a
fresh output directory when distributing a new build. Neither Node nor the
example backend is needed at runtime. Missing `studio/index.html` produces build
instructions on the console and authenticated `/studio` page; API/MCP still work
and browser auto-launch is suppressed. `--no-open` always leaves launch to you.

Enter the console's single-use access code at the printed `/studio/login` URL.
Assets and deep links pass through `UseStudioAccess` before static middleware or
endpoint binding. Missing file requests return 404 rather than HTML. Production
CSP allows only same-origin scripts, module workers, connections and base URI;
inline styles are needed by Angular/Monaco. It does not allow `unsafe-eval`, HMR
WebSockets, embedding or plugins. Login credentials never enter a URL or localStorage.

No configured model is a supported inspection state, with explicit unavailable
reasons. Invalid URL/model/profile configuration fails with a sanitized startup
message. Set `Studio:EnableRetrieval=true` only with the existing retrieval/store
configuration; this opt-in does not provision a database or migrate/index data.
All four normal entities remain discoverable and availability follows the selected
profile. Profiles are immutable configuration, not arbitrary model selectors in
browser requests. For example, set `Studio__Profiles__0__Id=reviewed` and
`Studio__Profiles__0__Model=provider:model` in the host environment.

### Investigating with an assistant

After manually connecting the MCP client described above, examples of requests are:

> Inspect the retained translation run I selected. Follow the French branch's
> recorded executor and invocation IDs through validation and retry. Load only
> relevant span payloads, explain the blocking issue, and flag missing evidence.

> Discover available profiles and the translation input schema. Start a new case
> translating “Your order 247 is ready.” to Spanish and French under cli-default,
> then the reviewed profile. Wait for both terminal runs and compare their typed
> outcomes, structural output differences and known metrics. Do not infer quality
> or cost from missing telemetry.

Submission performs real configured model/tool calls. The assistant stays in its
normal session and uses narrow Studio tools; Studio does not take over that session.
For Codex the equivalent manual CLI registration is
`codex mcp add local-agent-studio --url http://localhost:5051/mcp --bearer-token-env-var STUDIO_BEARER_TOKEN`.
The token must already be set in the assistant process environment. Never reuse a
cloud/provider credential for it. No command here is executed automatically.

### Capture, export and lifecycle limits

Local capture includes sanitized input/output and model/tool evidence by default.
This is separate from `Observability:AgentFramework:EnableSensitiveData`, whose
external OTLP default remains false. Configure an external collector explicitly
with the existing observability settings; Studio is not an OTLP receiver or a
substitute for Aspire/DevUI. To validate export, compare a recorded trace ID in
Studio with that exact trace in your local Aspire instance, then confirm external
prompt/tool contents remain absent while local capture is visible. A running
exporter or a fake capture test alone does not prove collector delivery.

IDs, correlations, invocation matching, usage coverage and capture gaps reflect
retained observations. Missing parents, unavailable metrics and truncated/redacted
payloads are explicit; edge traversal, causal equivalence and billed calls are not
inferred. Run again starts fresh work from reviewed sanitized input, with a new
request ID; redacted or evicted originals cannot be recovered. Browser disconnect
ends observation only; explicitly cancel execution when wanted. Ctrl+C stops the
host and cancels accepted work within its configured shutdown grace.

Configured HTTP/MCP input limits and Kestrel's envelope limit use the same
`Studio:Limits:MaxPayloadBytes`; the default stays 2 MiB. Values below 64 bytes or
above `Int32.MaxValue - 4097` are rejected to keep envelope/probe arithmetic safe.
The browser editor independently caps input/schema text at 2 MiB; increasing the
server limit does not expand the browser editor's limit.

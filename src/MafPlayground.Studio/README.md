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
and `MapStudio()`. Studio adds an ASP.NET Core shared-framework reference; its
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
| `GET /entities` | Dynamic catalog and typed schemas, including unavailable entities |
| `GET /entities/{id}/topology?profileId=cli-default` | Native topology or explicit unavailable state |
| `GET /profiles` | Allowed host-configured profile IDs |
| `GET /sessions` / `POST /sessions` | List/create bounded session handles (`entityId`, `profileId`) |
| `POST /sessions/{id}/reset` | Reset native history; queued and active turns return conflict |
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
current `/studio` page explicitly reports absent frontend assets; asset packaging
and MCP mapping are separate delivery steps.

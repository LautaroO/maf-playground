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
`capability_unavailable` events are emitted from the same invocation. Detailed
model/tool spans and payload capture are a separate integration.

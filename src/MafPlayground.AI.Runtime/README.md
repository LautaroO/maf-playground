# MafPlayground.AI.Runtime

Provider-neutral runtime infrastructure for registered AI entities. It owns
entity-aware `provider:model` resolution, ordered `IChatClient` decorators,
standard MAF agent construction, typed in-process workflow execution, guards,
telemetry, registry dispatch, and the persistence port for serialized MAF agent
sessions.

Runtime settings bind from `AI:Defaults` and `AI:Entities:{id}`. Entity settings
override model, guard profile, and model-call timeout. Provider SDK types remain
inside provider adapter projects.

The older `AI:Resilience:ModelCallTimeout` setting is retained only as the
fallback used by existing CLI/RAG/Help composition. New hosts should configure
`AI:Defaults:ModelCallTimeout` and entity overrides.

`AddInMemoryAgentSessions` is an explicit development/test option. It has bounded
capacity and retention, enforces caller ownership and optimistic concurrency,
and loses all data on process restart. A production host should implement
`IAgentSessionStore` with encryption, retention, migrations, and durable atomic
compare-and-swap semantics appropriate to its persistence technology.

Normal `AIBuilder` registrations can include a typed input example,
configuration-only availability check, and optional `IMafAgentSource` or
`IMafWorkflowSource` factory. `IAIEntityInspection` is implemented by the same
scoped registry as `IAIEntityRegistry`; it does not maintain a second entity list.
Metadata inspection never resolves those factories. `AIBuilder.Registrations`
exposes the same duplicate-validated registration metadata to host composition
before DI is built, without constructing another service provider.

Native-source factories receive `(IServiceProvider, AIEntityInspectionContext)`;
`AIEntityInspectionContext.ProfileId` is opaque to Runtime. The supplied provider
is the current scope, and the host owns immutable profile binding and source
lifetimes. The parameterless generic `WithMafAgentSource<TSource>()` and
`WithMafWorkflowSource<TSource>()` bind an existing DI registration. Factories used
for topology inspection must construct metadata without invoking a model/tool or
eagerly creating a model client. Availability callbacks inspect configuration;
they do not replace hosted startup validation or execution validation.

`WithProfileFactory<TEntity>` registers application-owned construction for a bound
execution profile. A host binds `AIEntityExecutionScope` once before native
inspection or typed invocation. The scope caches immutable effective options and
one entity instance; its profile factory supplies both the typed entity and its
optional native-source interfaces. The factory owns new clients through
`AIEntityConstructionContext.Own`/`RegisterResource`, which release them when the
scope ends. Default unbound invocation retains existing DI lifetimes. No second
service provider or mutation of global options is involved.

`AIExecutionContext.Observer` carries optional neutral execution events through
the same typed invocation. `WithStreamingEvents` advertises that capability;
streaming does not bypass entity input validation, guards, or session storage.
`WithOutcomeClassifier<TOutput>` is application-owned and returns a neutral
`AIOutcomeClassification`; otherwise the domain outcome remains unclassified.
The framework never infers domain status from arbitrary output property names.

Session keys optionally bind a profile ID. `IAgentSessionStore.Acquire` rejects
overlapping turns, `ResetAsync` clears an idle caller-owned conversation, and
successful execution alone commits the serialized MAF session. Failed or cancelled
streaming turns cannot replace committed history. Profile changes require a new
session or an explicit reset using the existing owner/profile key.

Native agent updates pass through the existing guard pipeline. Profiles with
content filtering enabled buffer until output transformation completes, preventing
sensitive values split across chunks from leaking. Other profiles preserve native
incremental updates while retaining input-length, tool and budget checks. Structured
RAG/Help output remains buffered until its domain validation and repair complete.

Local inspection hosts may explicitly pass `AIStartupValidationMode.LocalInspection`
to `AddAIRuntime`. It defers default model/provider/session readiness checks so
registered entities remain discoverable without infrastructure. Profile-capable
entities expose unavailable reasons for missing runtime configuration/provider,
invalid guards/timeouts or absent session storage. Bound profile settings are
checked on execution. This does not disable DI graph or registered options
validation. `Strict` remains the default for every existing caller.

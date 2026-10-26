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

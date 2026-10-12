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

# LangChain / LangGraph → Microsoft Agent Framework crosswalk

Use this as a mental-model bridge, not as a claim of one-to-one API equivalence.

| LangChain / LangGraph concept | Closest MAF concept | Important design difference |
|---|---|---|
| LangChain agent | `AIAgent` / commonly `ChatClientAgent` | MAF separates agent runtime, session/context, middleware, and workflows explicitly. |
| Tool | `AIFunction` / `AITool` / MCP tool | Prefer `AIFunctionFactory.Create` for local C# methods when supported; tool security remains deterministic code. |
| Agent as tool | `AIAgent.AsAIFunction()` | Use when a parent agent should decide whether to delegate; this is still LLM-driven routing. |
| LangGraph node | Workflow `Executor` or function-based executor | In C#, prefer typed message handlers and MAF workflow contracts rather than a generic dictionary state node. |
| Graph edge | Workflow edge (`AddEdge`, conditional/switch/fan-out/fan-in patterns) | Routing should be explicit and typed where possible. |
| Conditional edge | MAF conditional edge / switch | Deterministic routing belongs here rather than asking an LLM to route when rules are known. |
| Graph state | Typed workflow messages + workflow shared state when justified | Do not default to one giant mutable state object; message flow is usually clearer. |
| Checkpointer | Workflow checkpoints / durable execution integration | Separate checkpoint/runtime durability from domain persistence. |
| Messages/history | Agent session + history provider | Session state is conversation-scoped; it is not automatically long-term product memory. |
| Runnable middleware/callbacks | Agent middleware / function middleware / `IChatClient` middleware | Choose the layer based on what you need to intercept. |
| Retriever | Context provider or retrieval tool | Proactive context injection and agent-decided retrieval are different patterns. |
| 2-step RAG | Context provider that retrieves before model invocation | Retrieval is deterministic/up-front. |
| Agentic RAG | Search/retrieval exposed as a tool | Agent decides whether/what to retrieve. |
| Hybrid RAG | Context provider + retrieval tool | Common context can be injected while ambiguous lookups remain agent-controlled. |
| Structured output parser | MAF/provider-supported structured output + typed validation | Validation remains application responsibility even when schema-constrained generation is available. |
| Human interrupt | Workflow request/response / `RequestPort` patterns | Model approval and workflow pause/resume should be explicit runtime events. |
| Subgraph | Nested/composed workflow or workflow-as-agent depending semantics | Do not wrap a workflow as an agent unless agent semantics are actually required. |

## Migration traps

- Do not recreate LangGraph's generic shared state dictionary if typed workflow messages express the flow.
- Do not convert every LangGraph node into an agent; many nodes should be deterministic executors/services.
- Do not use an LLM router when the branch rule is deterministic.
- Do not treat chat history, application memory, retrieval context, and workflow checkpoint state as one concept.
- Do not build a custom tool registry before checking MAF / `Microsoft.Extensions.AI` tool abstractions.
- Do not import provider-specific SDK types into workflow state because a Python sample did so conveniently.

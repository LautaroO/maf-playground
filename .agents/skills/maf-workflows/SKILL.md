---
name: maf-workflows
description: Implement or review Microsoft Agent Framework workflows in C#/.NET using WorkflowBuilder, Executor, MessageHandler, typed messages, edges, conditional/switch routing, fan-out/fan-in, shared state, checkpoints, RequestPort/HITL, workflow outputs, agent executors, workflow-as-agent, or durable workflow integration. Use when process control must be explicit rather than model-decided.
---

# MAF workflows

Read `../../references/official-sources.md`, `../../references/langgraph-crosswalk.md`, and `references/executors-edges.md`. Read `references/orchestrations.md` for multi-agent composition and `references/state-checkpoints-hitl.md` for state/durability/HITL. When MAF is one module inside a larger application, also read `../../references/application-boundaries.md`.

## Choose built-in orchestration vs custom graph

Before authoring a custom graph, check MAF's built-in sequential, concurrent, handoff, group-chat, and Magentic orchestration patterns. Use one when its control semantics match the requirement; otherwise use a custom typed workflow.

Do not use a model-driven multi-agent orchestration for deterministic business flow.

## Core model

A MAF workflow is an explicit data-flow graph. The core pieces are:

- `WorkflowBuilder` — graph/topology definition;
- `Executor` — processing node;
- `[MessageHandler]` methods in C# — recommended typed message handlers in current docs;
- typed messages — contracts flowing through the graph;
- edges — direct, conditional, switch, fan-out, fan-in patterns;
- `IWorkflowContext` — send messages, yield outputs/events, access supported state mechanisms;
- workflow events/run APIs — execution and external interaction.

Verify the installed version before coding.

## Executor rule

An executor should perform one graph step. It may call a deterministic application/domain service or a bounded agent. Do not turn every executor into an agent, and do not move business rules into executor handlers merely because the process is represented as a workflow.

Prefer `partial` executor classes with `[MessageHandler]` where supported by the installed MAF version.

## Typed flow first

Prefer dedicated records between steps:

```csharp
public sealed record TranslationRequested(...);
public sealed record TranslationProduced(...);
public sealed record ValidationCompleted(...);
```

Use shared workflow state when data genuinely must be accessed outside direct message flow or would otherwise be duplicated awkwardly. Avoid one giant mutable "graph state" object imported from LangGraph habits.

## Routing

If the routing rule is deterministic, put it in an edge/switch condition using validated typed data.

If the model decides a category used by routing, obtain a structured output, validate it in C#, then route deterministically.

Do not let free-form model text directly control critical branching.

## Fan-out / fan-in

Use workflow fan-out/fan-in constructs for independent parallel branches and aggregation. Do not launch ad hoc `Task.WhenAll` inside an executor when the branches are semantically workflow steps that need observability/state/checkpoint behavior.

## Retry

Choose the owner of retries:

- transient HTTP/provider retry → adapter/client policy;
- semantic re-attempt after invalid model output → bounded workflow transition;
- business retry after external side effect → explicit idempotent workflow logic.

Avoid nested multiplicative retries.

## HITL and external waits

Use workflow request/response mechanisms such as `RequestPort` patterns when the workflow must pause for human/external input. Do not busy-wait or poll inside an executor.

## Checkpoints / durability

Use checkpoints when in-process workflow state must be resumed/replayed. Use durable extensions/runtime when the execution itself must survive process loss and long waits. Keep domain persistence distinct from runtime checkpoint state.

## Workflow as agent

Expose a workflow as an `AIAgent` only when another agent/agent API needs to invoke the workflow using agent semantics. Do not do it merely to run or visualize a workflow; native workflow registration is preferable where supported.

## Testing

Test graph routing and executor behavior deterministically. Mock/fake only the semantic agent steps. Verify branch defaults, dead ends, cancellation, retries, idempotency, and checkpoint/HITL behavior when applicable.

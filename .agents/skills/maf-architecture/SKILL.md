---
name: maf-architecture
description: Design or refactor Microsoft Agent Framework architecture in C#/.NET. Use when deciding whether behavior should be deterministic C# code, a tool, an AIAgent/ChatClientAgent, a Workflow/Executor/edge, middleware, AgentSession state, a context provider/RAG component, checkpoint/durable state, or a provider adapter. Use before implementation when responsibilities or orchestration boundaries are unclear.
---

# MAF architecture

## Goal

Choose the smallest idiomatic MAF design before writing code.

## Required process

1. Read repository `AGENTS.md` and project-specific context.
2. Inspect installed MAF package versions and existing abstractions.
3. Detect whether MAF is the center of the repository or one module inside a larger application. If it is one module, read `../../references/application-boundaries.md`.
4. Read `../../references/official-sources.md`, `../../references/provider-neutrality.md`, and `../../references/langgraph-crosswalk.md` as needed.
5. Decompose the requirement into semantic decisions, deterministic rules, side effects, state, and external integrations.
6. Classify each responsibility using the decision matrix below.
7. Route implementation work to the specialized skill rather than inventing APIs here.

## Decision matrix

### Deterministic service

Choose ordinary C# when behavior is fully specifiable: validation, calculations, transformations, authorization, exact routing, persistence, retry policy, idempotency, or business rules.

### Tool

Choose a tool when an agent needs a bounded action or lookup. The agent may choose *whether* to call it, but the tool implementation and authorization are deterministic.

Read `$maf-tools` before implementation.

### Agent

Choose an agent for open-ended semantic work: language understanding, synthesis, semantic classification where rules are not exact, planning, conversation, or bounded autonomous tool selection.

Read `$maf-agents` before implementation.

### Workflow

Choose a workflow when the *process itself* must be controlled: required sequence, deterministic branches, parallelism, retries, approvals, pause/resume, checkpoints, aggregation, or explicit state transitions.

An executor is roughly the MAF equivalent of a LangGraph node, but avoid recreating a generic state-machine style if typed messages/edges express the flow.

Read `$maf-workflows` before implementation.

### Middleware

Choose middleware only for cross-cutting interception. First decide whether interception belongs around an entire agent run, a tool/function invocation, or a model-client call.

Read `$maf-middleware`.

### Session / context / memory

Use session state for conversation lifetime, context providers for supplied context/knowledge, and application persistence for durable product data. Do not call all of them “memory”.

Read `$maf-context-memory`.

### Provider adapter

If code depends on provider SDK/auth/options/capabilities, isolate it in infrastructure/composition root.

Read `$maf-provider-integration`.

## Application boundary first

If the solution contains ordinary domain/application projects plus an AI project, treat the AI project as an orchestration/integration boundary. MAF may call application use cases; it must not become the owner of domain rules. Read `../../references/application-boundaries.md`.

## Strong defaults

- Workflow controls deterministic process; agents perform bounded semantic steps.
- Typed messages before shared workflow state.
- Structured model outputs before workflow branching.
- Deterministic validators after model outputs.
- Provider-specific construction at the composition root.
- Human approval outside prompts for consequential actions.
- No custom orchestration loop until MAF workflow features have been evaluated.

## Architecture deliverable

Return a compact design containing:

1. current behavior;
2. component/responsibility breakdown;
3. selected MAF abstraction for each responsibility;
4. key data/state lifetimes;
5. provider-specific boundaries;
6. failure/retry/idempotency model;
7. test strategy;
8. risks/assumptions;
9. which specialized MAF skills should be used next.

Mark architectural judgment separately from documented MAF requirements.

---
name: maf-agents
description: Implement or review Microsoft Agent Framework agents in C#/.NET, especially AIAgent and ChatClientAgent, agent construction, RunAsync/streaming, AgentSession use, agent instructions, structured outputs, custom agents, or agent-as-tool composition. Use when the model owns an open-ended semantic decision or tool-selection loop.
---

# MAF agents

## Before coding

Read `../../references/official-sources.md` and verify installed packages. Then read `references/agent-patterns.md`.

## Default model

For application-owned chat/model agents, prefer the framework's normal `AIAgent`/`ChatClientAgent` path over a custom model loop when the installed provider can expose a compatible `IChatClient` or agent implementation.

`AIAgent` is the common agent abstraction. `ChatClientAgent` is the important application-owned implementation for a provider-neutral `IChatClient` pipeline. Verify constructor/options signatures against installed packages.

Only derive a custom `AIAgent` when the runtime behavior genuinely cannot be represented by an existing MAF agent type. Do not create a custom agent merely to wrap one prompt call.

## Agent responsibility

An agent should own semantic behavior, not application invariants.

Keep outside the agent:

- exact business rules;
- persistence rules;
- permissions/authorization;
- deterministic routing;
- retry/idempotency policy;
- irreversible side-effect decisions;
- data validation that can be expressed in C#.

## Instructions

Use instructions for role, task framing, semantic constraints, tool-use guidance, and output expectations. Do not use the prompt as the only enforcement mechanism for security or business rules.

## Structured outputs

When another component consumes the result programmatically, use provider/MAF-supported structured output where available, deserialize to a dedicated type, validate, and only then act.

Do not assume every agent/provider supports the same structured-output mechanism. The provider adapter/capability layer must expose limitations.

## Sessions

Create/reuse an `AgentSession` when multiple runs belong to the same conversation. Do not use session state as general application persistence.

Use `$maf-context-memory` when deciding history/context/storage behavior.

## Agent composition

When a parent agent should decide whether to delegate to another specialized agent, consider exposing the child agent with `AsAIFunction()` when supported by the installed API. This is analogous to agent-as-tool delegation in LangChain: routing remains model-driven.

If delegation order/branching must be guaranteed, prefer a workflow instead.

## Testing

- Keep the agent's deterministic collaborators injectable.
- Test prompt-independent rules outside the LLM.
- Use fake/stub agents or chat clients where the installed abstractions permit it.
- Real-model quality belongs in opt-in evaluations/integration tests.

## Completion check

Confirm that the implementation uses a real MAF agent abstraction, provider types do not leak into core contracts, session lifetime is intentional, structured output is validated, tools are bounded, cancellation propagates, and deterministic process control has not been hidden inside the prompt.

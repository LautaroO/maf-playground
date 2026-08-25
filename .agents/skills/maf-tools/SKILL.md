---
name: maf-tools
description: Implement or review Microsoft Agent Framework tools in C#/.NET, including AIFunction/AIFunctionFactory local function tools, AITool contracts, agent-as-tool via AsAIFunction, MCP tools, tool approval, tool metadata, authorization, cancellation, and safe external side effects.
---

# MAF tools

Read `../../references/official-sources.md` plus the relevant file in `references/`. When MAF is one module inside a larger application, also read `../../references/application-boundaries.md`.

## Local function tools

For local C# capabilities, prefer MAF / `Microsoft.Extensions.AI` function-tool abstractions supported by the installed stack. Current MAF guidance commonly uses `AIFunctionFactory.Create(...)` to convert a method/delegate into an `AIFunction`.

Use descriptions deliberately: tool and parameter descriptions are part of the model's selection interface.

Do not invent a parallel JSON tool registry unless the provider/MAF integration actually requires one.

## Tool boundary

Each tool should expose one bounded capability. The tool may call an application service; it should not become the application service itself. In mixed solutions, treat the tool primarily as an AI-facing adapter over an application capability; keep domain rules, authorization policy ownership, and general persistence behavior in the appropriate non-AI layer.

Good tool:

`SearchRunbookAsync(query, cancellationToken)` → delegates to a tested retrieval service.

Bad tool:

`AdminAsync(action, payload)` → generic privileged capability controlled by model text.

## Deterministic enforcement

Treat model-generated arguments as untrusted.

Inside the deterministic boundary enforce:

- input validation;
- authorization/tenant scope;
- business invariants;
- rate/size limits;
- timeout/cancellation;
- idempotency for retried side effects;
- output shaping/redaction.

## MCP

Use the official MCP C# SDK integration when MCP is the right external capability boundary. Discover only the tools the agent actually needs; do not blindly expose every remote tool.

Third-party MCP servers are external trust boundaries. Review data sharing, auth headers, server provenance, tool descriptions, and side effects.

## Approval

If a tool can perform consequential/irreversible actions, use the framework's approval/HITL mechanism supported by the installed agent/provider rather than asking the prompt to "be careful".

## Agent as tool

Use `AsAIFunction()` for semantic delegation to a specialized child agent when the parent model should decide whether to delegate. If routing is deterministic, use workflow edges instead.

## Testing

Test the underlying service and tool wrapper without an LLM. Verify invalid/unauthorized arguments fail closed.

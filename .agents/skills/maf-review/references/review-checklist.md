# MAF review checklist

## Agent

- Is semantic/open-ended behavior actually needed?
- Is `AIAgent`/`ChatClientAgent` being used instead of a handwritten LLM loop where appropriate?
- Are instructions focused rather than containing business rule engines?
- Is session lifetime explicit?
- Are structured outputs validated before use?

## Tools

- One bounded capability per tool?
- Clear metadata/descriptions?
- Deterministic validation and authorization?
- Cancellation/timeouts?
- No generic privileged client exposed to the model?
- Side effects idempotent/approved where needed?
- MCP server/tool trust reviewed?

## Workflow

- Should this process be a workflow rather than an agent loop / giant if-else service?
- Executors small and responsibility-focused?
- Typed messages used?
- Deterministic routing represented as edges/switches?
- Fan-out/fan-in represented in graph when semantically important?
- Shared state minimized and isolated?
- Retry ownership clear?
- HITL/external waits use request/response rather than polling?
- Checkpoint/durability requirements explicit?
- Workflow-as-agent used only for agent semantics?

## Context / memory

- Session state vs context provider vs workflow state vs durable app storage distinguished?
- RAG strategy explicitly 2-step, agentic, or hybrid?
- Retrieval independently testable?
- Conversation history retention/compaction/privacy considered?

## Middleware

- Correct layer: agent, function, or `IChatClient`?
- Cross-cutting rather than hidden business flow?
- Ordering/short-circuit behavior tested?

## Provider

- SDK types isolated?
- Model/provider selection in composition root/config?
- Capability differences explicit?
- Provider swap avoids rewriting workflow/tool/domain contracts?
- Provider exceptions translated at boundary?

## Reliability

- Cancellation propagated?
- Timeouts defined?
- Retry multiplication avoided?
- Duplicate side effects prevented on retry/resume?
- Invalid model/tool outputs fail closed?

## Observability

- Agent/model/tool/workflow boundaries traceable?
- Sensitive payload capture controlled?
- OTLP destination separate from DevUI trace assumptions?

## Testing

- Default suite live-model-free?
- Workflow branches/failure paths covered?
- Tools/validators independently tested?
- Provider integration tests isolated?
- Real-model evaluation opt-in and purpose-specific?

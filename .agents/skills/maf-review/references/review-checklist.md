# MAF review checklist

## Architecture

- Is deterministic logic outside the agent?
- Is open-ended semantic behavior bounded inside agents?
- Are explicit multi-step flows represented as workflows?
- Are large manual routers candidates for typed workflow edges?
- Are MAF abstractions used instead of custom loops or state machines where appropriate?

## Tools

- Narrow responsibility and explicit contract?
- Input validation?
- Cancellation and timeout?
- Authorization enforced in code?
- Idempotency for side effects?
- Minimum permissions?
- Independently testable?
- No unrestricted shell, database, filesystem, or HTTP access?

## Prompts and outputs

- Business rules outside prompts where possible?
- Structured outputs for programmatic decisions?
- Schema and semantic validation?
- Bounded repair/retry behavior?
- No execution from unvalidated free text?

## State

- Session, workflow, checkpoint, memory, context, and application data distinguished?
- Durable source of truth outside chat history?
- Serialization/versioning defined?
- Duplicate execution after resume considered?
- Retention and privacy defined?

## Provider/cloud isolation

- Is provider neutrality treated as a hard requirement?
- Are Azure/OpenAI examples interpreted only as concrete adapters?
- Are provider SDK types limited to infrastructure, adapters, and composition roots?
- Are agents, workflow contracts, tools, validators, prompts, and persistence models provider-neutral?
- Can the model provider change without rewriting application logic or workflows?
- Are model names, deployment names, regions, API versions, endpoints, and credentials absent from core code?
- Are provider-specific capabilities hidden behind small capability-oriented interfaces?
- Are streaming, tool calling, structured outputs, usage metadata, and provider errors translated at the adapter boundary?
- Are hosting and persistence replaceable?
- Is configuration typed and are secrets externalized?

## Reliability

- Cancellation propagated?
- Timeouts set?
- Retries bounded and only for transient failures?
- Non-idempotent operations protected?
- Partial failure behavior explicit?
- Human approval for consequential actions?

## Observability

- Structured logs and tracing?
- Correlation/workflow IDs?
- Agent/executor/tool durations?
- Model usage where available?
- Sensitive data redacted?

## Testing

- Domain, tools, validators, and executors tested without LLM?
- Workflow branching and failures tested with fakes?
- Provider adapters contract-tested?
- Real-model tests opt-in?
- Prompt injection and malformed outputs tested?

---
name: maf-testing
description: Design tests and evaluations for Microsoft Agent Framework C#/.NET agents, tools, workflows, middleware, sessions/context providers, provider adapters, structured outputs, DevUI hosts, and model behavior. Use to keep deterministic tests separate from MAF/MEAI evaluation and live-model quality gates.
---

# MAF testing

Read `references/test-strategy.md` and `references/evaluation.md`.

## Testing pyramid

Prefer this order:

1. deterministic domain/service tests;
2. tool wrapper + validator tests;
3. workflow executor and routing tests;
4. middleware/context/session behavior tests;
5. provider-adapter integration tests;
6. MAF hosting/DevUI smoke tests;
7. opt-in real-model evaluation tests.

The default test suite should not depend on network, paid model calls, or nondeterministic language output.

## Workflow tests

Assert graph behavior from typed inputs/outputs/events. Test all branches/defaults, retry limits, cancellation, idempotency, and state isolation. Replace only semantic agent steps with controllable fakes/stubs.

## Structured output

Test valid, invalid, missing, unknown enum, out-of-range, and malicious values after deserialization. The model schema is not a substitute for validator tests.

## Provider tests

Keep provider-specific tests focused on adapter construction, supported capabilities, streaming/tool/structured-output compatibility, and failure translation. Mark live-provider tests opt-in.

## Model evaluations

Use evaluations for semantic quality: task success, groundedness, tool selection, hallucination, safety, latency/cost. Do not turn flaky model judgments into the only CI gate for deterministic behavior.

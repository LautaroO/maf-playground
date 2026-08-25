# Application boundaries when MAF is one module

Use this reference when Microsoft Agent Framework is part of a larger application rather than the center of the repository.

## Core rule

MAF is an orchestration/integration layer. It may call application capabilities; it should not become the owner of application/domain behavior.

Prefer dependency direction such as:

```text
Domain <- Application <- AI / Infrastructure / Hosts
```

Exact project names may differ, but the important constraint is inward dependency: domain/application code must not depend on MAF or a model-provider SDK merely because an AI feature exists.

## AI module responsibilities

The AI project/module may own:

- MAF agent construction and instructions;
- workflow topology, executors, and orchestration messages;
- AI-specific tools that adapt application use cases for model invocation;
- context providers, retrieval adapters, and model-facing projections;
- structured model-output DTOs and validators specific to AI decisions;
- model-provider adapters/configuration;
- AI telemetry, evaluations, DevUI registration, and AI-specific hosting glue.

It should normally not own:

- domain invariants;
- authorization policy definitions;
- pricing, eligibility, accounting, or other business rules;
- persistence decisions that belong to application/domain services;
- generic HTTP/database integration merely because a tool needs it;
- provider SDK types in contracts consumed by non-AI projects.

## Tools as adapters

A tool should usually be a thin AI-facing adapter over an application capability.

Good:

```text
Agent -> GetOrderPriceTool -> IOrderPricingUseCase -> Domain
```

Avoid:

```text
Agent -> GetOrderPriceTool
                     |- computes discounts
                     |- applies taxes
                     |- queries DbContext directly
                     `- decides business eligibility
```

The tool owns the model-facing contract, argument validation appropriate to that contract, and adaptation to the application service. The application/domain layer owns business meaning.

## Workflows as process orchestration

A workflow may coordinate application use cases, semantic agent steps, validation, approval, retry, and asynchronous process state.

Do not move domain rules into executor handlers simply to keep everything inside the graph. If a branch is a business decision that can be expressed deterministically, call the application/domain service responsible for that decision and branch on its typed result.

## Models and messages

Separate three kinds of types when useful:

1. domain/application types;
2. AI-facing structured input/output contracts;
3. workflow orchestration messages.

Do not reuse a provider-native response type as an application contract.

## Failures

Translate model/provider/runtime failures at the AI boundary. Non-AI callers should not need to understand provider-specific exceptions unless the application explicitly exposes that concern.

## Testing

Keep these independently testable:

- domain/application behavior without MAF;
- AI tools with mocked/fake application services;
- workflow routing without a live model where possible;
- provider adapters in focused integration tests;
- model quality in explicit evaluation suites.

## Review smell

If removing the AI project would delete important business rules or make ordinary non-AI use cases impossible, the AI boundary is probably too broad.

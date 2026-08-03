---
name: maf-review
description: Review Microsoft Agent Framework C#/.NET code and architecture. Use for pull request review, refactoring advice, or checking whether agents, workflows, tools, middleware, memory, context, structured outputs, and provider adapters are used idiomatically.
---

# Microsoft Agent Framework review

## Review method

1. Read the repository root `AGENTS.md`.
2. Inspect package versions and relevant tests.
3. Read `references/review-checklist.md`.
4. Compare implementation with current official docs and pinned official .NET samples.
5. Separate confirmed framework guidance from architectural recommendations.

## Required review structure

### 1. What the implementation currently does

Describe control flow, model calls, tools, state, persistence, and provider dependencies.

### 2. MAF abstraction assessment

For each relevant area, label it:

- appropriate;
- incomplete;
- overly manual;
- over-agentic;
- provider-coupled;
- unsafe;
- unclear due to API/version uncertainty.

### 3. Recommended restructuring

Explain which logic belongs in:

- deterministic services;
- tools;
- agents;
- workflows/executors/edges;
- middleware;
- validators/structured outputs;
- sessions/context/memory;
- persistence/checkpointing;
- provider adapters.

### 4. Production concerns

Review cancellation, timeout, retry, idempotency, partial failures, observability, data privacy, prompt injection, tool permissions, and approval boundaries.

### 5. Test plan

Specify deterministic unit tests, workflow tests with fakes, adapter contract tests, and optional real-model evaluations.

## Severity

Use:

- Critical: security, data loss, unauthorized or irreversible execution.
- High: likely reliability failure, invalid decisions, state corruption, severe coupling.
- Medium: maintainability, testability, observability, or framework misuse.
- Low: clarity, naming, duplication, minor idiomatic improvements.

Do not focus on formatting already enforced by analyzers.

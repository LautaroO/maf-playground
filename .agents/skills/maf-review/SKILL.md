---
name: maf-review
description: Review a Microsoft Agent Framework C#/.NET implementation or repository for idiomatic use of AIAgent/ChatClientAgent, tools, workflows/executors/edges, context/session/memory, middleware, provider isolation, structured outputs, reliability, security, observability, testing, durability, and DevUI. Use for architecture/code reviews and identify manual mechanisms that MAF should own.
---

# MAF review

Read `../../references/official-sources.md`, `../../references/langgraph-crosswalk.md`, and `references/review-checklist.md`. When MAF is one module inside a larger product, also read `../../references/application-boundaries.md`.

## Review method

For each agentic component answer:

1. What is it currently doing?
2. Which MAF abstraction is it actually using?
3. Is that abstraction the right owner of the responsibility?
4. What has been implemented manually that MAF already models?
5. What deterministic logic has leaked into prompts/agents?
6. What provider-specific code has leaked upward?
7. What state lifetime is being assumed?
8. What happens on invalid model output, cancellation, retry, resume, duplicate delivery, or partial failure?
9. Can the important behavior be tested without a live model?

## Severity

Prioritize findings:

- **Bug / correctness** — behavior can fail or corrupt state/data.
- **Architecture** — wrong abstraction/coupling creates meaningful maintenance risk.
- **Reliability/security** — unsafe tool/model/state/retry behavior.
- **MAF idiom** — manual mechanism should use a framework primitive.
- **Quality** — naming, duplication, test ergonomics, observability.

Do not inflate stylistic preferences into architecture bugs.

## Required output

Keep the review actionable:

- current architecture summary;
- bugs;
- misuse/missed MAF abstractions;
- recommended restructuring;
- provider portability issues;
- production risks;
- test gaps;
- top 3 changes by impact.

## Additional boundary smell for mixed solutions

When MAF is only one project/module, explicitly check whether removing the AI module would also remove domain rules, ordinary use cases, authorization policy, or core persistence behavior. If yes, recommend moving those responsibilities back to application/domain code and keeping MAF as orchestration/adaptation.

# Provider-neutrality rules

Provider neutrality means architecture can swap model implementations without rewriting agent/workflow/business contracts.

Keep provider SDK code in infrastructure/composition roots. Prefer MAF and `Microsoft.Extensions.AI` abstractions where they represent the required capability.

Provider adapter responsibilities may include:

- authentication and endpoint creation;
- constructing `IChatClient` or another MAF-compatible model client;
- model/deployment selection from configuration;
- translating provider-specific options;
- provider-specific structured-output/tool configuration when unavoidable;
- usage/safety metadata translation;
- transient-error classification;
- capability detection.

Core code must not branch on provider names. If behavior depends on a capability, expose a capability-oriented contract rather than `if (provider == ...)` in agents/workflows.

Do not promise portability that the underlying providers do not actually support. Make missing capabilities explicit and test them at adapter boundaries.

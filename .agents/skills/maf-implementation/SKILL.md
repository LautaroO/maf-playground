---
name: maf-implementation
description: Implement Microsoft Agent Framework agents, tools, workflows, middleware, sessions, context providers, structured outputs, or durable patterns in C#/.NET. Use only when editing or generating MAF code; verify installed NuGet APIs and official samples before coding.
---

# Microsoft Agent Framework implementation

## Required process

1. Read the repository root `AGENTS.md`.
2. Inspect `global.json`, solution files, project files, central package management, and existing code.
3. Identify exact MAF package names and versions.
4. Search local code and tests for the closest pattern.
5. Consult `references/official-sources.md` and the pinned official .NET source.
6. Do not copy a sample blindly: adapt it to repository conventions and installed APIs.
7. Implement the smallest complete change with tests.
8. Run formatting, build, and targeted tests.

## Implementation rules

- Provider neutrality is mandatory.
- Treat Azure/OpenAI setup in official samples as replaceable adapter code.
- Keep provider client construction, authentication, request mapping, streaming translation, model options, and usage metadata behind DI and infrastructure boundaries.
- Do not expose provider SDK types in agents, tools, workflow contracts, validators, persistence models, or core tests.
- A provider swap should require only configuration, dependency registration, and adapter changes.
- Use typed records for messages, tool inputs, structured outputs, and workflow results.
- Pass `CancellationToken` throughout.
- Add timeout and bounded retry behavior at external boundaries.
- Validate model output before branching or tool execution.
- Keep tools narrow and independently testable.
- Do not create manual orchestration loops when a MAF workflow or executor graph expresses the behavior.
- Do not create a workflow for a single deterministic function.
- Do not add an agent when a typed service is sufficient.
- Keep prompts out of domain entities.
- Avoid static mutable state.
- Never hardcode credentials.

## Testing

Prefer fake or stub agents/model clients for default tests. Test workflow transitions and failure behavior deterministically.

Real-model tests must be opt-in and clearly labeled.

## Completion report

Report:

- files changed;
- architectural choice;
- package/API assumptions;
- commands run and results;
- remaining provider-specific coupling;
- untested or uncertain behavior.

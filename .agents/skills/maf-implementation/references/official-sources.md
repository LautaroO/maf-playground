# Official sources

Use these sources in priority order.

## Microsoft Learn

- Agent Framework documentation:
  https://learn.microsoft.com/agent-framework/
- Overview, C# pivot:
  https://learn.microsoft.com/agent-framework/overview/?pivots=programming-language-csharp
- Agents:
  https://learn.microsoft.com/agent-framework/agents/
- Tools:
  https://learn.microsoft.com/agent-framework/agents/tools/
- Middleware:
  https://learn.microsoft.com/agent-framework/agents/middleware/
- Workflows:
  https://learn.microsoft.com/agent-framework/workflows/
- Executors:
  https://learn.microsoft.com/agent-framework/workflows/executors
- Orchestrations:
  https://learn.microsoft.com/agent-framework/workflows/orchestrations/

Documentation routes may change. Start from the documentation root when a deep link no longer resolves.

## Official repositories

- Main repository:
  https://github.com/microsoft/agent-framework
- Pinned .NET tree used as a stable reference:
  https://github.com/microsoft/agent-framework/tree/c073ed9f74bf864d4c696e03a705e2811311a4db/dotnet
- Official samples repository:
  https://github.com/microsoft/Agent-Framework-Samples

## Version rule

The installed NuGet package version is authoritative for compilable APIs.

Use the pinned source for stable examples and architecture context, but check whether the repository's package version predates or postdates that commit. Do not mix APIs from `main`, the pinned commit, and installed packages without noting the difference.

## Source interpretation

Separate:

1. MAF abstractions and contracts;
2. provider-specific integration code;
3. cloud/hosting-specific setup;
4. sample-only shortcuts.

Azure, Microsoft Foundry, Azure OpenAI, and OpenAI code in a sample does not imply the application architecture may depend on those providers.

Treat provider-specific code only as an example of how to connect a concrete implementation to MAF. Extract the MAF abstraction being demonstrated, isolate the concrete SDK behind an adapter, and keep the rest of the application portable.

The target architecture must allow a provider swap without rewriting agents, tools, workflows, validators, domain logic, or core tests. Any unavoidable provider-only capability must be documented explicitly.

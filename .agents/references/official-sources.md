# Official sources and version discipline

Use official sources in this order:

1. Microsoft Learn Agent Framework documentation: https://learn.microsoft.com/agent-framework/
2. Official repository: https://github.com/microsoft/agent-framework
3. Official samples: https://github.com/microsoft/Agent-Framework-Samples
4. Installed NuGet package XML docs / decompiled public API / local source when available.

Useful Learn areas:

- Overview: https://learn.microsoft.com/agent-framework/overview/
- Agents: https://learn.microsoft.com/agent-framework/agents/
- Agent pipeline: https://learn.microsoft.com/agent-framework/agents/agent-pipeline
- Tools: https://learn.microsoft.com/agent-framework/agents/tools/
- Function tools: https://learn.microsoft.com/agent-framework/agents/tools/function-tools
- Local MCP tools: https://learn.microsoft.com/agent-framework/agents/tools/local-mcp-tools
- Structured outputs: https://learn.microsoft.com/agent-framework/agents/structured-outputs
- Sessions: https://learn.microsoft.com/agent-framework/agents/conversations/session
- Context providers: https://learn.microsoft.com/agent-framework/agents/conversations/context-providers
- RAG: https://learn.microsoft.com/agent-framework/agents/rag
- Middleware: https://learn.microsoft.com/agent-framework/agents/middleware/
- Runtime context: https://learn.microsoft.com/agent-framework/agents/middleware/runtime-context
- Workflows: https://learn.microsoft.com/agent-framework/workflows/
- Built-in orchestrations: https://learn.microsoft.com/agent-framework/workflows/orchestrations/
- Executors: https://learn.microsoft.com/agent-framework/workflows/executors
- Edges: https://learn.microsoft.com/agent-framework/workflows/edges
- Workflow state: https://learn.microsoft.com/agent-framework/workflows/state
- Checkpoints: https://learn.microsoft.com/agent-framework/workflows/checkpoints
- HITL: https://learn.microsoft.com/agent-framework/workflows/human-in-the-loop
- Workflows as agents: https://learn.microsoft.com/agent-framework/workflows/as-agents
- Durable extension: https://learn.microsoft.com/agent-framework/integrations/durable-extension
- Agent evaluation: https://learn.microsoft.com/agent-framework/agents/evaluation
- Agent observability: https://learn.microsoft.com/agent-framework/agents/observability
- DevUI: https://learn.microsoft.com/agent-framework/devui/
- Harness: https://learn.microsoft.com/agent-framework/agents/harness

## Version rule

MAF APIs change. The repository's installed package versions are authoritative for code that must compile.

Before using an API seen in current docs or `main`:

1. identify the installed package and version;
2. inspect the package's available type/method surface;
3. find the corresponding tag/commit/sample when possible;
4. adapt examples to the installed API instead of upgrading packages silently.

Never mix APIs from current docs, old samples, and installed packages without noticing the version mismatch.

## Source interpretation rule

For every official sample separate:

1. MAF abstraction being demonstrated;
2. model-provider-specific setup;
3. cloud/hosting-specific setup;
4. sample-only shortcuts.

Azure, Foundry, Azure OpenAI, or OpenAI appearing in a sample is not permission to couple core architecture to those SDKs.

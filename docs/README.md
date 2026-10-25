# MAF Playground documentation

Practical examples for C# developers learning to build agents and workflows with
Microsoft Agent Framework (MAF), and a reference for adapting those solutions to
client projects. Each example connects executable code with responsibility
boundaries, design decisions, and verification.

## Start here

Follow [Your first agent](start-here.md) to run the conversational example,
understand tools and context, and choose your next solution. You need C# and
dependency injection knowledge; previous agent experience is not required.

## Solutions

These examples represent concrete needs encountered while building agents. They
are working references with explicit limitations, not complete production hosts.

| Solution | Problem | What it demonstrates |
| --- | --- | --- |
| [Basic agent](../src/MafPlayground.AI/Agents/BasicAgent/README.md) | Answer conversational requests using trusted user context and an exact clock. | `AIAgent`, a narrow deterministic tool, context injection, provider boundaries, guards, and fake-client tests. |
| [Translation workflow](../src/MafPlayground.AI/Workflows/Translation/README.md) | Translate into several languages and validate each result. | Typed messages, parallel branches, bounded feedback retry, ordered fan-in, and partial failure. |
| [Basic RAG agent](../src/MafPlayground.AI/Agents/BasicRagAgent/README.md) | Answer questions from an explicitly ingested document collection. | Extraction, chunking, retrieval, structured claims, citation validation, and insufficient-evidence fallback. |
| [Repository help](repository-help/manual.md#repository-help-agent) | Explain application capabilities and exact CLI commands using bounded evidence. | Agent-selected read-only context tools, a live capability catalog, and grounded responses. |

The Basic agent page establishes the full solution-guide format. The other
examples retain their existing technical guides and will be expanded individually.

## Architecture

| Question | Reference |
| --- | --- |
| Where do contracts, orchestration, providers, and hosts belong? | [Solution architecture](../README.md#architecture) and [project responsibilities](../README.md#projects). |
| How are entities, models, guards, and sessions configured? | [Reusable runtime](../src/MafPlayground.AI.Runtime/README.md). |
| Why is some source still under `MafPlayground.AI`? | [Shared source layout](../src/MafPlayground.AI/README.md). |
| How do examples integrate with the entity runtime? | [Sample entities](../src/MafPlayground.AI.Samples/README.md). |
| How can model providers be replaced? | [Google adapter](../src/MafPlayground.Providers.Google/README.md) and [Ollama adapter](../src/MafPlayground.Providers.Ollama/README.md). |
| Where do retrieval and persistence responsibilities separate? | [Retrieval core](../src/MafPlayground.Retrieval/README.md) and [PostgreSQL adapter](../src/MafPlayground.Retrieval.Postgres/README.md). |
| How are traces and model costs observed? | [Observability](../src/MafPlayground.Observability/README.md). |

## Testing and evaluations

Start with the [test organization](../tests/README.md). The suites answer different
questions:

- [Deterministic tests](../tests/MafPlayground.Tests/README.md) verify contracts,
  tools, guards, and orchestration with controlled inputs and fake model clients.
- [Integration tests](../tests/MafPlayground.IntegrationTests/README.md) verify
  provider and storage adapters with explicitly enabled external services.
- [Quality evaluations](../tests/MafPlayground.Evals/README.md) exercise the help
  agent against versioned cases; real-model runs are opt-in. They are not a
  quality guarantee for every agent or provider.

Passing fake-client tests proves deterministic behavior, not that a particular
model will select the right tool or consistently produce a useful answer.

## Operational and design references

- [Repository manual](repository-help/manual.md): conceptual help and setup.
- [Generated CLI reference](repository-help/cli-reference.md): command names and options.
- [Current limitations](../README.md#current-limitations): development boundaries and known gaps.
- [Ingestion spike](rag-data-ingestion-spike.md): design evidence, not an introductory tutorial.
- [Codex setup](codex-setup.md): contributor tooling, separate from the agent runtime.

Markdown changes do not automatically update the repository help agent's indexed
knowledge. Follow the [explicit ingestion procedure](../README.md#repository-help-agent)
when updating its curated manual or CLI reference. This documentation index is
not automatically included in that knowledge base.

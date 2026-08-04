# MafPlayground.Tests

Fast deterministic xUnit tests for core behavior. This project must not require a
live model, network access, Docker, PostgreSQL, Ollama, DevUI, or an OTLP
collector.

## Structure

```text
MafPlayground.Tests/
├── AI/
│   ├── Agents/
│   ├── Resilience/
│   ├── Tools/
│   └── Workflows/
├── CLI/
├── Observability/
├── Retrieval/
└── TestDoubles/
```

| Area | What is verified |
| --- | --- |
| AI selection and registry | `provider:model` parsing, provider resolution, and errors. |
| Basic agent and tools | Instructions, trusted context, exact time values, and invalid time zones. |
| Basic RAG agent | Automatic evidence, refined search budget, citation validation/repair, and fallback. |
| Resilience | Timeout wrapping, cancellation distinction, and streaming behavior. |
| Translation workflow | Validation, parallel fan-out, ordered fan-in, retry feedback, partial failure, streaming events, topology, and DevUI input adaptation. |
| CLI | Parser hierarchy, configuration defaults, composition, console streaming, inspection, and trace mapping. |
| Observability | Opt-in registration, telemetry privacy, and cost estimation. |
| Retrieval | Extractor registry, PDF warnings, chunking, embedding selection, and ingestion behavior. |

## Test doubles

`FakeChatClient` supplies deterministic non-network chat behavior.
`FakeUserContextAccessor` supplies controlled invocation context. Workflow and RAG
tests also define narrow fake model/search implementations close to the tests that
use them.

Prefer fakes at provider-neutral boundaries such as `IChatClient`,
`ITranslationModel`, `IKnowledgeSearch`, `IEmbeddingGenerator`, and
`IKnowledgeStore`. Do not make core tests depend on provider SDK response types.

## Run

```bash
dotnet test tests/MafPlayground.Tests/MafPlayground.Tests.csproj
```

After a successful solution build:

```bash
dotnet test tests/MafPlayground.Tests/MafPlayground.Tests.csproj --no-build
```

## Test conventions

- Propagate and explicitly test `CancellationToken` behavior.
- Assert typed results, schemas, invariants, state transitions, and routing.
- Do not assert exact natural-language wording unless wording is the contract.
- Keep retries bounded and verify their attempt counts.
- Verify failure and partial-failure behavior, not only the happy path.
- Keep prompt/tool/retrieved payloads out of telemetry assertions unless sensitive
  capture is deliberately enabled for that test.
- Move any test requiring external infrastructure to
  `MafPlayground.IntegrationTests` and make it explicitly opt-in.


# Tests

Project documentation:

- [`MafPlayground.Tests`](MafPlayground.Tests/README.md): deterministic unit and
  component tests.
- [`MafPlayground.IntegrationTests`](MafPlayground.IntegrationTests/README.md):
  opt-in external-infrastructure tests.
- [`MafPlayground.Studio.Tests`](MafPlayground.Studio.Tests/README.md) covers Studio execution and API contracts.
- [`MafPlayground.Studio.IntegrationTests`](MafPlayground.Studio.IntegrationTests/README.md) covers host startup, transport and publishing.
- [`MafPlayground.Evals`](MafPlayground.Evals/README.md): versioned datasets,
  deterministic contracts, and opt-in model-judged quality evaluations.

`MafPlayground.Tests` contains deterministic tests that do not require a model,
network access, containers, or another external service. Its folders mirror the
logical production areas while keeping one lightweight unit-test project:

```text
MafPlayground.Tests/
  AI/
    Agents/
    Tools/
    Workflows/
  CLI/
  Observability/
  TestDoubles/
```

Tests that require Ollama, an OTLP collector, PostgreSQL, Docker Compose, or any
other external dependency belong in a separate `MafPlayground.IntegrationTests`
project. Quality datasets and model-judged behavioral regressions belong in
`MafPlayground.Evals`. The PostgreSQL/pgvector round-trip test is explicitly opt-in:

```bash
RAG_TEST_CONNECTION_STRING='Host=localhost;Database=maf_playground;Username=postgres;Password=postgres' \
  dotnet test tests/MafPlayground.IntegrationTests
```

Without that variable the database test is reported as skipped.

On macOS or Linux, after a Debug build, run `python3 tests/terminal/model_paste.py` to exercise local and remote keyboard input in real pseudoterminals. The test starts a loopback Studio server, opens the translation workflow repeatedly with arrows and Enter during resize, applies bracketed paste in the local model field and remote JSON editor, and checks that Ctrl+C exits from the editor with code 130. It makes no model calls.

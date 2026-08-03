# Tests

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
project. The PostgreSQL/pgvector round-trip test is explicitly opt-in:

```bash
RAG_TEST_CONNECTION_STRING='Host=localhost;Database=maf_playground;Username=postgres;Password=postgres' \
  dotnet test tests/MafPlayground.IntegrationTests
```

Without that variable the database test is reported as skipped.

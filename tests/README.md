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
project. That project should be added when the first integration test is
introduced, and its execution must be explicitly opt-in.

# Tests

Project documentation:

- [`MafPlayground.Tests`](MafPlayground.Tests/README.md): deterministic unit and
  component tests.
- [`MafPlayground.Studio.IntegrationTests`](MafPlayground.Studio.IntegrationTests/README.md): real CLI process/loopback Studio smoke checks without live models.
- [`MafPlayground.Studio.Tests`](MafPlayground.Studio.Tests/README.md): deterministic
  Studio catalog/schema/native-topology tests, including novel test-assembly entities.
- [`MafPlayground.IntegrationTests`](MafPlayground.IntegrationTests/README.md):
  opt-in external-infrastructure tests.
- [`MafPlayground.Evals`](MafPlayground.Evals/README.md): versioned datasets,
  deterministic contracts, and opt-in model-judged quality evaluations.

`MafPlayground.Tests` contains deterministic tests that do not require a model,
network access, containers, or another external service. Its folders mirror the
logical production areas:

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

## Studio verification

`./scripts/verify.sh` restores, verifies formatting, builds and runs the normal
CLI/runtime/DevUI regressions, fast Studio backend tests and actual CLI startup
checks. It excludes opt-in transport, package and real-model categories. All .NET
build/test steps use a single MSBuild node and no shared compiler.

Frontend gates are separate (run restore first for the offline contract exporter):

```sh
npm --prefix src/MafPlayground.Studio.Web ci
npm --prefix src/MafPlayground.Studio.Web run check:format
npm --prefix src/MafPlayground.Studio.Web run check:contracts
npm --prefix src/MafPlayground.Studio.Web run typecheck
npm --prefix src/MafPlayground.Studio.Web run test:unit
npm --prefix src/MafPlayground.Studio.Web run build
./scripts/verify-studio-package.sh
dotnet build src/MafPlayground.Studio.Web/e2e/fixtures/StudioFixture/StudioFixture.csproj -m:1 -p:UseSharedCompilation=false -nr:false
npm --prefix src/MafPlayground.Studio.Web run test:e2e
RUN_STUDIO_INTEGRATION_TESTS=true dotnet test tests/MafPlayground.Studio.IntegrationTests --no-build --filter Category=StudioTransport -m:1 -p:UseSharedCompilation=false -nr:false
```

Install Chromium once with `npx playwright install chromium` in the frontend
workspace. There are exactly three browser journeys and two fake-model loopback
transport checks. Real-model Studio smoke tests are separately opted in; see the
[Studio integration test README](MafPlayground.Studio.IntegrationTests/README.md).
No Studio evaluation dataset or LLM judge is introduced.

# MafPlayground.IntegrationTests

Opt-in xUnit tests for adapters that require real external infrastructure.
These tests are separate from the default deterministic suite so local and CI
runs do not accidentally depend on Docker, network access, or paid model calls.

## Current coverage

`PostgresKnowledgeStoreTests` exercises the real EF Core/pgvector adapter:

1. applies the retrieval migrations;
2. creates a unique collection;
3. replaces a document with a 768-dimensional vector chunk;
4. performs cosine semantic search;
5. verifies stable source and page metadata round-trip.

The collection name is unique per run. The test adds data to the configured
database and does not currently delete its collection afterward; use a local test
database, not a shared or production database.

## Run with local Compose infrastructure

```bash
docker compose up -d postgres
RAG_TEST_CONNECTION_STRING='Host=localhost;Port=5432;Database=maf_playground;Username=postgres;Password=postgres' \
  dotnet test tests/MafPlayground.IntegrationTests/MafPlayground.IntegrationTests.csproj
```

Without `RAG_TEST_CONNECTION_STRING`, `PostgresFactAttribute` reports the database
test as skipped instead of failing the default solution test run.

## Scope rules

Tests belong here when they require PostgreSQL, Ollama, a real embedding/chat
model, an OTLP collector, DevUI over HTTP, Docker Compose, or another external
system. New integration tests should:

- be opt-in through an explicit configuration switch;
- use isolated test identifiers or databases;
- avoid production credentials and externally visible side effects;
- clean up when safe, or document retained state;
- propagate cancellation and use bounded timeouts;
- assert adapter contracts rather than exact natural-language output;
- never run paid model evaluations as part of the default fast suite.

Tests that need real-model quality scoring should be categorized as evaluations
and documented separately from storage/provider contract tests.


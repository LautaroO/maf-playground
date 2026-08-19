# MafPlayground repository manual

## Purpose and scope

MafPlayground is a production-oriented C#/.NET playground for Microsoft Agent
Framework (MAF). It demonstrates provider-neutral agents, native workflows,
retrieval-augmented generation, observability, guards, local CLI execution, and
DevUI hosting.

This manual is the curated source for conceptual repository help. Exact CLI
commands and options are documented separately in `cli-reference.md`, which is
generated from the live `System.CommandLine` command tree.

The repository help agent is informational. It can search this manual and refine
that search once, but it cannot execute shell commands, modify files, inspect
arbitrary source code, or write to the knowledge base.

## Quick answers

### How do I run DevUI?

Run this exact command from the repository root:

```bash
dotnet run --project src/MafPlayground.CLI -- devui
```

Then open `http://localhost:5050/devui` in a browser. The `--model` option selects
the chat model and `--url` changes the loopback URL.

## Architecture

MAF is the orchestration layer. Model providers, persistence, hosts, and external
integrations remain replaceable adapters.

| Project | Responsibility |
| --- | --- |
| `MafPlayground.AI` | Provider-neutral agents, tools, context providers, workflows, guards, and resilience. |
| `MafPlayground.CLI` | Development CLI, dependency composition, entity inspection, and DevUI hosting. |
| `MafPlayground.Retrieval` | Document extraction, chunking, embeddings, search, and storage contracts. |
| `MafPlayground.Retrieval.Postgres` | EF Core, PostgreSQL, and pgvector persistence adapter. |
| `MafPlayground.Providers.Google` | Google Gen AI adapter for Gemini chat, embeddings, and token counting. |
| `MafPlayground.Providers.Ollama` | Local Ollama adapter for chat, embeddings, tokenizers, and sample pricing. |
| `MafPlayground.Observability` | OpenTelemetry registration and provider-neutral model-cost estimates. |

Provider-specific SDK types stay in provider adapters or the CLI composition
root. Agents and workflows depend on MAF, Microsoft.Extensions.AI, or
repository-owned contracts.

## Main execution concepts

- Use deterministic C# code for validation, routing, extraction, persistence,
  and other fully specified behavior.
- Use an `AIAgent` for open-ended conversational or semantic behavior.
- Use an `AIContextProvider` to supply bounded trusted context or retrieved
  evidence to an agent.
- Use a narrow tool when an agent needs a deterministic capability.
- Use a native workflow when ordering, branching, fan-out, validation, retries,
  or state transitions must be explicit.
- Use an agent session only for conversation-scoped history. The durable source
  of truth for RAG remains the knowledge database.

## Local prerequisites

- The .NET SDK version pinned by `global.json`.
- Docker with Compose for PostgreSQL/pgvector and optionally Aspire Dashboard.
- Ollama when using local chat or embedding models.
- `GEMINI_API_KEY` when using Google Gen AI.

The application does not load `.env` automatically. Copy `.env.example` to
`.env`, then load it into the current shell before running the CLI.

```bash
cp .env.example .env
set -a; source .env; set +a
```

## Model selection

Chat models use the `provider:model` selector supplied through `--model` or
`AI_MODEL`.

Examples:

```text
google:gemini-3.6-flash
ollama:llama3.1:8b
```

Each knowledge base independently selects its embedding model under
`AI:KnowledgeBases:<name>:EmbeddingModel`.

Examples:

```text
google:gemini-embedding-2
ollama:nomic-embed-text
```

The chat and embedding providers do not need to match. Gemini chat can query a
knowledge base embedded with Ollama/Nomic, provided all document and query
vectors within that collection use the same embedding identity and dimension.
The default `RepositoryHelp` knowledge base uses the multilingual
`google:gemini-embedding-2` model at 768 dimensions. Its chat model remains an
independent CLI or host selection.

## Agents and workflows

### Basic agent

`basic-agent` is a conversational agent with trusted local time-zone context and
a narrow current-date/time tool. It is useful for testing tools, sessions,
guards, streaming, and provider switching.

### Basic RAG agent

`basic-rag-agent` answers from an explicitly configured document knowledge base.
It retrieves evidence automatically, can perform one bounded refined search,
returns structured claims, validates every citation deterministically, and falls
back to an insufficient-evidence response instead of using unsupported model
knowledge.

### Repository help agent

`repository-help-agent` uses the same grounded-answer mechanism with the
dedicated `RepositoryHelp` knowledge base. Its scope is this repository, its
architecture, configuration, and CLI. It is instructed to answer in the user's
language and preserves exact command and configuration names from evidence.
When exact syntax is needed, the agent can call a narrow command lookup tool.
That tool resolves an exact command path against the live `System.CommandLine`
tree; natural-language understanding remains with the agent. Conceptual
questions continue through semantic retrieval and the grounded response
pipeline.

### Translation workflow

`translation-workflow` is a native MAF graph. It validates requested languages,
fans translations out concurrently, validates each branch, retries invalid
translations once with feedback, and returns partial failures without blocking
successful branches.

## RAG ingestion

Ingestion is an explicit deterministic maintenance operation. Its stages, in
execution order, are:

1. Receive a document file and validate its path and resource limits.
2. Select the format-specific extractor.
3. Produce a structured ingestion document with sections and metadata.
4. Split that structure with the token-aware chunker.
5. Generate document embeddings with the knowledge base's provider adapter.
6. Persist plain chunk text, metadata, and vectors through EF Core into
   PostgreSQL with pgvector.

Supported document types are Markdown, PDF, DOCX, and PPTX. Markdown uses the
Microsoft data-ingestion reader. DOCX and PPTX use repository-owned Open XML SDK
extractors. PDF extraction uses PdfPig and works best for PDFs with a normal text
layer; scanned or image-only pages require a future OCR adapter.

The chunker uses `Microsoft.Extensions.DataIngestion` document structure and the
tokenizer declared by the selected embedding provider. Chunk text is persisted
as plain text together with page/section metadata and its embedding vector.

Re-ingestion is idempotent. It skips unchanged documents when content hash,
provider-versioned embedding identity, chunking identity, and metadata match. A
document replacement and its chunks are committed atomically by the store
adapter.

## Repository help knowledge base setup

Start PostgreSQL and prepare the retrieval schema:

```bash
docker compose up -d postgres
dotnet run --project src/MafPlayground.CLI -- rag database migrate
```

Set `GEMINI_API_KEY` before ingestion and querying. The default
`RepositoryHelp` collection is `repository-help-multilingual-v1`; it is separate
from collections created with other embedding models because their vectors are
not compatible. Ingestion sends the manual and CLI-reference chunk text to
Google, and retrieval sends each user query to Google, so use this profile only
when those payloads are approved for the external service.

Regenerate the exact CLI reference after changing commands:

```bash
dotnet run --project src/MafPlayground.CLI -- \
  docs generate-cli-reference \
  --output docs/repository-help/cli-reference.md
```

Ingest the curated manual and generated reference as separate documents:

```bash
dotnet run --project src/MafPlayground.CLI -- \
  rag ingest --knowledge-base RepositoryHelp \
  --path docs/repository-help/manual.md \
  --source-root docs/repository-help \
  --metadata audience=developer --metadata source_kind=curated-manual

dotnet run --project src/MafPlayground.CLI -- \
  rag ingest --knowledge-base RepositoryHelp \
  --path docs/repository-help/cli-reference.md \
  --source-root docs/repository-help \
  --metadata audience=developer --metadata source_kind=generated-cli
```

Run one grounded question:

```bash
dotnet run --project src/MafPlayground.CLI -- \
  agent repository-help \
  --prompt "How do I run DevUI?"
```

Omit `--prompt` for an interactive conversation. Add `--watch` to display
sanitized lifecycle events.

## Retrieval configuration

`AI:KnowledgeBases:RepositoryHelp` owns:

- collection name;
- embedding provider and model;
- vector dimensions;
- chunk size and overlap;
- embedding batch size;
- document resource limits.

`AI:Agents:RepositoryHelp` owns:

- the knowledge-base reference;
- guard profile;
- `TopK`;
- minimum similarity;
- bounded additional-search count;
- maximum query length;
- deterministic metadata filters.

PostgreSQL connection configuration stays under
`AI:Retrieval:Postgres:ConnectionString`. The current database mapping uses
768-dimensional vectors, so another embedding dimension requires a compatible
schema migration or store.

## DevUI and inspection

`devui` hosts registered standalone agents and native workflows on a loopback
URL. It is a local development surface, not a production endpoint.

To run DevUI:

```bash
dotnet run --project src/MafPlayground.CLI -- devui
```

By default, open `http://localhost:5050/devui` after the host starts. Use
`--model` to select a chat model and `--url` to change the loopback URL.

`inspect list` lists registered entities. `inspect agent <id> --view-input`
prints an agent input schema and example. `inspect workflow <id> --diagram`
prints the native MAF graph as Mermaid.

DevUI tracing and external OTLP export are separate integrations. Sending spans
to Aspire Dashboard does not by itself populate DevUI's response trace panel.

## Observability and guards

The repository emits structured logs, traces, metrics, model usage, tool and
workflow events, and estimated model costs when pricing metadata is available.
Sensitive prompt, response, tool, and retrieved-document payloads are disabled
by default.

Reusable guard profiles enforce content handling and bounded budgets for model
calls, tool calls, tokens, and estimated cost. Retrieved RAG content is treated
as untrusted data rather than instructions.

## Build and tests

Run the repository verification script:

```bash
./scripts/verify.sh
```

The default suite contains deterministic unit and component tests. Tests that
call live Google, Ollama, or PostgreSQL infrastructure are opt-in integration
tests and are excluded from the fast default suite.

## Common failures

### A model selector is missing

Set `AI_MODEL` or pass `--model` using `provider:model` syntax.

### Google authentication fails

Set `GEMINI_API_KEY` or the equivalent .NET configuration value. Do not commit
the key to `appsettings.json` or the repository.

### Ollama is unavailable

Start the Ollama server and pull the configured model. Unknown Ollama embedding
models fail explicitly when no compatible tokenizer mapping exists.

### The repository help agent has no evidence

Confirm PostgreSQL is running, migrations were applied, and both repository-help
Markdown documents were ingested into `RepositoryHelp`. An empty or unrelated
knowledge base intentionally produces the fixed insufficient-evidence answer.

### A PDF produces little or no content

Check whether it has a selectable text layer. Image-only or scanned documents
need OCR, which is not currently provided by the PDF extractor.

### Documentation changed but answers remain old

Regenerate `cli-reference.md` when commands change, then re-ingest every changed
manual document. Content hashes make repeated ingestion safe.

## Trust and maintenance rules

- Do not ingest `.env`, secrets, credentials, `bin`, `obj`, or arbitrary working
  tree content into the repository-help knowledge base.
- Prefer this curated manual for conceptual behavior and the generated CLI
  reference for exact command syntax.
- Update the manual in the same change that modifies documented architecture or
  operational behavior.
- Regenerate the CLI reference whenever the command tree changes.
- If evidence is missing or contradictory, the agent must say so instead of
  inventing a command or implementation detail.

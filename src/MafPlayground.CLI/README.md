# MafPlayground.CLI

Development-only command-line harness and web host for running the repository's
agents and workflows locally.

The CLI is a composition root, not the reusable application core. It wires
together AI orchestration, Ollama, retrieval, PostgreSQL, observability, DevUI,
and local user context. A production web or worker host should compose the same
libraries independently.

## Command tree

```text
maf-playground
├── agent
│   ├── basic
│   └── basic-rag
├── workflow
│   └── translate
├── rag
│   ├── database
│   │   └── migrate
│   └── ingest
├── inspect
│   ├── list
│   ├── agent <id>
│   └── workflow <id>
└── devui
```

Discover options through System.CommandLine:

```bash
dotnet run --project src/MafPlayground.CLI -- --help
dotnet run --project src/MafPlayground.CLI -- agent basic --help
dotnet run --project src/MafPlayground.CLI -- inspect workflow --help
```

## Structure

| Area | Responsibility |
| --- | --- |
| `Program.cs` and `Parser.cs` | Entry point and root command composition. |
| `Commands/` | One file per command and command-specific host composition. |
| `InteractiveAgentConsole.cs` | Interactive/single-prompt agent harness and streaming. |
| `WorkflowExecutionConsole.cs` | Renders native workflow execution events for `--watch`. |
| `Inspection/` | Entity catalog, input schemas/examples, and Mermaid export. |
| `DevUI/` | Current-preview trace bridge into DevUI response streams. |
| `AIProviderCompositionExtensions.cs` | Registers enabled provider adapters. |
| `RetrievalCompositionExtensions.cs` | Registers retrieval core and current store adapter. |
| `LocalUserContextAccessor.cs` | Supplies machine-local context for development only. |
| `appsettings.json` | Non-secret local defaults and sample pricing. |

## Composition

Commands build only the services they need. Common registrations are:

```csharp
services.AddAIServices(modelSelection);
services.AddConfiguredAIProviders(configuration);
services.AddConfiguredRetrieval(configuration, embeddingSelection);
services.AddMafPlaygroundObservability(configuration);
```

The model and embedding selectors use `provider:model`. Provider endpoint
configuration remains owned by the provider adapter. PostgreSQL configuration
remains owned by its retrieval adapter.

## Configuration

The host reads `appsettings.json` and process environment variables. `.env` files
are not loaded automatically:

```bash
cp .env.example .env
set -a; source .env; set +a
```

Frequently used values are `AI_MODEL`, `AI_EMBEDDING_MODEL`, `DEVUI_URL`,
`AI__PROVIDERS__OLLAMA__ENDPOINT`, retrieval settings under `AI__RETRIEVAL`, and
observability settings under `OBSERVABILITY`. CLI options override model, prompt,
URL, input, and watch behavior where provided.

## Local testing surfaces

- Interactive agent commands keep a MAF session across prompts.
- `--prompt` runs once and exits.
- `--watch` streams sanitized agent or workflow lifecycle events.
- `inspect` lists entities, prints input schemas/examples, and exports native
  workflow Mermaid graphs.
- `devui` hosts agents and native workflows on loopback for execution, graph
  inspection, and response-linked traces.

DevUI is registered as a local development service. Do not expose it remotely
without authentication and network controls. Its current structured workflow
input limitation is isolated in the translation workflow's chat adapter.

## Exit behavior

- `0`: success; translation also requires all branches to validate.
- `1`: a valid workflow result contains one or more failed translation branches,
  or ingestion produced no chunks.
- `2`: invalid input/configuration or an unavailable registered provider.
- `130`: caller cancellation.

Infrastructure failures not explicitly converted by a command surface normally
propagate so they are not confused with grounded no-evidence answers.

## Tests

Parser, commands, interactive streaming, entity input rendering, workflow event
rendering, DevUI trace translation, and composition are covered in
[`MafPlayground.Tests`](../../tests/MafPlayground.Tests/README.md).


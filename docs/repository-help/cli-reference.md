# MafPlayground CLI reference

> Generated deterministically from the System.CommandLine command tree. Do not edit manually.

Use `dotnet run --project src/MafPlayground.CLI -- --help` for terminal help at any time.

## `maf-playground`

Microsoft Agent Framework playground CLI

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- [command]
```

Subcommands:

| Command | Description |
| --- | --- |
| `agent` | Run and test agents. |
| `workflow` | Run and test workflows. |
| `rag` | Manage the local RAG knowledge base. |
| `devui` | Run the local Agent Framework DevUI. |
| `docs` | Generate repository documentation artifacts. |
| `inspect` | Inspect local agents and workflows. |

### `maf-playground agent`

Run and test agents.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- agent [command]
```

Subcommands:

| Command | Description |
| --- | --- |
| `basic` | Run the Basic agent. |
| `basic-rag` | Run the grounded Basic RAG agent. |
| `repository-help` | Ask grounded questions about the repository and its CLI. |

#### `maf-playground agent basic`

Run the Basic agent.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- agent basic [options]
```

Options:

| Option | Required | Description |
| --- | --- | --- |
| `--model`, `-m` | no | Model selector in provider:model format. Falls back to AI_MODEL. |
| `--prompt`, `-p` | no | Run one prompt and exit. Omit to start an interactive session. |
| `--watch` | no | Show agent lifecycle and tool-call events while streaming. |

#### `maf-playground agent basic-rag`

Run the grounded Basic RAG agent.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- agent basic-rag [options]
```

Options:

| Option | Required | Description |
| --- | --- | --- |
| `--model`, `-m` | no | Chat model in provider:model format. Falls back to AI_MODEL. |
| `--prompt`, `-p` | no | Run one prompt and exit. Omit for an interactive session. |
| `--watch` | no | Show agent lifecycle events while streaming. |
| `--filter` | no | Require document metadata in key=value format. Repeat for multiple filters. |

#### `maf-playground agent repository-help`

Ask grounded questions about the repository and its CLI.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- agent repository-help [options]
```

Options:

| Option | Required | Description |
| --- | --- | --- |
| `--model`, `-m` | no | Chat model in provider:model format. Falls back to AI_MODEL. |
| `--prompt`, `-p` | no | Run one question and exit. Omit for an interactive session. |
| `--watch` | no | Show agent lifecycle events while streaming. |

### `maf-playground workflow`

Run and test workflows.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- workflow [command]
```

Subcommands:

| Command | Description |
| --- | --- |
| `translate` | Translate text concurrently and validate each result. |

#### `maf-playground workflow translate`

Translate text concurrently and validate each result.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- workflow translate [options]
```

Options:

| Option | Required | Description |
| --- | --- | --- |
| `--model`, `-m` | no | Model selector in provider:model format. Falls back to AI_MODEL. |
| `--text`, `-t` | no | Source text to translate. |
| `--languages`, `-l` | no | Comma-separated target language identifiers, for example es,fr,pt-BR. |
| `--watch` | no | Stream native workflow execution events to the terminal. |

### `maf-playground rag`

Manage the local RAG knowledge base.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- rag [command]
```

Subcommands:

| Command | Description |
| --- | --- |
| `database` | Manage the retrieval database. |
| `ingest` | Extract, chunk, embed, and index a document. |

#### `maf-playground rag database`

Manage the retrieval database.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- rag database [command]
```

Subcommands:

| Command | Description |
| --- | --- |
| `migrate` | Apply retrieval database migrations. |

##### `maf-playground rag database migrate`

Apply retrieval database migrations.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- rag database migrate
```

#### `maf-playground rag ingest`

Extract, chunk, embed, and index a document.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- rag ingest --path <value> --knowledge-base <value> [options]
```

Options:

| Option | Required | Description |
| --- | --- | --- |
| `--path` | yes | Document file to ingest. |
| `--source-root` | no | Optional root used to create stable relative source identifiers. |
| `--knowledge-base` | yes | Configured knowledge base to ingest into. |
| `--metadata` | no | Document metadata in key=value format. Repeat for multiple values. |

### `maf-playground devui`

Run the local Agent Framework DevUI.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- devui [options]
```

Options:

| Option | Required | Description |
| --- | --- | --- |
| `--model`, `-m` | no | Model selector in provider:model format. Falls back to AI_MODEL. |
| `--url` | no | HTTP URL for DevUI. Falls back to DEVUI_URL. |

### `maf-playground docs`

Generate repository documentation artifacts.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- docs [command]
```

Subcommands:

| Command | Description |
| --- | --- |
| `generate-cli-reference` | Generate the repository-help CLI reference from the live command tree. |

#### `maf-playground docs generate-cli-reference`

Generate the repository-help CLI reference from the live command tree.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- docs generate-cli-reference --output <value>
```

Options:

| Option | Required | Description |
| --- | --- | --- |
| `--output`, `-o` | yes | Markdown file to create or replace. |

### `maf-playground inspect`

Inspect local agents and workflows.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- inspect [command]
```

Subcommands:

| Command | Description |
| --- | --- |
| `list` | List locally registered agents and workflows. |
| `agent` | Inspect a local agent. |
| `workflow` | Inspect a local workflow. |

#### `maf-playground inspect list`

List locally registered agents and workflows.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- inspect list
```

#### `maf-playground inspect agent`

Inspect a local agent.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- inspect agent <id> [options]
```

Arguments:

| Name | Required | Description |
| --- | --- | --- |
| `id` | yes | The local agent identifier. |

Options:

| Option | Required | Description |
| --- | --- | --- |
| `--view-input` | no | Print the required input JSON Schema and an example. |

#### `maf-playground inspect workflow`

Inspect a local workflow.

Usage:

```text
dotnet run --project src/MafPlayground.CLI -- inspect workflow <id> [options]
```

Arguments:

| Name | Required | Description |
| --- | --- | --- |
| `id` | yes | The local workflow identifier. |

Options:

| Option | Required | Description |
| --- | --- | --- |
| `--view-input` | no | Print the required input JSON Schema and an example. |
| `--diagram` | no | Print the native MAF workflow graph as Mermaid. |


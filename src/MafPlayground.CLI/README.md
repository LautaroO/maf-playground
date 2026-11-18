# MafPlayground.CLI

Development-only command-line harness and web host for running the repository's
agents and workflows locally.

The CLI is a composition root, not the reusable application core. It wires
together AI orchestration, Google Gen AI, Ollama, retrieval, PostgreSQL,
observability, DevUI, and local user context. A production web or worker host
should compose the same libraries independently.

## Command tree

```text
maf-playground
├── agent
│   ├── basic
│   ├── basic-rag
│   └── repository-help
├── workflow
│   └── translate
├── rag
│   ├── database
│   │   └── migrate
│   └── ingest
├── docs
│   └── generate-cli-reference
├── inspect
│   ├── list
│   ├── agent <id>
│   └── workflow <id>
├── devui
├── serve
└── tui
```

Discover options through System.CommandLine:

```bash
dotnet run --project src/MafPlayground.CLI -- --help
dotnet run --project src/MafPlayground.CLI -- agent basic --help
dotnet run --project src/MafPlayground.CLI -- inspect workflow --help
```

## Terminal inspector

Open a terminal at the repository root:

```sh
dotnet run --project src/MafPlayground.CLI -- tui
```

The catalog lists all registered agents and workflows before a model is configured.
Select a row to see an example input before the first run or a duration chart
with the latest retained runs, newest first. Each bar is scaled to the longest
visible run for that entity. Rows show the latest status and elapsed time.
Run coverage counts entities with an observed run in the displayed history.
Press `l` to open the selected entity’s latest run.
Catalog keys `1`, `2`, and `3` switch between entities, running jobs, and recent failures.
Select a run with ↑/↓ and press Enter to inspect it. With an empty history, `n` opens the first available example.
Select an entity with ↑/↓ and Enter. Press Esc or `c` in an inspector to return to the
catalog. Opening that entity again restores its draft, selected view and run.
Local runs continue while you browse. Use `--entity ID` to open an inspector
directly. With `--input` alone, the first entity you open receives the file. Press `m` to choose a local
`provider:model` (paste with the terminal’s Paste command and press Enter to apply, or Esc to discard), or pass `--model` or set `AI_MODEL`. The local runtime builds
agents and workflows in process. An animated RUNNING banner appears on submission.
Each currently executing node has a yellow ▶ and remains visible as active when
selected. Concurrent branches can be active together. Edges describe possible
routes. Agent tool nodes appear when the agent invokes the corresponding tool.

| Key | Local action |
| --- | --- |
| `Ctrl+P` or `:` | Find actions by name, keyword or shortcut. Use ↑/↓ and Enter. Esc or Ctrl+P closes the palette. Ctrl+C quits, as does `q` with an empty search. From the catalog you can search for entities. |
| `4` | Open the invocation timeline. Select a bar to see timing, input, output and errors. Older captures can use an event-order scale, identified in the footer. |
| `j` | Switch between a readable result and raw JSON. Translation summaries include requested languages, validation and attempts. |
| `a` | Compare the saved suite on two local models or two Studio profiles. Review the run count before starting. Enter on a case opens the paired graphs. |
| `t` | Save, edit or load a named input, run saved cases and inspect suite results. |
| `g` | Replay the captured graph one event at a time using ↑/↓. |
| `c`, `v`, `i` | Choose an entity, read its typed input schema, or edit its JSON. Tab focuses Apply, Enter confirms, Esc discards. |
| `r`, `x` | Execute or cancel the current run. Use `--entity` and `--input <file.json>` for startup values. |
| `1`, `2`, `3` | Graph, event timeline, final result. In the timeline, use ↑/↓ and Enter to inspect an observed invocation. |
| ↑/↓, ←/→ | Select a node or follow an incoming/outgoing edge. |
| Tab or Enter, ←/→, `[` / `]` | Focus the inspector, switch input/output/error, or select another invocation. Narrow terminals show the focused inspector full-screen. |
| `e` | Jump to the next failed or rejected invocation. |
| `h`, `b` | Reopen a run from session history or compare the current capture to a baseline. Each local inspector retains its latest 12 archived runs. |
| `s`, `o` | Save a completed capture to a new JSON file or inspect a saved capture offline. Files include the submitted input, outputs and errors. Keep them with the data they describe. |
| `?`, `q` | Show shortcuts or quit and cancel local execution. |

On macOS and Linux, Ctrl+C interrupts the TUI even inside the model editor. It returns exit code 130. Use `x` in the inspector to cancel just the run.

The footer follows the focused pane. Open the action palette to see commands and the reason an unavailable action needs more evidence or a different run state.

### Using regression cases

Open an entity, press `i` and choose **Save as case** after editing the JSON.
Give it a name. Checks are optional. A case can check an output substring, expected
translation languages, the presence of a JSON Pointer (for example
`/translations/0/translatedText`), or a maximum invocation count for a node.
With a pointer, the text check applies to that subtree. On a translation result
without a pointer, each translated text must contain the substring.

Press `t` to load a case as the draft, run one case, or run the suite in sequence.
Running cases invokes the chosen model or host profile. Each remote case starts
without conversation history. Select a failed result to inspect the run and,
when the evidence identifies one, the failing invocation. Cases are saved on
the client per entity in the OS local application-data directory under
`maf-playground/cases`. Set `MAF_TUI_CASES_DIR` for a different case library.
Files contain the input and checks you explicitly saved.

Use `g` after a run to step through its recorded graph. Use `b` to compare two
runs. The two diagrams mark changed nodes with Δ. Select a node and press Enter
to see input, output and errors for each occurrence side by side. Replay and
comparison use retained evidence and do not make new model calls.

### Running saved cases in CI

Point the TUI at a project-owned library with `MAF_TUI_CASES_DIR` when saving cases you want CI to run. Keep its per-entity directory structure when sharing the library. Set `AI_MODEL` and the chosen provider configuration in CI, then run:

```sh
dotnet run --project src/MafPlayground.CLI -- cases run --entity translation-workflow --cases-dir ./cases --artifacts ./artifacts/maf
```

The command runs the same checks as the local TUI. Exit 0 means every case passed, 1 means the suite failed or stopped early, 2 indicates a setup or library error, and 130 indicates cancellation. Each failed run writes a new capture containing input, output, and invocation evidence. Press `o` in a local inspector to open it. Choose the artifact directory and retention policy accordingly.

### Connect to a Studio host

`serve` and `tui --host` ship in the same .NET CLI. The server runs the agents
and owns the catalog, profiles, sessions, jobs and retained results. On the
machine with the model configuration, start the API:

```sh
dotnet run --project src/MafPlayground.CLI -- serve --url http://127.0.0.1:5051
```

The host reads `AI_MODEL` or `--model provider:model`. Retrieval-backed agents
require the knowledge base configuration and `Studio__EnableRetrieval=true`.
A regular `dotnet publish src/MafPlayground.CLI -c Release` produces the same
host and client. Both run without Node or web assets.

For another machine, forward that same loopback address and port to preserve
the host's origin checks:

```sh
ssh -N -L 127.0.0.1:5051:127.0.0.1:5051 user@studio-machine
```

In the client terminal, supply the **API bearer token** printed by the host in
`MAF_STUDIO_TOKEN`. For example, in zsh:

```sh
read -rs 'MAF_STUDIO_TOKEN?Studio API bearer token: ' && export MAF_STUDIO_TOKEN
dotnet run --project src/MafPlayground.CLI -- tui --host http://127.0.0.1:5051
unset MAF_STUDIO_TOKEN
```

The remote workspace also accepts HTTPS for compatible authenticated hosts.
The remote catalog is populated by the selected host. Esc brings you back to
the list. Reopening an entity resumes its current run and session for that profile.
Use F9 for the Workspace, Run, and Inspect menus. Press `p` to select a profile,
`c` for the catalog, `s` for sessions, and `h` for history. The graph, events
and result use `1`, `2` and `3`. Edit with `i`, run with `r`, and cancel with
`x`. Open retained runs to inspect observed executor and
tool activity, failures, spans, payloads and available usage metrics. Use Run to
prepare a rerun or compare two runs on that host. Closing the remote client
disconnects the viewer; Run → Cancel execution explicitly requests cancellation.
Sessions and run history follow the selected server's lifetime and retention limits.

## Structure

| Area | Responsibility |
| --- | --- |
| `Program.cs` and `Parser.cs` | Entry point and root command composition. |
| `Commands/` | One file per command and command-specific host composition. |
| `Execution/InteractiveAgentConsole.cs` | Interactive/single-prompt agent harness and streaming. |
| `Execution/WorkflowExecutionConsole.cs` | Renders native workflow execution events for `--watch`. |
| `Inspection/` | Entity catalog, input schemas/examples, and Mermaid export. |
| `Documentation/` | Deterministic repository-help CLI reference generation. |
| `DevUI/` | Current-preview trace bridge into DevUI response streams. |
| `Extensions/AIProviderCompositionExtensions.cs` | Registers enabled provider adapters. |
| `Extensions/RetrievalCompositionExtensions.cs` | Registers retrieval core and current store adapter. |
| `Helpers/LocalUserContextAccessor.cs` | Supplies machine-local context for development only. |
| `appsettings.json` | Non-secret local defaults and sample pricing. |

## Composition

Commands build only the services they need. Common registrations are:

```csharp
services.AddAIServices(modelSelection);
services.AddConfiguredAIProviders(configuration);
services.AddConfiguredRetrieval(configuration);
services.AddMafPlaygroundObservability(configuration);
```

The chat model selector uses `provider:model`; for example,
`google:gemini-3.6-flash` or `ollama:llama3.1:8b`. Each configured knowledge base
owns its embedding selector in the same format. Gemini chat can use either
Ollama/Nomic embeddings or `google:gemini-embedding-2`; provider configuration
remains owned by its adapter, while PostgreSQL configuration remains owned by
its retrieval adapter.

## Configuration

The host reads `appsettings.json` and process environment variables. `.env` files
are not loaded automatically:

```bash
cp .env.example .env
set -a; source .env; set +a
```

Frequently used values are `AI_MODEL`, `GEMINI_API_KEY`, `DEVUI_URL`,
`AI__PROVIDERS__OLLAMA__ENDPOINT`, knowledge bases under `AI__KNOWLEDGEBASES`,
agent search policy under `AI__AGENTS`, store settings under `AI__RETRIEVAL`, and
observability settings under `OBSERVABILITY`. CLI options override model, prompt,
URL, input, and watch behavior where provided. `rag ingest` requires an explicit
`--knowledge-base`; embedding models are not overridden globally by commands.

RAG ingestion accepts repeatable `--metadata key=value` options. The Basic RAG
agent accepts repeatable `--filter key=value` options; they override its static
`MetadataFilters` for that CLI run. Multiple entries use AND semantics. DevUI
uses the static filters from `appsettings.json` because it hosts a shared agent
registration rather than one command-scoped instance.

`agent repository-help` uses the separately configured `RepositoryHelp`
knowledge base. Regenerate its exact command reference with
`docs generate-cli-reference`, then ingest that file and the curated manual under
`docs/repository-help/` before querying the agent. The default profile uses
multilingual `google:gemini-embedding-2` embeddings in the dedicated
`repository-help-multilingual-v1` collection and therefore requires
`GEMINI_API_KEY`; the selected chat model remains independent.

The command is a concrete host for the reusable `HelpAgent` in
`MafPlayground.AI`. This project supplies repository-specific instructions and a
`System.CommandLine` implementation of `IHelpCapabilityCatalog`; another CLI,
web UI, or product domain can supply its own definition and catalog. The catalog
tools return help evidence only and never execute the described capability. The
agent skips context tools for purely social turns and selects a knowledge or
capability tool before answering application questions.

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

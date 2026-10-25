# Your first agent

[Documentation home](README.md)

This path is for developers who know C# and dependency injection but are new to
agents. Start with a conversation that can use an exact clock; then explore the
architecture and choose a more involved solution.

## 1. Prepare the local example

### Why this walkthrough uses Ollama

Ollama runs downloaded models on your computer. It is a useful starting point
because local inference does not require a cloud account or API key, and you can
repeat experiments without a hosted API charge per request. Your machine still
supplies the memory, compute, storage, and electricity.

Using a local model also lets you explore tools, context, and orchestration
without sending model requests to a hosted inference service. This describes
the local setup below; remote endpoints, cloud models, external tools, and
telemetry have their own data flows. See the [official Ollama FAQ](https://docs.ollama.com/faq)
for local and cloud behavior.

The tradeoff is that speed and usable model size depend on your hardware.
Tool selection and structured-output quality also depend on the selected model;
a small local model's failure does not by itself identify an orchestration bug.
Use the same representative cases to evaluate another model or the existing
Google adapter when local resources or observed behavior do not meet your needs.

Ollama is the learning path's starting configuration. MAF owns orchestration,
and this repository's provider adapter supplies `IChatClient`, so choosing a
different supported provider does not require rewriting the agent or its tools.

### Install and start

Run commands from the repository root. Install the .NET SDK pinned by
[`global.json`](../global.json) and choose a supported chat provider. This
walkthrough uses the existing Ollama example so you can run a local model.
Install Ollama separately, start its service if it is not already running, and
download the example model:

```bash
ollama serve
```

In another terminal:

```bash
ollama pull llama3.1:8b
dotnet restore MafPlayground.slnx
```

The model download requires network access and sufficient local resources. The
selector `ollama:llama3.1:8b` is an example configuration, not a core application
requirement. The [Google adapter](../src/MafPlayground.Providers.Google/README.md)
is another supported option; it requires its own credentials and configuration.

The basic agent does not need Docker, PostgreSQL, an embedding model, or an OTLP
collector. Those services are used by later examples or optional observability.
This walkthrough passes the chat model explicitly, so loading `.env` is not
required. If you use `.env` for other settings, load it explicitly as described
in the [repository manual](repository-help/manual.md#local-prerequisites).

## 2. Run one request

```bash
dotnet run --project src/MafPlayground.CLI -- \
  agent basic --model ollama:llama3.1:8b \
  --prompt "What date and time is it for me?" --watch
```

The CLI supplies your machine's local time zone as trusted context. The model
should request `get_current_date_time` and use its returned values in the answer.
`--watch` shows lifecycle and tool-call events, but not tool arguments or results.
The wording varies by model; a fluent answer alone does not demonstrate that the
tool ran. Check for invocation events and compare the answer with your local
clock and time zone, allowing for elapsed time. Exact tool-result preservation
requires controlled inspection; the deterministic clock tests below verify the
tool's values independently of model wording.

For a conversation, omit `--prompt`:

```bash
dotnet run --project src/MafPlayground.CLI -- \
  agent basic --model ollama:llama3.1:8b --watch
```

Type `/exit` to end the interactive session.

If execution fails, check that the model is downloaded, Ollama is reachable, and
the [provider settings](../src/MafPlayground.Providers.Ollama/README.md) match your
environment. Model calls have configured timeouts and run budgets; local model
speed can affect whether the example completes within those limits.

## 3. Understand the pieces

- **Agent:** MAF coordinates the conversation and model-selected tool calls.
- **Model client:** `IChatClient` separates that behavior from provider SDKs.
- **Tool:** ordinary C# validates a time-zone ID and returns typed clock data.
- **Context provider:** supplies host-owned user context before model invocation.
- **Session:** holds conversational history; it is separate from user context
  and durable application data.
- **Host:** the CLI selects providers, builds dependencies, and renders results.

Read the [Basic agent solution](../src/MafPlayground.AI/Agents/BasicAgent/README.md)
for the request flow, source links, decisions, failure behavior, and reuse guide.
Then consult the [project map](../README.md#projects) and
[shared source layout](../src/MafPlayground.AI/README.md). Some source remains
linked into the newer layered projects; folder names alone do not define a
project boundary.

## 4. Verify behavior without a model

```bash
dotnet test tests/MafPlayground.Tests/MafPlayground.Tests.csproj \
  --filter 'FullyQualifiedName~BasicAgentTests|FullyQualifiedName~CurrentDateTimeToolTests'
```

These tests use controlled clients and clocks. They verify context injection,
tool contracts, exact time conversion, and guard behavior without running a
model. They do not measure whether your selected model understands a request or
chooses a tool correctly. See [Testing and evaluations](README.md#testing-and-evaluations)
for the distinction and the available suites.

## 5. Choose your next solution

- Explore the [translation workflow](../src/MafPlayground.AI/Workflows/Translation/README.md)
  when execution order, parallel branches, validation, and retries need explicit
  orchestration. A known process is a reason to use a workflow.
- Explore [Basic RAG](../src/MafPlayground.AI/Agents/BasicRagAgent/README.md) when
  answers need evidence from documents. This introduces embeddings, PostgreSQL,
  migrations, and explicit ingestion.
- Explore [repository help](repository-help/manual.md#repository-help-agent) when
  an agent should choose among bounded application-context tools.

Use the [solution catalog](README.md#solutions) for direct lookup. Before adapting
an example for a client, read its limitations: CLI and DevUI are development
surfaces, the sample HTTP authentication is development-only, and in-memory
sessions do not survive a process restart.

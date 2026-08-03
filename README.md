# maf-playground
This is a Microsoft Agent Framework (MAF) playground to discover ideas, apps, and learn how to build agents with MAF.

## Run the Basic agent

Start Ollama and make sure a model is available:

```bash
ollama serve
ollama pull llama3.1:8b
```

Run an interactive conversation:

```bash
dotnet run --project src/MafPlayground.CLI -- agent basic --model ollama:llama3.1:8b
```

Or run one prompt and exit:

```bash
dotnet run --project src/MafPlayground.CLI -- agent basic --model ollama:llama3.1:8b --prompt "Hello"
```

Models use a provider-qualified selector. For Ollama, use `ollama:<model>`, for example
`ollama:llama3.1:8b`. The selector can also be supplied through `AI_MODEL`.
Ollama's endpoint defaults to `http://localhost:11434` and can be overridden with
the provider-owned `AI__PROVIDERS__OLLAMA__ENDPOINT` variable.

For local configuration, copy `.env.example` to `.env` and load it into your shell:

```bash
cp .env.example .env
set -a; source .env; set +a
```

The application reads process environment variables; it does not load `.env`
files automatically.

## Observability

Agent instrumentation lives in `MafPlayground.AI`, while telemetry collection and
export live in the reusable `MafPlayground.Observability` project. The CLI is only
one host of those libraries; an ASP.NET Core or worker host can register the same
services without referencing the CLI.

Telemetry export is disabled by default. To export traces, metrics, and structured
logs to an OTLP-compatible collector such as the Aspire Dashboard, set:

```bash
export OBSERVABILITY__ENABLED=true
export OBSERVABILITY__SERVICENAME=maf-playground-cli
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
dotnet run --project src/MafPlayground.CLI -- agent basic --model ollama:llama3.1:8b
```

Prompts, responses, and tool payloads are excluded by default. Sensitive capture
can be enabled explicitly for a secured local environment with
`OBSERVABILITY__AGENTFRAMEWORK__ENABLESENSITIVEDATA=true`.

Future web or worker hosts can compose the reusable infrastructure with:

```csharp
services.AddAIServices(modelSelection);
services.AddMafPlaygroundObservability(configuration);
```

## Local infrastructure

The root `compose.yaml` provides development infrastructure shared by any local
host. It currently starts the standalone Aspire Dashboard and can be extended
later with PostgreSQL or other dependencies.

Copy the example environment, enable observability, and start the stack:

```bash
cp .env.example .env
# Set OBSERVABILITY__ENABLED=true in .env
docker compose up -d
set -a; source .env; set +a
dotnet run --project src/MafPlayground.CLI -- agent basic
```

Open the dashboard at `http://localhost:18888`. The local stack accepts OTLP/gRPC
on `http://localhost:4317` and OTLP/HTTP on `http://localhost:4318`.

Anonymous dashboard access is enabled only for local convenience. Set
`ASPIRE_DASHBOARD_ALLOW_ANONYMOUS=false` to use its login-token flow instead.
Telemetry is held in memory by the standalone dashboard and is lost when the
container restarts.

Stop the local infrastructure with:

```bash
docker compose down
```

# Codex starter for Microsoft Agent Framework on .NET

## Repository layout

```text
AGENTS.md
.agents/
  skills/
    maf-architecture/
    maf-implementation/
    maf-review/
.codex/
  config.toml.example
docs/
  codex-setup.md
```

## Important distinction

- `AGENTS.md`: durable instructions committed with the repository.
- `.agents/skills/`: repository-scoped Codex skills.
- `~/.codex/AGENTS.md`: optional personal instructions applied across repositories.
- `~/.codex/config.toml`: personal Codex configuration.
- `.codex/config.toml.example`: documentation only; copy settings manually to the user-level configuration if desired.

Codex currently discovers repository skills from `.agents/skills`, not `.codex/skills`.


## Non-negotiable provider neutrality

This starter treats Azure, Microsoft Foundry, Azure OpenAI, and OpenAI as optional concrete integrations only.

The core architecture must remain portable across providers and clouds. Provider SDKs belong in adapters and composition roots; agents, workflows, tools, validators, prompts, persistence contracts, and core tests must remain provider-neutral.

## Installation

Copy `AGENTS.md` and `.agents/` to the root of your .NET repository.

Optionally copy settings from `.codex/config.toml.example` into `~/.codex/config.toml`.

Start a new Codex session in the repository and run:

```text
Summarize the AGENTS.md instructions and list the available MAF skills.
```

Then invoke a skill explicitly when useful:

```text
$maf-architecture Design the translation workflow.
$maf-implementation Implement the approved design.
$maf-review Review the current MAF implementation.
```

Skills can also activate implicitly from their descriptions.

## Customization

Replace generic build commands with the exact commands used by your solution and CI.

Add nested `AGENTS.override.md` files only when a subdirectory genuinely needs different rules.

Keep the root `AGENTS.md` stable and concise. Put task-specific procedures and longer references into skills.

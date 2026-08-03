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

The Basic agent exposes the reusable `get_current_date_time` operation. It accepts
an IANA or system time-zone identifier and returns the exact current date, time,
weekday, resolved time-zone ID, and UTC offset.

The CLI supplies `TimeZoneInfo.Local.Id` as its local-development user context.
The context contract is a generic key/value bag and a MAF context provider adds
its values per invocation, so future hosts can supply other trusted fields without
expanding the Basic agent prompt. A web host should replace
`IUserContextAccessor` with a request-aware implementation rather than using the
server's local time zone.

For local configuration, copy `.env.example` to `.env` and load it into your shell:

```bash
cp .env.example .env
set -a; source .env; set +a
```

The application reads process environment variables; it does not load `.env`
files automatically.

## Run DevUI

The same CLI executable can host the Agent Framework DevUI for local visual
testing. Load the environment and run:

```bash
set -a; source .env; set +a
dotnet run --project src/MafPlayground.CLI -- devui
```

Open `http://localhost:5050/devui`. Override the model with `--model` or the
listening address with `--url`. DevUI reuses the same agents, providers, and
observability pipeline as the terminal harness, and is restricted to local
development use.

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

Estimated model-call cost can also be emitted as the
`maf_playground.gen_ai.cost` metric and the `maf_playground.gen_ai.cost`
span attribute. Prices use the common provider convention of currency units per
one million input or output tokens:

```text
cost = (input tokens × input rate + output tokens × output rate) / 1,000,000
```

Each provider owns how its prices are represented and exposes normalized pricing
through a provider-neutral contract. The host supplies the actual values. The
CLI's `appsettings.json` configures a synthetic USD 0.01-per-million rate under
`AI:Providers:Ollama:Pricing`, even though a local Ollama call has no provider
charge. Set both rates to `0`, or add model objects to that provider's `Models`
array, to represent other pricing. Environment variables can still override
individual settings when a deployment requires it. An estimate is emitted only
when both a matching price and provider-reported input/output usage are
available; it is not an invoice or an authoritative billing record.

Future web or worker hosts can compose the reusable infrastructure with:

```csharp
services.AddAIServices(modelSelection);
services.AddMafPlaygroundObservability(configuration);
```

The host must also register an `IUserContextAccessor`. The CLI's
`AddLocalUserContext()` implementation is intended only for local development;
request-based hosts should provide their own registration.

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

# maf-playground
This is a Microsoft Agent Framework (MAF) playground to discover ideas, apps, and learn how to build agents with MAF.

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


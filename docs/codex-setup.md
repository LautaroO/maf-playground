# Codex setup

## 1. Add repository guidance

Place `AGENTS.md` at the Git repository root.

Codex reads instruction files from the repository root down to the current working directory. A closer `AGENTS.override.md` takes precedence for that directory.

## 2. Add repository skills

Place skills under:

```text
.agents/skills/<skill-name>/SKILL.md
```

Codex scans `.agents/skills` from the current directory toward the repository root.

## 3. Optional global configuration

Personal configuration belongs in:

```text
~/.codex/config.toml
~/.codex/AGENTS.md
```

Do not commit personal secrets or machine-specific configuration.

## 4. Verify discovery

Launch a new Codex session at the repository root and ask:

```text
List the instruction files and skills you loaded.
```

For CLI verification of instructions:

```bash
codex --ask-for-approval never "Summarize the current instructions."
```

## 5. Recommended usage

Use `maf-architecture` before substantial design changes.

Use `maf-implementation` after the architecture is clear or for localized MAF changes.

Use `maf-review` for pull requests, refactors, and architecture assessments.

## 6. Keep versions aligned

Record MAF NuGet package versions with central package management or project files. The skill instructs Codex to inspect installed versions before relying on current online samples.

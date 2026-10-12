# Codex setup

## Shared MAF guidance

Project rules and context live in `AGENTS.md` and existing project documentation.
Shared skills and references come from `maf-dotnet@lautaro-plugins`; there are no
local copies. Reserve `.agents/skills/<skill-name>/SKILL.md` for repeatable workflows
specific to maf-playground. There are currently no repository-specific skills.

## Installation

The tracked `.codex/config.toml` declares the marketplace and enables the plugin;
it does not install it. Each developer installs it in their Codex environment.

Check registered marketplaces:

```sh
codex plugin marketplace list
```

If `lautaro-plugins` is absent:

```sh
codex plugin marketplace add https://github.com/LautaroO/lautaro-plugins.git --sparse .agents/plugins
```

If already registered, refresh it instead:

```sh
codex plugin marketplace upgrade lautaro-plugins
```

Then install and inspect:

```sh
codex plugin add maf-dotnet@lautaro-plugins --json
codex plugin list --json
```

Installation belongs to the user's Codex environment, not exclusively to this
repository. Keep personal settings, credentials, and machine-specific trust
configuration outside source control.

## Verify discovery

Open the repository as a trusted project in Codex and start a fresh session.
Confirm the plugin is enabled and all fourteen skills are discoverable, including
`maf-adoption`, `maf-architecture`, and `maf-review`. Select skills using the
installed plugin namespace shown by the client. A cached installation alone does
not prove discovery in a running session.

## Usage and versions

Use `maf-architecture` when responsibilities or orchestration are unclear, then
the matching implementation skill such as `maf-agents`, `maf-tools`, or
`maf-workflows`. Use `maf-review` for reviews and `maf-adoption` for guidance updates.

Repository rules remain authoritative. Inspect `Directory.Packages.props` before
relying on plugin examples: this application uses MAF 1.16.0, independently of
the plugin version. Do not upgrade packages just to match plugin documentation.

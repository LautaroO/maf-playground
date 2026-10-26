# MafPlayground.Studio.IntegrationTests

Real-process loopback smoke checks for the existing CLI Studio command. They use
an ephemeral local port, clear model/provider credential overrides from the child
environment, and require no model or database. The test verifies browser bootstrap,
authenticated disabled discovery, the API-only page and graceful Unix shutdown.
The in-memory HTTP failure matrix remains in `MafPlayground.Studio.Tests`.

Run `dotnet test tests/MafPlayground.Studio.IntegrationTests` from the repository root.

Exactly two opt-in `Category=StudioTransport` checks run against real loopback
Kestrel with an in-process fake model: the official MCP client and HTTP share one
accepted run; an SSE viewer reconnects to that accepted job without resubmission.
The existing CLI process startup/shutdown smoke stays in this owning project.

```bash
RUN_STUDIO_INTEGRATION_TESTS=true dotnet test tests/MafPlayground.Studio.IntegrationTests --filter Category=StudioTransport
```

Startup checks isolate the built CLI without assets, verify actionable API-only
operation, sanitized invalid configuration, missing-model availability, configured
request limits, and graceful shutdown. These checks remain in the default gate.

`Category=StudioPackaging` is explicitly enabled by `STUDIO_PUBLISH_DIRECTORY`.
Prefer `./scripts/build-studio.sh` then `./scripts/verify-studio-package.sh`: the
script publishes the real CLI to a temporary directory, tests it and removes it.
The process resolves the .NET executable before clearing runtime PATH, so Node
cannot participate. Tests verify all bundled scripts/workers and fonts, required
license notices, authenticated routes/deep links, narrow CSP, foreign-Origin
denial and 404s for absent files. The test fixture is absent from publish output.

`LiveModel/` has two separately opted-in execution smoke checks for native
agent/date-tool and translation paths. They use the existing CLI provider
composition and generic Studio executor, require no semantic dataset/judge, and
are skipped by default:

```sh
RUN_STUDIO_LIVE_MODEL_TESTS=true AI_MODEL=provider:model \
  dotnet test tests/MafPlayground.Studio.IntegrationTests --filter Category=StudioLiveModel \
  -m:1 -p:UseSharedCompilation=false -nr:false
```

Supply provider-specific credentials/configuration through the existing environment
conventions. Missing `AI_MODEL` is explicitly skipped as unconfigured; an opted-in
configured but unreachable/unsupported provider fails rather than pretending to pass.
These tests collect local evidence but do not register an external exporter. They
do not validate Aspire delivery. For a configured collector, separately compare a
trace ID from the running Studio CLI with Aspire and verify external prompt/tool
payloads stay absent under the default sensitive-export setting. Report missing
provider/collector availability separately; never print credentials in test evidence.

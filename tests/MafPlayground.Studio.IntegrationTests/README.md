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

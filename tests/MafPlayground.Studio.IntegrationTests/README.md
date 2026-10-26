# MafPlayground.Studio.IntegrationTests

Real-process loopback smoke checks for the existing CLI Studio command. They use
an ephemeral local port, clear model/provider credential overrides from the child
environment, and require no model or database. The test verifies browser bootstrap,
authenticated disabled discovery, the API-only page and graceful Unix shutdown.
The in-memory HTTP failure matrix remains in `MafPlayground.Studio.Tests`.

Run `dotnet test tests/MafPlayground.Studio.IntegrationTests` from the repository root.

# MafPlayground.Studio.Tests

Deterministic unit/component tests for Studio discovery, typed schemas/examples,
optional capabilities, unavailable entities, and native MAF workflow topology.
They use normal runtime registrations and unknown test-assembly contracts; no
provider credentials, model/tool calls, or network services are required.

Run `dotnet test tests/MafPlayground.Studio.Tests` from the repository root.
External transport checks belong in the separate Studio integration project when
HTTP/MCP transport is introduced. Existing runtime and CLI regressions stay in
`MafPlayground.Tests`.

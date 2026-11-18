# MafPlayground.Studio.Tests

Run `dotnet test tests/MafPlayground.Studio.Tests` for the fast Studio HTTP API suite. TestServer hosts the actual middleware and routes. Recording model clients and synthetic entities exercise the MAF runtime without provider credentials or external services.

The contract tests cover bearer authentication, local access checks, error sanitization, profile discovery, admission, retained results and SSE replay. Component tests exercise execution, topology, profile isolation, sessions and telemetry correlation. Unit tests exercise cancellation, lifecycle transitions, bounded retention, capture privacy and run comparison.

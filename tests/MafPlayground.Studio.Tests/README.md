# MafPlayground.Studio.Tests

Deterministic unit/component tests for Studio discovery, typed schemas/examples,
optional capabilities, unavailable entities, native MAF workflow topology, and
bounded run supervision/journal retention. Lifecycle tests use controllable fake
executors, async DI scopes and a manual TimeProvider for cancellation, timeout,
shutdown, retry, session isolation, capture pressure and subscriber gaps.
They use normal runtime registrations and unknown test-assembly contracts; no
provider credentials, model/tool calls, or network services are required.

Run `dotnet test tests/MafPlayground.Studio.Tests` from the repository root.
External transport checks belong in the separate Studio integration project. Existing runtime and CLI regressions stay in
`MafPlayground.Tests`.


Component tests exercise the generic typed executor with unknown agent/workflow
contracts and actual MAF sample behavior under controlled clients. They cover real
date/help tool invocation, one-invocation streaming, concurrent chat profiles,
grounded repair, translation validation and partial outcomes, model-free native
inspection, effective-profile snapshots, scope disposal, session continuity,
reset, overlap, profile mismatch, and failure/cancellation rollback. No live model, external HTTP service, MCP server, or browser is required.

Capture tests cover admission/result/error redaction, Unicode/size/depth bounds,
serialization failure containment, raw-envelope release, real model/tool exchanges,
stream completion, external sensitive-telemetry isolation, native workflow payloads,
parallel/repeated invocation identity and late spans. Profile scope tests verify
RAG/Help generated telemetry resources are disposed after success and failure
without constructing unused agents. The disposal assertion inspects the pinned
MAF 1.16 telemetry resource, not a production reflection dependency.

HTTP contract tests use real minimal API handlers and `TestServer` with the real
catalog/run supervisor and deterministic registered entities/fake model clients.
`Microsoft.AspNetCore.TestHost` 10.0.10 is test-only. Tests cover authentication,
bootstrap, retained queries, SSE reconnect/gaps/disconnect, explicit cancellation,
profile/session isolation and reset, bounded admission, capture lookups/redaction,
input limits, disabled discovery and stable errors. Actual CLI process/loopback
startup is isolated in `MafPlayground.Studio.IntegrationTests`.

The HTTP fixture explicitly uses the Development environment. Focused access
regressions cover missing/malformed query binding, invalid credentials, foreign
Host/Origin, bearer/cookie authentication and a throwing endpoint dependency.
They verify denial before activation and fixed ProblemDetails without credentials
or development diagnostics. The process smoke test repeats the missing-query
regression against the actual CLI with no credential, bearer and exchanged cookie.

`Contract/StudioMcpContractTests.cs` uses the official MCP SDK client over TestServer
for handshake/discovery, typed and malformed input, bearer-only/local access,
dynamic unknown entity contracts/native topology, profile scope isolation, bounded
list/event/span/payload pages, redaction/result limits, cross-transport request
idempotency (including eviction), cancellation and shared session reset/history.
No external model is called.

Span-page contracts cover oversized/unavailable capture, mixed pages and malformed
DTOs without fabricating spans. `Unit/StudioMcpToolsTests.cs` deterministically
checks cancellation receipts after snapshot eviction and preservation of existing
journal gaps/cursors.

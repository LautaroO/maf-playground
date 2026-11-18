# MafPlayground.Studio

Studio is the development-only HTTP API used by the terminal workspace. The CLI hosts it alongside the normal application runtime and configured provider adapters. The catalog discovers entities from runtime registrations. JSON schemas use the same serializer options as typed execution, and workflow topology comes from the registered native MAF workflow.

## Access

Run `dotnet run --project src/MafPlayground.CLI -- serve` and copy the host's bearer token into the client. Use the exact loopback authority printed at startup. Requests require an `Authorization: Bearer <token>` header, a loopback peer, a matching Host header and, when present, a matching Origin. Restarting the host rotates the credential and clears retained runs and session handles.

Custom hosts register `AddStudio(configuration)`, install `UseStudioAccess()` before endpoint handling, and call `MapStudio()`. The host supplies the local caller's permissions. Run and session creation enforce the target entity's authorization policy.

## HTTP contract

All paths below are relative to `/api/studio/v1`.

| Operation | Route |
| --- | --- |
| Read host identity and source provenance | `GET /instance` |
| Discover entities, schemas and availability | `GET /entities` with optional `profileId` |
| Read configured execution profiles | `GET /profiles` |
| Inspect workflow topology | `GET /topology?entityId=…&profileId=…` |
| List or create an explicit session | `GET /sessions`, `POST /sessions` |
| Reset session history and return a fresh handle | `POST /sessions/{id}/reset` |
| Submit a run, list retained runs, or read one snapshot | `POST /runs`, `GET /runs`, `GET /runs/{id}` |
| Read a terminal result or request cancellation | `GET /runs/{id}/result`, `POST /runs/{id}/cancel` |
| Page or stream the journal | `GET /runs/{id}/event-page?after=…&limit=…`, `GET /runs/{id}/events` |
| Inspect captured evidence | `GET /runs/{id}/spans/{spanId}`, `GET /runs/{id}/payloads/{payloadId}` |
| Compare two terminal runs retained by the same host | `POST /comparisons` |

A run request supplies a request ID, entity ID, profile ID, JSON input and optional session ID. Admission returns HTTP 202 and a receipt. Repeating the request ID with identical parameters returns the original receipt. Reusing it with different parameters returns HTTP 409. A session handle binds turns to its original entity and profile.

The event stream uses server-sent events. Resume from the last observed sequence with the `after` query parameter or `Last-Event-ID` header. Closing the connection ends observation while accepted work continues. When journal retention creates a gap, reload the snapshot for the current state.

## Execution and retention

The run supervisor uses bounded worker and queue capacity. Accepted jobs execute in fresh DI scopes through the application's provider-neutral runtime. A job owns its cancellation token through completion. Defaults allow four concurrent runs, 16 waiting jobs, a five-minute run timeout and ten seconds for graceful shutdown.

Configure retention and capture via `Studio:Limits`. Defaults retain 100 completed runs, up to 2,048 journal events per run, 128 MiB of captured data across runs and 2 MiB per payload. The store also bounds request metadata. When older captures are evicted, retained request IDs continue protecting against duplicate execution for the host lifetime. Captured payloads are redacted and can be truncated. Comparison uses retained evidence and reports missing observations explicitly.

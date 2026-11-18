# MafPlayground.Studio.IntegrationTests

Run `dotnet test tests/MafPlayground.Studio.IntegrationTests` for CLI startup, authenticated discovery, sanitized configuration errors, request limits and shutdown. The tests launch the headless host on loopback without a configured model.

Set `RUN_STUDIO_INTEGRATION_TESTS=true` to include real Kestrel HTTP and SSE checks with a recording model. They cover idempotent submission and event replay after reconnecting.

To check a published CLI, run these commands from the repository root.

```sh
publish_dir=$(mktemp -d)
dotnet publish src/MafPlayground.CLI -o "$publish_dir"
STUDIO_PUBLISH_DIRECTORY="$publish_dir" dotnet test tests/MafPlayground.Studio.IntegrationTests --filter Category=StudioPackaging
```

The published-process check verifies bearer authentication, foreign-origin rejection and the absence of bundled Studio web assets.

Real-provider smoke tests require `RUN_STUDIO_LIVE_MODEL_TESTS=true`, `AI_MODEL=provider:model` and the selected provider's configuration. Run them with `--filter Category=StudioLiveModel`. They exercise date-tool execution and native translation with local event capture.

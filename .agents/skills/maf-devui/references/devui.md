# DevUI and local MAF testing

## Do not conflate these components

| Surface | Purpose |
|---|---|
| Plain CLI harness | Repository-owned UX for conversational/typed local tests. |
| `HarnessAgent` | Opinionated MAF agent runtime with additional operational capabilities. |
| Harness sample terminal UX | Example console around a harness agent; not a universal console framework. |
| DevUI | Local web/API surface for discovering, running, visualizing, and debugging agents/workflows. |
| OTLP backend | External collector/viewer for logs, metrics, and traces. |

Keep CLI UX and DevUI in development hosts. A harness agent may be a real application runtime only when its capabilities are intentionally required.

## Verify the .NET version first

Before implementation:

1. inspect installed `Microsoft.Agents.AI.DevUI`, hosting, and workflow package versions;
2. inspect XML/public APIs;
3. compare with matching official source/tag;
4. treat Python-only DevUI documentation as conceptual until confirmed for .NET.

## Register entities intentionally

Current .NET patterns use:

- `AddAIAgent` for standalone agents;
- `AddWorkflow` for native graph workflows so DevUI can discover/visualize topology;
- workflow-as-agent registration only when agent semantics are independently needed.

Do not register the same logical workflow both natively and as an agent unless two entities are intentional.

Put reusable identity/description on the agent/workflow itself (for example workflow builder name/description APIs supported by the installed version), not only in DevUI host glue.

Verify entity discovery using the actual DevUI endpoint such as `/v1/entities` when that endpoint exists in the installed package.

## Execution protocol

A workflow that accepts a typed application input is not automatically compatible with the chat/OpenAI-compatible protocol exposed by DevUI.

Preserve the typed core contract and add a thin host/entry adapter only when needed. Do not stringify internal workflow state just to satisfy the UI.

Test both:

- native in-process workflow execution;
- the DevUI hosted request path (for example `/v1/responses` with the installed entity protocol).

## Tracing

External OTLP export and DevUI trace rendering are separate paths. DevUI cannot display spans that were never emitted/delivered to it.

Do not assume an OTLP exporter sends traces "back" to DevUI. Verify:

- instrumentation exists;
- DevUI's installed trace collector/bridge path;
- external OTLP export independently;
- sensitive-data settings.

## Security

Bind DevUI to loopback by default. If remote access is explicitly required, add authentication/network policy. Do not expose it as a production endpoint merely because it can execute agents/workflows.

## Verification checklist

1. Build/tests pass.
2. DevUI host starts.
3. Entity discovery returns each logical entity once.
4. Agent/workflow names and descriptions are correct.
5. Native workflow graph is visible when expected.
6. Every registered entity executes through the UI/API path.
7. Cancellation/provider errors behave correctly.
8. External telemetry and DevUI traces are verified separately.
9. DevUI is not remotely exposed by accident.

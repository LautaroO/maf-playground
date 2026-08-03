# DevUI and local MAF testing

Use this reference for C# DevUI hosting, workflow visualization, interactive testing, and DevUI trace integration.

## Establish the exact surface

Do not conflate these components:

| Surface | Purpose |
| --- | --- |
| Plain CLI harness | Repository-owned UX for conversational or typed local tests. |
| `HarnessAgent` | Opinionated MAF agent runtime with tools, context management, approvals, and telemetry. |
| Harness sample console | Sample terminal UX around a harness agent; not a shipped general-purpose console framework. |
| DevUI | Local web application and OpenAI-compatible API for discovering, running, visualizing, and debugging agents and workflows. |
| Aspire Dashboard or another OTLP backend | Collector/viewer for application logs, metrics, and distributed traces. |

Keep the plain CLI UX, harness sample console, and DevUI in a development host or command. `HarnessAgent` may be a reusable runtime when its capabilities are part of the application design; do not introduce it merely to obtain a console UX. Do not make a reusable AI/core project depend on DevUI or a specific console implementation.

## Verify the .NET version first

DevUI evolves quickly and its C# Learn pages may lag or say that C# guidance is coming soon. Before implementing:

1. inspect installed versions of `Microsoft.Agents.AI.DevUI`, `Microsoft.Agents.AI.Hosting`, and workflow packages;
2. inspect their XML documentation and public APIs;
3. compare with the matching tag or current official .NET source;
4. treat Python DevUI docs as conceptual only until each behavior is confirmed for .NET.

In particular, do not assume structured workflow-input introspection, trace capture, directory discovery, or event names behave identically across runtimes and preview versions.

## Register entities intentionally

- Register standalone agents with `AddAIAgent`.
- Register graph workflows with `AddWorkflow` so DevUI can discover and visualize the native executor graph.
- Do not call `AddAsAIAgent` merely to make a native workflow executable in DevUI. It creates another agent registration and can show the same logical workflow twice.
- Use workflow-as-agent only when another agent or an agent-only API must invoke the workflow as an `AIAgent`.
- Align the DI registration key, `Workflow.Name`, and user-facing identity unless distinct names are deliberate.
- Set reusable metadata while building the entity: agent name/description on the agent, and `WithName` plus `WithDescription` on `WorkflowBuilder`.

DevUI entity discovery reads registered `AIAgent` and `Workflow` services. Verify `GET /v1/entities` rather than inferring discovery from successful startup. The current .NET implementation obtains workflow descriptions from `Workflow.Description` and graph data from the native workflow.

## Design the execution protocol

A workflow that runs through an internal typed runner is not automatically compatible with DevUI's OpenAI Responses endpoint.

1. Preserve a typed core input/result contract for application and CLI execution.
2. Add a thin host adapter or entry executor when DevUI supplies chat-protocol input.
3. Emit the response type expected by the installed hosting integration; do not stringify intermediate state prematurely.
4. Test the exact DevUI path, not only the core runner.

For a native workflow, test at least:

- in-process execution with the same input protocol DevUI sends;
- final output shape and failure events;
- an HTTP request to `/v1/responses` with `metadata.entity_id` when practical.

If structured input is required, inspect the installed .NET entity schema implementation first. Do not rely on Python documentation claiming automatic first-executor schema reflection unless the C# package actually exposes it.

## Treat observability as two paths

MAF instrumentation and telemetry destinations are separate concerns.

### External observability

- Enable agent/chat-client instrumentation and workflow instrumentation such as `WithOpenTelemetry` where supported.
- Subscribe the host's OpenTelemetry provider to the relevant `ActivitySource` names.
- Export logs, metrics, and traces with OTLP to Aspire Dashboard or another backend.

### DevUI trace panel

DevUI does not create missing agent/workflow spans. It can only display trace data delivered through the mechanism supported by the installed backend version.

- Do not assume `AddOtlpExporter` sends data back into DevUI; OTLP normally targets an external collector.
- Inspect the installed DevUI/hosting implementation for its trace collector or response event contract.
- If the .NET preview lacks an in-process collector, isolate any response-stream trace bridge inside the DevUI host and label it preview-specific.
- Keep sensitive prompt/tool payload capture opt-in.
- Verify external OTLP export and DevUI trace rendering independently.

For tool visibility, confirm both the agent's registered `ChatOptions.Tools` metadata and actual tool-call trace spans. A tool executing successfully does not guarantee that discovery metadata or the debug panel can see it.

## Hosting and security

- Add DevUI services and the OpenAI Responses/Conversations hosting services required by the installed package, then map their endpoints in the order expected by that version.
- Default to loopback-only URLs.
- Treat DevUI as a development tool, not a production agent endpoint.
- If remote access is explicitly required, configure authentication and network policy; never rely only on an obscure URL.
- Keep provider setup in provider registration extensions and resolve agents/workflows through DI.

## Verification checklist

1. Build and run focused unit/workflow tests.
2. Start the DevUI host and request `/v1/entities`.
3. Confirm each logical component appears once with the intended type, name, description, tools, and executors.
4. Execute every registered entity through `/v1/responses` or the UI.
5. Confirm workflow topology and final output.
6. Confirm agent, model, tool, workflow, and executor spans in every configured destination.
7. Test cancellation and provider errors through the hosted path.
8. Confirm DevUI remains unavailable from non-loopback interfaces unless remote access was explicitly secured.

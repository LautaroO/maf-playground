# Agent pipeline layers

Current MAF docs describe `ChatClientAgent` as a layered pipeline:

1. agent middleware;
2. context/history layer;
3. `IChatClient` pipeline, which can itself include middleware and function invocation behavior.

This matters because a concern should intercept the smallest meaningful surface.

Examples:

- redact user input before any agent processing → agent middleware;
- deny a tool argument based on tenant policy → function middleware/tool boundary;
- record model latency/token metadata → `IChatClient` instrumentation/middleware;
- inject user profile/runbook context → context provider, not middleware;
- deterministic multi-step process → workflow, not middleware.

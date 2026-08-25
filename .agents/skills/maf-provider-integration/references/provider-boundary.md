# Provider boundary

A useful project shape is:

```text
Application / Domain
    ↓
MAF Agents + Workflows + Tools
    ↓
Microsoft.Extensions.AI / MAF-neutral contracts
    ↓
Infrastructure.ProviderX
    ↓
Provider SDK
```

Composition root responsibilities:

- bind provider/model configuration;
- create authenticated provider client;
- adapt to `IChatClient` or provider-specific `AIAgent` implementation;
- decorate with supported middleware/telemetry;
- register agent/workflow factories.

Do not pass provider SDK request/response types into workflow messages or tools.

Provider-specific hosted tools are capabilities, not universal MAF primitives. If used, isolate them behind a feature boundary and make fallback/unsupported behavior explicit.

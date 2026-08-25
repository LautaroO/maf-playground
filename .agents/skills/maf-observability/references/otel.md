# OpenTelemetry notes

MAF integrates with OpenTelemetry / GenAI semantic conventions. Current C# docs show instrumentation on provider-neutral chat clients via builder extensions such as `UseOpenTelemetry(...)` and agent/workflow instrumentation through their supported APIs.

Exact source names/extensions evolve. Inspect installed packages before hardcoding them.

Verification should answer separately:

1. are spans/metrics/logs being created?;
2. is the host subscribed?;
3. is the exporter configured?;
4. does the target backend receive them?;
5. does DevUI receive/show traces if DevUI is being used?

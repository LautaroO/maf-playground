# Workflow state, checkpoints, HITL

## State

Prefer typed messages. Use workflow shared state only for data that multiple executors genuinely need outside direct message flow.

Current workflow state APIs are scope-aware and visibility can follow workflow superstep semantics. Verify `ReadStateAsync` / queued update APIs against the installed package before relying on same-step visibility.

Never share mutable executor/workflow instances across concurrent runs accidentally. If a stateful executor is intentionally reused, inspect whether the installed version requires/supports reset semantics such as `IResettableExecutor`.

## Checkpoints

Checkpoints capture workflow runtime state so execution can resume from a saved point. They are useful for long workflows, pause/resume, failure recovery, and audit/replay scenarios.

Checkpoint state is not a substitute for domain/application persistence.

## HITL / RequestPort

Current MAF workflow docs use request/response handling (`RequestPort` in C#) for interactions with external systems/humans. The workflow emits a request event and waits; the outside host returns a typed response and execution resumes.

Use this for approval or required human/external data. Persist pending requests/checkpoints when the wait may outlive the process.

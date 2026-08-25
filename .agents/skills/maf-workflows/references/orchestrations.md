# Built-in multi-agent orchestrations

Before building a custom graph, check whether a current MAF orchestration already matches the collaboration semantics.

Current Agent Framework documentation includes these orchestration patterns:

| Pattern | Use when | Do not use merely for |
|---|---|---|
| Sequential | participants must run one after another and pass conversational output forward | deterministic validation/retry branches that need typed process control |
| Concurrent | several participants independently process the same input and results are aggregated | arbitrary parallel C# work that is not an agent/workflow concern |
| Handoff | an active agent transfers conversational ownership to another specialist | parent-agent delegation where control must return to the parent; use agent-as-tool for that |
| Group Chat | several agents iteratively collaborate under a speaker-selection manager | a fixed deterministic pipeline |
| Magentic | a manager dynamically plans and coordinates specialists for complex open-ended work | ordinary routing or predictable business processes |

In current .NET docs these are exposed through workflow/orchestration builders such as `AgentWorkflowBuilder` and specialized builders. Exact builder methods and experimental status are version-sensitive; verify installed packages before coding.

## Selection rule

Use a built-in orchestration when its semantics are the requirement. Use a custom `WorkflowBuilder` graph when you need typed domain messages, deterministic branch rules, custom executors, explicit validation/retry transitions, precise HITL placement, or process-specific state/checkpoint behavior.

Do not choose Magentic/Group Chat simply because “multi-agent” sounds more agentic. They add model-driven coordination cost and variability.

## Handoff vs agent-as-tool

- **Handoff:** ownership of the conversation moves to another agent.
- **Agent as tool (`AsAIFunction`)**: the parent retains ownership and delegates a bounded subtask.
- **Deterministic workflow edge:** the application decides the next participant.

These are different control-flow semantics and should not be treated as interchangeable implementations.

# Agent implementation patterns

## Common application-owned path

Conceptually:

```csharp
IChatClient chatClient = /* resolved from provider adapter */;

AIAgent agent = new ChatClientAgent(
    chatClient,
    /* options/name/instructions/tools according to installed MAF version */);
```

The exact overloads/options are version-sensitive. Inspect installed APIs before copying current Learn snippets.

## Run model

MAF agent calls typically use `RunAsync` / streaming variants and may accept an `AgentSession` plus run options. Treat the session as the conversation-scoped state owner.

## Custom agent threshold

A custom `AIAgent` is justified when you are implementing a genuinely different agent runtime/protocol/remote service behavior. It is not the default way to make a domain-specific assistant.

## Agent vs workflow

Use an agent when the model chooses what happens next. Use a workflow when the application must guarantee what happens next.

Examples:

- "Use whichever of these 4 tools helps answer the question" → agent.
- "Translate, validate, retry at most twice, then escalate" → workflow containing agent steps.
- "Choose between SQL schema lookup and glossary lookup based on ambiguity" → potentially agentic retrieval tool choice.
- "Always load tenant policy before answering" → context provider / deterministic pre-step, not agent discretion.

## Agent as tool

`AIAgent.AsAIFunction()` is useful for a focused child agent exposed to a parent agent. Keep the child responsibility narrow and description explicit. If the parent accumulates many child agents/tools, reconsider workflow orchestration or hierarchical boundaries because tool selection quality degrades as the surface grows.

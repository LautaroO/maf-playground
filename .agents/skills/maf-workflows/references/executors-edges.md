# Executors and edges

Current MAF C# guidance recommends `[MessageHandler]` methods on `partial` classes deriving from `Executor`.

Conceptual shape:

```csharp
internal sealed partial class ValidateExecutor : Executor
{
    public ValidateExecutor() : base("Validate") { }

    [MessageHandler]
    private async ValueTask<ValidationCompleted> HandleAsync(
        TranslationProduced message,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        // deterministic validation / bounded collaborator call
        return new ValidationCompleted(...);
    }
}
```

Do not copy this blindly; constructor and handler shapes are version-sensitive.

## Edge choices

- direct edge → fixed sequence;
- conditional edge → binary/multiple predicates;
- switch → multi-route decision with explicit default;
- fan-out/multi-selection → independent branches;
- fan-in barrier → aggregate after required branches complete.

Current docs show `WorkflowBuilder` methods such as `AddEdge`, switch/fan-out/fan-in variants, and `WithOutputFrom`. Exact names can evolve.

## LangGraph migration

A LangGraph node often maps to an executor, but many LangGraph nodes that only call deterministic functions should remain ordinary services invoked by a thin executor. A conditional edge should remain a deterministic edge when the condition is known in code.

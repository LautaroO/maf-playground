# Function tools

Current MAF C# docs show local methods converted using `AIFunctionFactory.Create` from `Microsoft.Extensions.AI`.

Conceptual example:

```csharp
[Description("Look up a runbook entry by query.")]
static Task<RunbookResult> SearchRunbookAsync(
    [Description("Specific operational question to search for.")] string query,
    CancellationToken cancellationToken)
    => service.SearchAsync(query, cancellationToken);

AIFunction tool = AIFunctionFactory.Create(SearchRunbookAsync);
```

The exact supported signatures/metadata mapping can vary by package version/provider. Verify locally.

Prefer returning a bounded result DTO over raw HTTP/database/provider response objects.

using System.Text.Json;
using MafPlayground.Studio.Contracts;

namespace MafPlayground.Studio.Execution;

public interface IStudioEntityExecutor
{
    ValueTask<StudioEntityExecutionResult> ExecuteAsync(StudioRunRequest request, StudioExecutionContext context, CancellationToken cancellationToken);
}

public sealed record StudioEntityExecutionResult(JsonElement Output, StudioOutcomeClassification Classification = StudioOutcomeClassification.Unclassified);

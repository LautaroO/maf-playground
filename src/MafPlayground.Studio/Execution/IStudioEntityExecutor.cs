using System.Text.Json;
using MafPlayground.Studio.Contracts;

namespace MafPlayground.Studio.Execution;

public interface IStudioEntityExecutor
{
    ValueTask<JsonElement> ExecuteAsync(StudioRunRequest request, StudioExecutionContext context, CancellationToken cancellationToken);
}

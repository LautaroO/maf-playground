using MafPlayground.AI.Abstractions;
using MafPlayground.Studio.Contracts;

namespace MafPlayground.Studio.Execution;

public interface IStudioRunService
{
    ValueTask<StudioRunReceipt> StartAsync(StudioRunRequest request, AICallerContext caller, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<StudioRunSnapshot>> ListAsync(CancellationToken cancellationToken);
    ValueTask<StudioRunSnapshot?> GetAsync(string runId, CancellationToken cancellationToken);
    ValueTask<bool> CancelAsync(string runId, CancellationToken cancellationToken);
    ValueTask<StudioEventPage> ReadEventsAsync(string runId, long after, int limit, CancellationToken cancellationToken);
    IAsyncEnumerable<StudioEvent> WatchAsync(string runId, long after, CancellationToken cancellationToken);
}
public sealed class StudioCapacityException(string message) : InvalidOperationException(message);
public sealed class StudioRequestConflictException() : InvalidOperationException("The request ID was already used with different input or caller.");
public sealed class StudioSessionBusyException() : InvalidOperationException("The session already has an accepted turn.");

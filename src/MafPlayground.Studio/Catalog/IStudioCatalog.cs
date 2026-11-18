using MafPlayground.Studio.Contracts;

namespace MafPlayground.Studio.Catalog;

public interface IStudioCatalog
{
    ValueTask<IReadOnlyList<StudioEntityDescriptor>> ListAsync(CancellationToken cancellationToken, string? profileId = null);

    ValueTask<StudioTopology> GetTopologyAsync(string entityId, string profileId, CancellationToken cancellationToken);
}

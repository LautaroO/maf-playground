using MafPlayground.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MafPlayground.CLI;

public sealed class LocalUserContextAccessor : IUserContextAccessor
{
    private readonly UserContext _context = new(
        new Dictionary<string, string>
        {
            [UserContextKeys.TimeZone] = TimeZoneInfo.Local.Id,
        });

    public UserContext GetCurrent() => _context;
}

public static class LocalUserContextServiceExtensions
{
    public static IServiceCollection AddLocalUserContext(
        this IServiceCollection serviceCollection)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.TryAddSingleton<IUserContextAccessor, LocalUserContextAccessor>();
        return serviceCollection;
    }
}

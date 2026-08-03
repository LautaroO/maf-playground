using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.AI;

public static class AIServiceExtensions{
    public static IServiceCollection AddAIServices(this IServiceCollection serviceCollection)
    {
        return serviceCollection;
    }
}
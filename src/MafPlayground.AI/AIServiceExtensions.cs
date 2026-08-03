using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.AI;

public static class AIServiceExtensions
{
    public static IServiceCollection AddAIServices(
        this IServiceCollection serviceCollection,
        AIModelSelection modelSelection)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);
        ArgumentNullException.ThrowIfNull(modelSelection);

        serviceCollection.AddOptions<AgentTelemetryOptions>();
        serviceCollection.AddSingleton(modelSelection);
        serviceCollection.AddSingleton<AIProviderRegistry>();
        serviceCollection.AddSingleton<IChatClient>(serviceProvider =>
            serviceProvider.GetRequiredService<AIProviderRegistry>().CreateChatClient(modelSelection));
        serviceCollection.AddSingleton<Agents.BasicAgent.BasicAgent>();

        return serviceCollection;
    }
}

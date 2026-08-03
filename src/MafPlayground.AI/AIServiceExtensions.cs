using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton(modelSelection);
        serviceCollection.AddSingleton<AIProviderRegistry>();
        serviceCollection.AddSingleton<IChatClient>(serviceProvider =>
        {
            IChatClient chatClient = serviceProvider
                .GetRequiredService<AIProviderRegistry>()
                .CreateChatClient(modelSelection);

            foreach (IChatClientDecorator decorator in
                serviceProvider.GetServices<IChatClientDecorator>())
            {
                chatClient = decorator.Decorate(chatClient, modelSelection);
            }

            return chatClient;
        });
        serviceCollection.AddSingleton<Tools.CurrentDateTimeTool>();
        serviceCollection.AddSingleton<UserContextProvider>();
        serviceCollection.AddSingleton<Agents.BasicAgent.BasicAgent>();

        return serviceCollection;
    }
}

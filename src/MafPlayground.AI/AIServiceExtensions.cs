using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Contracts;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Guards.Budget;
using MafPlayground.AI.Guards.Content;
using MafPlayground.AI.Observability;
using MafPlayground.AI.Providers;
using MafPlayground.AI.Resilience;
using MafPlayground.AI.Workflows.Translation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI;

public static class AIServiceExtensions
{
    public static IServiceCollection AddAICore(
        this IServiceCollection serviceCollection,
        AIModelSelection modelSelection)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);
        ArgumentNullException.ThrowIfNull(modelSelection);

        serviceCollection.AddOptions<AgentTelemetryOptions>();
        serviceCollection.AddOptions<AIGuardOptions>()
            .Validate(ValidateGuardOptions, "AI guard configuration is invalid.")
            .ValidateOnStart();
        serviceCollection.AddOptions<AIResilienceOptions>()
            .Validate(
                options => options.ModelCallTimeout > TimeSpan.Zero,
                "AI:Resilience:ModelCallTimeout must be greater than zero.")
            .ValidateOnStart();
        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.TryAddSingleton(modelSelection);
        serviceCollection.TryAddSingleton<AIProviderRegistry>();
        serviceCollection.TryAddSingleton<GuardProfileResolver>();
        serviceCollection.TryAddSingleton<GuardExecutionContextAccessor>();
        serviceCollection.TryAddSingleton<IContentInspector, RegexPiiContentInspector>();
        serviceCollection.TryAddSingleton<ContentGuard>();
        serviceCollection.TryAddSingleton<AgentGuardPipeline>();
        serviceCollection.TryAddSingleton<WorkflowGuardCoordinator>();
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IChatClientDecorator, TimeoutChatClientDecorator>());
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IChatClientDecorator, BudgetChatClientDecorator>());
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IChatClientDecorator, ContentGuardChatClientDecorator>());
        serviceCollection.TryAddSingleton<IChatClient>(serviceProvider =>
        {
            IChatClient chatClient = serviceProvider
                .GetRequiredService<AIProviderRegistry>()
                .CreateChatClient(modelSelection);

            IChatClientDecorator[] decorators = serviceProvider
                .GetServices<IChatClientDecorator>()
                .OrderBy(decorator => decorator.Order)
                .ToArray();
            if (decorators.Select(decorator => decorator.Order).Distinct().Count() !=
                decorators.Length)
            {
                throw new InvalidOperationException(
                    "Chat-client decorators must have unique explicit order values.");
            }

            foreach (IChatClientDecorator decorator in decorators)
            {
                chatClient = decorator.Decorate(chatClient, modelSelection);
            }

            return chatClient;
        });
        return serviceCollection;
    }

    public static IServiceCollection AddAIServices(
        this IServiceCollection serviceCollection,
        AIModelSelection modelSelection) => serviceCollection
        .AddAICore(modelSelection)
        .AddBasicAgent()
        .AddBasicRagAgent()
        .AddTranslationWorkflow();

    private static bool ValidateGuardOptions(AIGuardOptions options)
    {
        if (options.Profiles.Count == 0)
        {
            return false;
        }

        foreach ((string name, GuardProfileOptions profile) in options.Profiles)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                profile.Content.MaxInputCharacters <= 0)
            {
                return false;
            }

            BudgetGuardOptions budget = profile.Budget;
            if (budget.MaxCostPerRun < 0 ||
                budget.MaxModelCalls <= 0 ||
                budget.MaxToolCalls <= 0 ||
                budget.MaxInputTokens <= 0 ||
                budget.MaxOutputTokens <= 0 ||
                budget.MaxOutputTokensPerCall <= 0 ||
                budget.MaxOutputTokensPerCall > budget.MaxOutputTokens ||
                budget.EstimatedCharactersPerToken <= 0 ||
                string.IsNullOrWhiteSpace(budget.Currency))
            {
                return false;
            }
        }

        return true;
    }
}

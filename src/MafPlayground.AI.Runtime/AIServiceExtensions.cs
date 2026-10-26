using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Contracts;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Guards.Budget;
using MafPlayground.AI.Guards.Content;
using MafPlayground.AI.Observability;
using MafPlayground.AI.Resilience;
using MafPlayground.AI.Runtime.Agents;
using MafPlayground.AI.Runtime;
using MafPlayground.AI.Runtime.Configuration;
using MafPlayground.AI.Runtime.Providers;
using MafPlayground.AI.Runtime.Registry;
using MafPlayground.AI.Runtime.Sessions;
using MafPlayground.AI.Runtime.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MafPlayground.AI;

public static class AIServiceExtensions
{
    public static AIBuilder ConfigureAIEntities(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new AIBuilder(services);
    }

    public static AIBuilder AddAIRuntime(
        this IServiceCollection services,
        IConfiguration configuration,
        AIStartupValidationMode validationMode = AIStartupValidationMode.Strict)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Enum.IsDefined(validationMode)) throw new ArgumentOutOfRangeException(nameof(validationMode));
        services.AddSingleton(new AIStartupValidationOptions(validationMode));
        services.AddOptions<AIRuntimeOptions>()
            .Bind(configuration.GetSection(AIRuntimeOptions.ConfigurationSectionName))
            .Validate(
                options => options.Defaults is not null && options.Entities is not null,
                "AI defaults and entity configuration are required.")
            .ValidateOnStart();
        services.AddOptions<AIGuardOptions>()
            .Bind(configuration.GetSection(AIGuardOptions.ConfigurationSectionName));
        return AddRuntimeServices(services);
    }

    public static IServiceCollection AddAICore(
        this IServiceCollection services,
        AIModelSelection modelSelection)
    {
        ArgumentNullException.ThrowIfNull(modelSelection);
        services.AddOptions<AIRuntimeOptions>().Configure(options =>
            options.Defaults.Model = modelSelection.ToString());
        _ = AddRuntimeServices(services);

        // Compatibility only for the deferred RAG/Help sample implementations.
        services.TryAddSingleton<IChatClient>(provider => provider
            .GetRequiredService<IAIChatClientFactory>()
            .Create("legacy-samples"));
        return services;
    }

    private static AIBuilder AddRuntimeServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<AgentTelemetryOptions>();
        services.AddOptions<AIResilienceOptions>()
            .Validate(
                options => options.ModelCallTimeout > TimeSpan.Zero,
                "AI:Resilience:ModelCallTimeout must be greater than zero.")
            .ValidateOnStart();
        services.AddOptions<AIGuardOptions>()
            .Validate(ValidateGuardOptions, "AI guard configuration is invalid.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<AIProviderRegistry>();
        services.TryAddSingleton<GuardProfileResolver>();
        services.TryAddSingleton<GuardExecutionContextAccessor>();
        services.TryAddSingleton<IContentInspector, RegexPiiContentInspector>();
        services.TryAddSingleton<ContentGuard>();
        services.TryAddSingleton<AgentGuardPipeline>();
        services.TryAddSingleton<WorkflowGuardCoordinator>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IChatClientDecorator, ProviderFailureChatClientDecorator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IChatClientDecorator, TimeoutChatClientDecorator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IChatClientDecorator, BudgetChatClientDecorator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IChatClientDecorator, ContentGuardChatClientDecorator>());
        services.TryAddSingleton<IAIEntityRuntimeOptionsResolver, AIEntityRuntimeOptionsResolver>();
        services.TryAddSingleton<IAIChatClientFactory, AIChatClientFactory>();
        services.TryAddSingleton<IMafAgentPipeline, MafAgentPipeline>();
        services.TryAddSingleton<IMafAgentFactory, MafAgentFactory>();
        services.TryAddSingleton<IMafWorkflowRunner, MafWorkflowRunner>();
        services.TryAddScoped<AIEntityExecutionScope>();
        services.TryAddScoped<AIEntityRegistry>();
        services.TryAddScoped<IAIEntityRegistry>(provider =>
            provider.GetRequiredService<AIEntityRegistry>());
        services.TryAddScoped<IAIEntityInspection>(provider =>
            provider.GetRequiredService<AIEntityRegistry>());
        services.TryAddScoped<IAIEntityInvoker, AIEntityInvoker>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AIEntityStartupValidator>());
        return services.ConfigureAIEntities();
    }

    public static IServiceCollection AddInMemoryAgentSessions(
        this IServiceCollection services,
        Action<InMemoryAgentSessionOptions>? configure = null)
    {
        services.AddOptions<InMemoryAgentSessionOptions>()
            .Configure(options => configure?.Invoke(options))
            .Validate(options => options.Capacity > 0, "Session capacity must be positive.")
            .Validate(options => options.Retention > TimeSpan.Zero, "Session retention must be positive.")
            .ValidateOnStart();
        services.TryAddSingleton<IAgentSessionStore, InMemoryAgentSessionStore>();
        return services;
    }

    private static bool ValidateGuardOptions(AIGuardOptions options)
    {
        if (options.Profiles.Count == 0)
        {
            return false;
        }

        foreach ((string name, GuardProfileOptions profile) in options.Profiles)
        {
            BudgetGuardOptions budget = profile.Budget;
            if (string.IsNullOrWhiteSpace(name) ||
                profile.Content.MaxInputCharacters <= 0 ||
                budget.MaxCostPerRun < 0 ||
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

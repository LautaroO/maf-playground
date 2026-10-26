using MafPlayground.AI.Agents.BasicRagAgent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MafPlayground.AI.Agents.HelpAgent;

public static class HelpAgentServiceCollectionExtensions
{
    public static IServiceCollection AddHelpAgent(
        this IServiceCollection services,
        HelpAgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);

        services.AddSingleton(definition);
        return AddHelpAgentServices(services);
    }

    internal static IServiceCollection AddHelpAgentServices(this IServiceCollection services)
    {
        services.AddOptions<HelpAgentOptions>()
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.KnowledgeBase),
                "Help agent requires a knowledge base.")
            .Validate(
                options => options.Retrieval.TopK > 0,
                "Help agent retrieval TopK must be greater than zero.")
            .Validate(
                options => options.Retrieval.MinimumSimilarity is >= 0 and <= 1,
                "Help agent retrieval MinimumSimilarity must be between zero and one.")
            .Validate(
                options => options.Retrieval.MaximumAdditionalSearches >= 0,
                "Help agent retrieval MaximumAdditionalSearches cannot be negative.")
            .Validate(
                options => options.Retrieval.MaximumQueryCharacters > 0,
                "Help agent retrieval MaximumQueryCharacters must be greater than zero.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.GuardProfile),
                "Help agent requires a guard profile.")
            .ValidateOnStart();
        services.TryAddSingleton<CitationValidator>();
        services.TryAddSingleton<IRagAnswerRepairService, ChatClientRagAnswerRepairService>();
        services.TryAddSingleton<IHelpAgentFactory, HelpAgentFactory>();
        services.TryAddSingleton<HelpAgent>();
        return services;
    }
}

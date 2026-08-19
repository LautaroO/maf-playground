using MafPlayground.AI.Agents.BasicRagAgent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MafPlayground.AI.Agents.RepositoryHelpAgent;

public static class RepositoryHelpAgentServiceCollectionExtensions
{
    public static IServiceCollection AddRepositoryHelpAgent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<RepositoryHelpAgentOptions>()
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.KnowledgeBase),
                "Repository help agent requires a knowledge base.")
            .Validate(
                options => options.Retrieval.TopK > 0,
                "Repository help agent retrieval TopK must be greater than zero.")
            .Validate(
                options => options.Retrieval.MinimumSimilarity is >= 0 and <= 1,
                "Repository help agent retrieval MinimumSimilarity must be between zero and one.")
            .Validate(
                options => options.Retrieval.MaximumAdditionalSearches >= 0,
                "Repository help agent retrieval MaximumAdditionalSearches cannot be negative.")
            .Validate(
                options => options.Retrieval.MaximumQueryCharacters > 0,
                "Repository help agent retrieval MaximumQueryCharacters must be greater than zero.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.GuardProfile),
                "Repository help agent requires a guard profile.")
            .ValidateOnStart();
        services.TryAddSingleton<CitationValidator>();
        services.TryAddSingleton<IRagAnswerRepairService, ChatClientRagAnswerRepairService>();
        services.TryAddSingleton<RepositoryHelpAgent>();
        return services;
    }
}

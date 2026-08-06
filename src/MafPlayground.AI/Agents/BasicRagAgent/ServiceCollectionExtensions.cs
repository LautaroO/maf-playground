using MafPlayground.AI.Guards;
using MafPlayground.AI.Guards.Content;
using MafPlayground.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Agents.BasicRagAgent;

public static class BasicRagAgentServiceCollectionExtensions
{
    public static IServiceCollection AddBasicRagAgent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<BasicRagAgentOptions>()
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.KnowledgeBase),
                "Basic RAG agent requires a knowledge base.")
            .Validate(
                options => options.Retrieval.TopK > 0,
                "Basic RAG agent retrieval TopK must be greater than zero.")
            .Validate(
                options => options.Retrieval.MinimumSimilarity is >= 0 and <= 1,
                "Basic RAG agent retrieval MinimumSimilarity must be between zero and one.")
            .Validate(
                options => options.Retrieval.MaximumAdditionalSearches >= 0,
                "Basic RAG agent retrieval MaximumAdditionalSearches cannot be negative.")
            .Validate(
                options => options.Retrieval.MaximumQueryCharacters > 0,
                "Basic RAG agent retrieval MaximumQueryCharacters must be greater than zero.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.GuardProfile),
                "Basic RAG agent requires a guard profile.")
            .ValidateOnStart();
        services.TryAddSingleton<RagInvocationContextAccessor>();
        services.TryAddSingleton<RagContextProvider>(provider =>
        {
            BasicRagAgentOptions options = provider
                .GetRequiredService<IOptions<BasicRagAgentOptions>>()
                .Value;
            IKnowledgeSearch search = provider
                .GetRequiredService<IKnowledgeSearchFactory>()
                .Create(
                    new KnowledgeBaseId(options.KnowledgeBase),
                    new KnowledgeSearchOptions
                    {
                        TopK = options.Retrieval.TopK,
                        MinimumSimilarity = options.Retrieval.MinimumSimilarity,
                        MaximumQueryCharacters = options.Retrieval.MaximumQueryCharacters,
                        MetadataFilters = options.Retrieval.MetadataFilters,
                    });
            return new RagContextProvider(
                search,
                options.Retrieval,
                provider.GetRequiredService<RagInvocationContextAccessor>(),
                provider.GetRequiredService<ContentGuard>(),
                provider.GetRequiredService<GuardProfileResolver>().Resolve(options.GuardProfile));
        });
        services.TryAddSingleton<CitationValidator>();
        services.TryAddSingleton<IRagAnswerRepairService, ChatClientRagAnswerRepairService>();
        services.TryAddSingleton<BasicRagAgent>();
        return services;
    }
}

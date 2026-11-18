using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Agents.HelpAgent;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Guards.Content;
using MafPlayground.AI.Observability;
using MafPlayground.AI.Runtime.Configuration;
using MafPlayground.AI.Runtime.Registry;
using MafPlayground.AI.Runtime.Providers;
using MafPlayground.Retrieval;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Agents;

internal static class GroundedAgentProfiles
{
    internal static AIAgent CreateRag(IServiceProvider services, AIEntityConstructionContext context)
    {
        ResolvedAIEntityRuntimeOptions runtime = context.RuntimeOptions;
        BasicRagAgentOptions configured = services.GetRequiredService<IOptions<BasicRagAgentOptions>>().Value;
        BasicRagAgentOptions options = new() { KnowledgeBase = configured.KnowledgeBase, GuardProfile = runtime.GuardProfile, Retrieval = Copy(configured.Retrieval) };
        IChatClient client = context.Own(services.GetRequiredService<IAIChatClientFactory>().Create(runtime));
        RagInvocationContextAccessor accessor = new();
        IKnowledgeSearch search = services.GetRequiredService<IKnowledgeSearchFactory>().Create(new(options.KnowledgeBase),
            new()
            {
                TopK = options.Retrieval.TopK,
                MinimumSimilarity = options.Retrieval.MinimumSimilarity,
                MaximumQueryCharacters = options.Retrieval.MaximumQueryCharacters,
                MetadataFilters = options.Retrieval.MetadataFilters
            });
        RagContextProvider ragContext = new(search, options.Retrieval, accessor, services.GetRequiredService<ContentGuard>(),
            services.GetRequiredService<GuardProfileResolver>().Resolve(runtime.GuardProfile));
        AIAgent agent = new BasicRagAgent.BasicRagAgent(client, ragContext, accessor, services.GetRequiredService<CitationValidator>(),
            new ChatClientRagAnswerRepairService(client), services.GetRequiredService<AgentGuardPipeline>(), Options.Create(options),
            services.GetRequiredService<IOptions<AgentTelemetryOptions>>()).Agent;
        if (agent is IDisposable disposable) context.Own(disposable);
        return agent;
    }

    internal static AIAgent CreateHelp(IServiceProvider services, AIEntityConstructionContext context)
    {
        ResolvedAIEntityRuntimeOptions runtime = context.RuntimeOptions;
        HelpAgentOptions configured = services.GetRequiredService<IOptions<HelpAgentOptions>>().Value;
        HelpAgentOptions options = new() { KnowledgeBase = configured.KnowledgeBase, GuardProfile = runtime.GuardProfile, Retrieval = Copy(configured.Retrieval) };
        IChatClient client = context.Own(services.GetRequiredService<IAIChatClientFactory>().Create(runtime));
        AIAgent agent = new HelpAgentFactory(client, services.GetRequiredService<IKnowledgeSearchFactory>(), services.GetRequiredService<CitationValidator>(),
            new ChatClientRagAnswerRepairService(client), services.GetRequiredService<AgentGuardPipeline>(), services.GetRequiredService<ContentGuard>(),
            services.GetRequiredService<GuardProfileResolver>(), services.GetRequiredService<HelpAgentDefinition>(), Options.Create(options),
            services.GetRequiredService<IHelpCapabilityCatalog>(), services.GetRequiredService<IOptions<AgentTelemetryOptions>>()).Create();
        if (agent is IDisposable disposable) context.Own(disposable);
        return agent;
    }

    private static RagRetrievalOptions Copy(RagRetrievalOptions source) => new()
    {
        TopK = source.TopK,
        MinimumSimilarity = source.MinimumSimilarity,
        MaximumAdditionalSearches = source.MaximumAdditionalSearches,
        MaximumQueryCharacters = source.MaximumQueryCharacters,
        MetadataFilters = new(source.MetadataFilters, StringComparer.OrdinalIgnoreCase),
    };
}

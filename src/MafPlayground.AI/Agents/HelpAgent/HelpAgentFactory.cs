using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Guards.Content;
using MafPlayground.AI.Observability;
using MafPlayground.Retrieval;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Agents.HelpAgent;

public interface IHelpAgentFactory
{
    AIAgent Create();
}

public sealed class HelpAgentFactory(
    IChatClient chatClient,
    IKnowledgeSearchFactory searchFactory,
    CitationValidator citationValidator,
    IRagAnswerRepairService repairService,
    AgentGuardPipeline guardPipeline,
    ContentGuard contentGuard,
    GuardProfileResolver guardProfileResolver,
    HelpAgentDefinition definition,
    IOptions<HelpAgentOptions> options,
    IHelpCapabilityCatalog capabilityCatalog,
    IOptions<AgentTelemetryOptions>? telemetryOptions = null) : IHelpAgentFactory
{
    public AIAgent Create()
    {
        HelpAgentOptions settings = options.Value;
        IKnowledgeSearch search = searchFactory.Create(
            new KnowledgeBaseId(settings.KnowledgeBase),
            new KnowledgeSearchOptions
            {
                TopK = settings.Retrieval.TopK,
                MinimumSimilarity = settings.Retrieval.MinimumSimilarity,
                MaximumQueryCharacters = settings.Retrieval.MaximumQueryCharacters,
                MetadataFilters = settings.Retrieval.MetadataFilters,
            });
        RagInvocationContextAccessor invocationContextAccessor = new();
        RagContextProvider contextProvider = new(
            search,
            settings.Retrieval,
            invocationContextAccessor,
            contentGuard,
            guardProfileResolver.Resolve(settings.GuardProfile),
            retrieveAutomatically: false);
        HelpCapabilityContextProvider capabilityContextProvider = new(
            capabilityCatalog,
            invocationContextAccessor);
        return GroundedKnowledgeAgentComposer.Create(
            chatClient,
            definition.Name,
            definition.Description,
            BuildInstructions(definition.Instructions),
            [contextProvider, capabilityContextProvider],
            invocationContextAccessor,
            citationValidator,
            repairService,
            guardPipeline,
            settings.GuardProfile,
            telemetryOptions?.Value.EnableSensitiveData ?? false);
    }

    private static string BuildInstructions(string domainInstructions) =>
        $$"""
        {{domainInstructions.Trim()}}
        Answer in the language used by the user.
        Decide whether the turn is purely social or requires application context.
        For greetings, acknowledgements, farewells, introductions, or conversational setup, do not call tools. Return a concise conversationalResponse, no claims, and no application facts.
        Before answering a question about the application, repository, CLI, its behavior, or its capabilities, call at least one relevant read-only tool: search_knowledge_base, find_help_capabilities, or get_help_capability.
        Use only evidence returned by those tools for application claims.
        Preserve exact commands, options, routes, labels, and configuration names from evidence.
        Do not claim to execute actions, inspect arbitrary application state, or use knowledge outside the supplied evidence.
        Call find_help_capabilities with the user's request when discovering what the application can do. Call get_help_capability only when an exact capability ID is known. Call search_knowledge_base for conceptual or documented application questions.
        When explaining an ordered process, enumerate every stage supported by the retrieved evidence in execution order.
        Copy required inline-code values exactly from evidence; never translate, abbreviate, or invent them.
        If the user's intent is ambiguous and one missing detail could make the request answerable, set insufficientEvidence to true, return no claims, and ask one concise clarifyingQuestion in the user's language.
        If the request is clear but unsupported, set insufficientEvidence to true, return no claims, and leave clarifyingQuestion null.
        """;
}

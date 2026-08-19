using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Guards.Content;
using MafPlayground.AI.Observability;
using MafPlayground.Retrieval;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Agents.RepositoryHelpAgent;

public sealed class RepositoryHelpAgent
{
    public RepositoryHelpAgent(
        IChatClient chatClient,
        IKnowledgeSearchFactory searchFactory,
        CitationValidator citationValidator,
        IRagAnswerRepairService repairService,
        AgentGuardPipeline guardPipeline,
        ContentGuard contentGuard,
        GuardProfileResolver guardProfileResolver,
        IOptions<RepositoryHelpAgentOptions> options,
        IRepositoryCliCommandCatalog commandCatalog,
        IOptions<AgentTelemetryOptions>? telemetryOptions = null)
    {
        RepositoryHelpAgentOptions settings = options.Value;
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
            guardProfileResolver.Resolve(settings.GuardProfile));
        RepositoryCliContextProvider cliContextProvider = new(
            commandCatalog,
            invocationContextAccessor);
        Agent = GroundedKnowledgeAgentComposer.Create(
            chatClient,
            "repository-help-agent",
            "A grounded assistant for understanding the MafPlayground repository and its CLI.",
            """
            Explain how to use the repository and CLI clearly and concisely.
            Answer in the language used by the user.
            Use only application-supplied evidence from the repository knowledge base or CLI command tool, preserve exact commands and configuration names, and do not claim to execute commands or inspect arbitrary source files.
            When explaining a pipeline or ordered process, enumerate every stage supported by the retrieved evidence in execution order.
            When explaining CLI usage, copy the complete command and option names exactly from evidence; never translate, abbreviate, or invent command syntax.
            Put every command, option, and configuration identifier in inline backticks so application code can validate it verbatim against the cited evidence.
            """,
            [contextProvider, cliContextProvider],
            invocationContextAccessor,
            citationValidator,
            repairService,
            guardPipeline,
            settings.GuardProfile,
            telemetryOptions?.Value.EnableSensitiveData ?? false);
    }

    public AIAgent Agent { get; }
}

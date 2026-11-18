using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Agents.HelpAgent;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Observability;
using MafPlayground.AI.Guards.Content;
using MafPlayground.AI.Workflows.Translation;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MafPlayground.CLI.Inspection;

internal enum LocalEntityKind
{
    Agent,
    Workflow,
}

internal sealed record LocalEntityDescriptor(
    string Id,
    LocalEntityKind Kind,
    string Description,
    Type InputType,
    object InputExample,
    Func<IServiceProvider, AIAgent>? CreateAgent = null,
    Func<IConfiguration, Workflow>? CreateWorkflowDiagram = null,
    Func<IServiceProvider, string, Workflow>? CreateHostedWorkflow = null,
    Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<WorkflowEvent>>? StreamWorkflow = null,
    bool RequiresRetrieval = false);

internal static class LocalEntityCatalog
{
    public static IReadOnlyList<LocalEntityDescriptor> All { get; } =
    [
        new(
            "basic-agent",
            LocalEntityKind.Agent,
            "General conversational agent.",
            typeof(BasicAgentRequest),
            new BasicAgentRequest("What time is it for me?"),
            CreateAgent: services => services.GetRequiredService<IBasicAgentSource>().Agent),
        new(
            "basic-rag-agent",
            LocalEntityKind.Agent,
            "A grounded help assistant that answers from an ingested document knowledge base with citations.",
            typeof(string),
            "How long does a password-reset link remain valid?",
            CreateAgent: services => services.GetRequiredService<BasicRagAgent>().Agent, RequiresRetrieval: true),
        new(
            "repository-help-agent",
            LocalEntityKind.Agent,
            "A grounded assistant for understanding the MafPlayground repository and its CLI.",
            typeof(string),
            "How do I run the repository help agent from the CLI?",
            CreateAgent: services => services.GetRequiredService<HelpAgent>().Agent, RequiresRetrieval: true),
        new(
            "translation-workflow",
            LocalEntityKind.Workflow,
            "Parallel validated translation.",
            typeof(TranslationWorkflowRequest),
            new TranslationWorkflowRequest("Hello, how are you?", ["es", "fr"]),
            CreateWorkflowDiagram: CreateTranslationWorkflowDiagram,
            CreateHostedWorkflow: (services, name) => services
                .GetRequiredService<ITranslationWorkflowSource>()
                .CreateWorkflowForDevUI(name),
            StreamWorkflow: (services, input, token) => services.GetRequiredService<TranslationWorkflowRunner>()
                .RunStreamingAsync((TranslationWorkflowRequest)input, includeInspectionEvents: true, token)),
    ];

    public static LocalEntityDescriptor? Find(
        LocalEntityKind kind,
        string id) =>
        All.FirstOrDefault(entity =>
            entity.Kind == kind &&
            entity.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static Workflow CreateTranslationWorkflowDiagram(IConfiguration configuration)
    {
        TranslationWorkflowOptions options = configuration
            .GetSection("AI:Workflows:Translation")
            .Get<TranslationWorkflowOptions>() ?? new TranslationWorkflowOptions();
        IOptions<TranslationWorkflowOptions> workflowOptions = Options.Create(options);
        AIGuardOptions guardOptions = configuration
            .GetSection(AIGuardOptions.ConfigurationSectionName)
            .Get<AIGuardOptions>() ?? new AIGuardOptions();
        GuardProfileResolver profiles = new(Options.Create(guardOptions));
        GuardExecutionContextAccessor contextAccessor = new();
        WorkflowGuardCoordinator guards = new(
            profiles,
            contextAccessor,
            new ContentGuard(new RegexPiiContentInspector()));
        TranslationService service = new(
            new DiagramOnlyTranslationModel(),
            workflowOptions,
            guards);
        TranslationWorkflowFactory factory = new(
            service,
            workflowOptions,
            Options.Create(new AgentTelemetryOptions()),
            guards);
        return factory.Create();
    }

    private sealed class DiagramOnlyTranslationModel : ITranslationModel
    {
        public Task<string> TranslateAsync(
            TranslationDraftRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Diagram inspection does not execute workflows.");

        public Task<TranslationValidation> ValidateAsync(
            TranslationValidationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Diagram inspection does not execute workflows.");
    }
}

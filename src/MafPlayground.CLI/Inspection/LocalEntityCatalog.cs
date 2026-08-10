using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI.Agents.BasicRagAgent;
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
    Func<IServiceProvider, string, Workflow>? CreateHostedWorkflow = null);

internal static class LocalEntityCatalog
{
    public static IReadOnlyList<LocalEntityDescriptor> All { get; } =
    [
        new(
            "basic-agent",
            LocalEntityKind.Agent,
            "A basic conversational agent for experimenting with Microsoft Agent Framework.",
            typeof(string),
            "What time is it for me?",
            CreateAgent: services => services.GetRequiredService<BasicAgent>().Agent),
        new(
            "basic-rag-agent",
            LocalEntityKind.Agent,
            "A grounded help assistant that answers from an ingested document knowledge base with citations.",
            typeof(string),
            "How long does a password-reset link remain valid?",
            CreateAgent: services => services.GetRequiredService<BasicRagAgent>().Agent),
        new(
            "translation-workflow",
            LocalEntityKind.Workflow,
            "Translates text in parallel, validates each translation, and retries with feedback.",
            typeof(TranslationWorkflowRequest),
            new TranslationWorkflowRequest("Hello, how are you?", ["es", "fr"]),
            CreateWorkflowDiagram: CreateTranslationWorkflowDiagram,
            CreateHostedWorkflow: (services, name) => services
                .GetRequiredService<TranslationWorkflowFactory>()
                .CreateForDevUI(name)),
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
            string sourceText,
            string targetLanguage,
            IReadOnlyList<string>? validationFeedback,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Diagram inspection does not execute workflows.");

        public Task<TranslationValidation> ValidateAsync(
            string sourceText,
            string targetLanguage,
            string translatedText,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Diagram inspection does not execute workflows.");
    }
}

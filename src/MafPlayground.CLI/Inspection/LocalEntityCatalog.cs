using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Agents.HelpAgent;
using MafPlayground.AI.Runtime.Registry;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;

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
    object? InputExample,
    Func<IServiceProvider, AIAgent>? CreateAgent = null,
    Func<IServiceProvider, Workflow>? CreateWorkflowDiagram = null,
    Func<IServiceProvider, string, Workflow>? CreateHostedWorkflow = null);

internal static class LocalEntityCatalog
{
    public static IReadOnlyList<LocalEntityDescriptor> Create(IReadOnlyList<AIEntityRegistrationInfo> registrations)
    {
        AIEntityInspectionContext context = new("cli-default");
        IEnumerable<LocalEntityDescriptor> registered = registrations.Select(registration => new LocalEntityDescriptor(
            registration.Descriptor.Id,
            registration.Descriptor.Kind == AIEntityKind.Agent ? LocalEntityKind.Agent : LocalEntityKind.Workflow,
            registration.Descriptor.Description,
            registration.Descriptor.InputType,
            registration.InputExample,
            CreateAgent: registration.AgentSourceFactory is null ? null : services =>
                registration.AgentSourceFactory(services, context).Agent,
            CreateWorkflowDiagram: registration.WorkflowSourceFactory is null ? null : services =>
                registration.WorkflowSourceFactory(services, context).CreateWorkflow(),
            CreateHostedWorkflow: registration.WorkflowSourceFactory is null ? null : (services, name) =>
                registration.WorkflowSourceFactory(services, context).CreateWorkflowForDevUI(name)));

        // These CLI-only legacy surfaces remain until their normal typed registrations exist.
        // A future normal registration takes precedence, without duplicating its logical ID.
        HashSet<string> registeredIds = registrations.Select(registration => registration.Descriptor.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return registered.Concat(LegacyEntities.Where(entity => !registeredIds.Contains(entity.Id)))
            .OrderBy(entity => entity.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static LocalEntityDescriptor? Find(
        IReadOnlyList<LocalEntityDescriptor> entities,
        LocalEntityKind kind,
        string id) =>
        entities.FirstOrDefault(entity =>
            entity.Kind == kind && entity.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<LocalEntityDescriptor> LegacyEntities { get; } =
    [
        new(
            "basic-rag-agent",
            LocalEntityKind.Agent,
            "A grounded help assistant that answers from an ingested document knowledge base with citations.",
            typeof(string),
            "How long does a password-reset link remain valid?",
            CreateAgent: services => services.GetRequiredService<BasicRagAgent>().Agent),
        new(
            "repository-help-agent",
            LocalEntityKind.Agent,
            "A grounded assistant for understanding the MafPlayground repository and its CLI.",
            typeof(string),
            "How do I run the repository help agent from the CLI?",
            CreateAgent: services => services.GetRequiredService<HelpAgent>().Agent),
    ];
}

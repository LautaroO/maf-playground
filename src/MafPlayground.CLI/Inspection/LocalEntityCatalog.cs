using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Registry;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

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

        return registered.OrderBy(entity => entity.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static LocalEntityDescriptor? Find(
        IReadOnlyList<LocalEntityDescriptor> entities,
        LocalEntityKind kind,
        string id) =>
        entities.FirstOrDefault(entity =>
            entity.Kind == kind && entity.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Registry;
using MafPlayground.AI.Runtime.Workflows;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MafPlayground.Studio.Catalog;

public sealed class StudioCatalog(IAIEntityRegistry registry, IAIEntityInspection inspection, StudioExecutionProfileBinder? profiles = null) : IStudioCatalog
{
    public ValueTask<IReadOnlyList<StudioEntityDescriptor>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<StudioEntityDescriptor> entities = [];
        foreach (AIEntityDescriptor descriptor in registry.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AIEntityRegistrationInfo registration = inspection.GetRegistration(descriptor.Id);
            AIEntityAvailability availability = inspection.GetAvailability(descriptor.Id);
            entities.Add(new StudioEntityDescriptor(
                descriptor.Id,
                descriptor.Kind,
                descriptor.Description,
                descriptor.InputType.FullName ?? descriptor.InputType.Name,
                descriptor.OutputType.FullName ?? descriptor.OutputType.Name,
                AIJsonUtilities.CreateJsonSchema(descriptor.InputType, serializerOptions: AIJsonSerializerOptions.Default),
                AIJsonUtilities.CreateJsonSchema(descriptor.OutputType, serializerOptions: AIJsonSerializerOptions.Default),
                registration.InputExample,
                descriptor.SupportsSessions,
                registration.HasAgentSource,
                registration.HasWorkflowSource,
                availability.IsAvailable,
                availability.IsAvailable ? null : availability.Reason ?? "The entity is unavailable.",
                registration.SupportsProfiles, registration.SupportsStreaming, registration.SupportsOutcomeClassification));
        }
        return ValueTask.FromResult<IReadOnlyList<StudioEntityDescriptor>>(entities.AsReadOnly());
    }

    public ValueTask<StudioTopology> GetTopologyAsync(string entityId, string profileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        profiles?.Bind(profileId);
        AIEntityRegistrationInfo registration = inspection.GetRegistration(entityId);
        AIEntityAvailability availability = inspection.GetAvailability(entityId);
        if (!availability.IsAvailable)
        {
            return ValueTask.FromResult(Unavailable(availability.Reason ?? "The entity is unavailable."));
        }
        if (!registration.HasWorkflowSource)
        {
            return ValueTask.FromResult(Unavailable(registration.Descriptor.Kind == AIEntityKind.Agent
                ? "Agents expose observed call trees rather than a declared workflow graph."
                : "No native workflow source is registered for this entity."));
        }

        IMafWorkflowSource source = inspection.GetWorkflowSource(entityId, new AIEntityInspectionContext(profileId))
            ?? throw new InvalidOperationException("The registered workflow source factory returned null.");
        cancellationToken.ThrowIfCancellationRequested();
        StudioTopology topology = WorkflowTopologyMapper.Map(registration.Descriptor.Id, profileId, source.CreateWorkflow());
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(topology);

        StudioTopology Unavailable(string reason) => new(
            registration.Descriptor.Id, profileId, StudioTopologyStatus.Unavailable,
            reason, null, [], [], null, null);
    }
}

public static class StudioCatalogServiceExtensions
{
    public static IServiceCollection AddStudioCatalog(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IStudioCatalog, StudioCatalog>();
        return services;
    }
}

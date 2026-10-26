using System.Text.Json;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Agents;
using MafPlayground.AI.Runtime.Workflows;

namespace MafPlayground.AI.Runtime.Registry;

/// <summary>Opaque profile identity forwarded to a source factory in the current DI scope.</summary>
public sealed record AIEntityInspectionContext(string ProfileId);

public sealed record AIEntityAvailability(bool IsAvailable, string? Reason = null)
{
    public static AIEntityAvailability Available { get; } = new(true);

    public static AIEntityAvailability Unavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(false, reason);
    }
}

public sealed record AIEntityRegistrationInfo(
    AIEntityDescriptor Descriptor,
    JsonElement? InputExample,
    Func<IServiceProvider, AIEntityInspectionContext, IMafAgentSource>? AgentSourceFactory,
    Func<IServiceProvider, AIEntityInspectionContext, IMafWorkflowSource>? WorkflowSourceFactory)
{
    public bool HasAgentSource => AgentSourceFactory is not null;

    public bool HasWorkflowSource => WorkflowSourceFactory is not null;
}

/// <summary>Optional capabilities on the existing entity registry. Metadata inspection never resolves native sources.</summary>
public interface IAIEntityInspection
{
    AIEntityRegistrationInfo GetRegistration(string entityId);

    AIEntityAvailability GetAvailability(string entityId);

    IMafAgentSource? GetAgentSource(string entityId, AIEntityInspectionContext context);

    IMafWorkflowSource? GetWorkflowSource(string entityId, AIEntityInspectionContext context);
}

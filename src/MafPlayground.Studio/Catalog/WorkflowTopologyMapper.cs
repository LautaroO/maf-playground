using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MafPlayground.Studio.Contracts;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;

namespace MafPlayground.Studio.Catalog;

internal static class WorkflowTopologyMapper
{
    public static StudioTopology Map(string entityId, string profileId, Workflow workflow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(workflow);
        StudioTopologyNode[] nodes = workflow.ReflectExecutors().Keys
            .Order(StringComparer.Ordinal)
            .Select(id => new StudioTopologyNode(id, id, id == workflow.StartExecutorId))
            .ToArray();
        StudioTopologyEdge[] edges = workflow.ReflectEdges().Values
            .SelectMany(group => group)
            .Select(MapEdge)
            // Native fan-in edges are reported once for each source. Their public
            // structural identity is the connection, kind and conditionality.
            .DistinctBy(edge => edge.Id)
            .OrderBy(edge => edge.Id, StringComparer.Ordinal)
            .ToArray();
        return new StudioTopology(
            entityId, profileId, StudioTopologyStatus.Available, null, workflow.StartExecutorId,
            Array.AsReadOnly(nodes), Array.AsReadOnly(edges),
            "MAF 1.16 public topology metadata does not expose edge labels. The native Mermaid export retains available labels.",
            WorkflowVisualizer.ToMermaidString(workflow));
    }

    private static StudioTopologyEdge MapEdge(EdgeInfo edge)
    {
        string[] sources = edge.Connection.SourceIds.Order(StringComparer.Ordinal).ToArray();
        string[] targets = edge.Connection.SinkIds.Order(StringComparer.Ordinal).ToArray();
        string kind = edge.Kind switch
        {
            EdgeKind.Direct => "direct",
            EdgeKind.FanOut => "fanOut",
            EdgeKind.FanIn => "fanIn",
            _ => throw new NotSupportedException($"Unsupported MAF edge kind '{edge.Kind}'."),
        };
        bool conditional = edge is DirectEdgeInfo { HasCondition: true } or FanOutEdgeInfo { HasAssigner: true };
        string identity = JsonSerializer.Serialize(new { kind, sources, targets, conditional });
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return new StudioTopologyEdge(id, kind, Array.AsReadOnly(sources), Array.AsReadOnly(targets), conditional, null);
    }
}

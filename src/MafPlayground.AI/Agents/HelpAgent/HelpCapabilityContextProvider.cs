using System.ComponentModel;
using MafPlayground.AI.Agents.BasicRagAgent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Agents.HelpAgent;

internal sealed class HelpCapabilityContextProvider(
    IHelpCapabilityCatalog capabilityCatalog,
    RagInvocationContextAccessor invocationContextAccessor) : AIContextProvider
{
    private const int MaximumToolRequestCharacters = 512;
    private const int MaximumCapabilityResults = 3;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AIFunction findCapabilities = AIFunctionFactory.Create(
            ([Description("Natural-language description of the application capability to find.")] string request,
                CancellationToken toolCancellationToken) =>
            {
                toolCancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(request) ||
                    request.Length > MaximumToolRequestCharacters)
                {
                    return new HelpCapabilitySearchToolResult(
                        [],
                        $"The capability search request must contain between 1 and {MaximumToolRequestCharacters} characters.");
                }

                IReadOnlyList<RagEvidence> evidence = AddEvidence(
                    capabilityCatalog.Search(request, MaximumCapabilityResults)
                        .Take(MaximumCapabilityResults));
                return evidence.Count == 0
                    ? new HelpCapabilitySearchToolResult(
                        [],
                        "No matching application capability was found.")
                    : new HelpCapabilitySearchToolResult(evidence, null);
            },
            name: "find_help_capabilities",
            description: "Finds matching application capabilities from a natural-language request and returns bounded, read-only evidence.");
        AIFunction getCapability = AIFunctionFactory.Create(
            ([Description("Exact application capability ID.")] string capabilityId,
                CancellationToken toolCancellationToken) =>
            {
                toolCancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(capabilityId) ||
                    capabilityId.Length > MaximumToolRequestCharacters)
                {
                    return new HelpCapabilityToolResult(
                        null,
                        $"The capability ID must contain between 1 and {MaximumToolRequestCharacters} characters.");
                }

                HelpCapability? capability = capabilityCatalog.Find(capabilityId);
                if (capability is null)
                {
                    return new HelpCapabilityToolResult(
                        null,
                        "Unknown capability ID. Use an exact ID returned by find_help_capabilities.");
                }

                return new HelpCapabilityToolResult(AddCapabilityEvidence(capability), null);
            },
            name: "get_help_capability",
            description: "Gets bounded, read-only evidence for one exact application capability ID.");

        return ValueTask.FromResult(new AIContext
        {
            Instructions =
                "Call find_help_capabilities when the user asks what the application can do or how to perform an operation. " +
                "Call get_help_capability only when an exact capability ID is already known. " +
                "Do not call either tool for a purely social turn. " +
                "These tools describe capabilities and never execute them.",
            Messages = [],
            Tools = [findCapabilities, getCapability],
        });

        IReadOnlyList<RagEvidence> AddEvidence(
            IEnumerable<HelpCapability> capabilities) => capabilities
            .Select(AddCapabilityEvidence)
            .ToArray();

        RagEvidence AddCapabilityEvidence(HelpCapability capability) =>
            invocationContextAccessor.Current.AddEvidence(
                capability.EvidenceText,
                capability.Citation,
                similarity: 1,
                requiredInlineCode: capability.RequiredInlineCode);
    }
}

internal sealed record HelpCapabilityToolResult(
    RagEvidence? Evidence,
    string? Error);

internal sealed record HelpCapabilitySearchToolResult(
    IReadOnlyList<RagEvidence> Evidence,
    string? Message);

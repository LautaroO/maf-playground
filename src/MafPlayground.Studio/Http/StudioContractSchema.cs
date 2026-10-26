using System.Text.Json;
using MafPlayground.AI.Runtime.Registry;
using MafPlayground.AI.Abstractions;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using Microsoft.Extensions.AI;

namespace MafPlayground.Studio.Http;

public static class StudioContractSchema
{
    public static JsonElement Create(IEnumerable<AIEntityRegistrationInfo> registrations)
    {
        Type[] types = [typeof(StudioInstance), typeof(StudioSourceProvenance), typeof(StudioRunRequest), typeof(StudioRunReceipt), typeof(StudioRunSnapshot),
            typeof(StudioEntityDescriptor), typeof(StudioTopology), typeof(StudioEvent), typeof(StudioEventPage),
            typeof(StudioPayload), typeof(StudioSpan), typeof(StudioSessionRequest), typeof(StudioSession),
            typeof(StudioExecutionProfileSnapshot), typeof(StudioMcpSpanPage),
            typeof(StudioComparisonRequest), typeof(StudioComparison), typeof(StudioComparisonRun), typeof(StudioComparisonMetrics), typeof(StudioComparisonError),
            typeof(StudioMetricDelta), typeof(StudioValueChange), typeof(StudioInvocationMatch),
            typeof(StudioMcpPage<StudioEntityDescriptor>), typeof(StudioMcpPage<StudioRunSnapshot>), typeof(StudioMcpPage<string>), typeof(Microsoft.AspNetCore.Mvc.ProblemDetails)];
        var transport = types.ToDictionary(type => type.IsGenericType ? $"StudioMcpPageOf{type.GenericTypeArguments[0].Name}" : type.Name, type => AIJsonUtilities.CreateJsonSchema(type, serializerOptions: AIJsonSerializerOptions.Default));
        Type[] enums = [typeof(StudioRunStatus), typeof(StudioOutcomeClassification), typeof(StudioCaptureState), typeof(StudioTopologyStatus), typeof(AIEntityKind)];
        foreach (Type type in enums)
            transport.Add(type.Name, JsonSerializer.SerializeToElement(new { type = "integer", @enum = Enum.GetValues(type).Cast<object>().Select(Convert.ToInt32), tsEnumNames = Enum.GetNames(type) }));
        return JsonSerializer.SerializeToElement(new
        {
            version = "1",
            transport,
            entities = registrations.Select(registration => new
            {
                registration.Descriptor.Id,
                input = AIJsonUtilities.CreateJsonSchema(registration.Descriptor.InputType, serializerOptions: AIJsonSerializerOptions.Default),
                output = AIJsonUtilities.CreateJsonSchema(registration.Descriptor.OutputType, serializerOptions: AIJsonSerializerOptions.Default),
                example = registration.InputExample,
            }).ToArray(),
        }, AIJsonSerializerOptions.Default);
    }
}

using System.Text.Json;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Registry;
using MafPlayground.Studio.Contracts;

namespace MafPlayground.Studio.Execution;

/// <summary>Reusable transport preflight and retained-data inspection; never reconstructs evicted capture.</summary>
public sealed class StudioInspectionService(IAIEntityInspection inspection, StudioExecutionProfileBinder binding,
    StudioProfileCatalog profiles, IStudioRunService runs)
{
    public AIEntityRegistrationInfo ValidateEntity(string entityId, string profileId, bool requireAvailable)
    {
        profiles.Validate(profileId);
        binding.Bind(profileId);
        var registration = inspection.GetRegistration(entityId);
        if (requireAvailable && !inspection.GetAvailability(entityId).IsAvailable) throw new StudioEntityUnavailableException();
        return registration;
    }

    public void ValidateRun(StudioRunRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestId);
        if (request.RequestId.Length > 128 || request.RequestId.Any(char.IsControl)) throw new ArgumentException("Invalid request identity.");
        var registration = ValidateEntity(request.EntityId, request.ProfileId, true);
        if (request.SessionId is not null && !registration.Descriptor.SupportsSessions) throw new ArgumentException("Sessions are unsupported.");
        if (request.Input.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) throw new ArgumentException("Input is required.");
        try
        {
            _ = request.Input.Deserialize(registration.Descriptor.InputType, AIJsonSerializerOptions.Default) ?? throw new JsonException();
        }
        catch (JsonException) { throw new ArgumentException("Input does not match the entity contract."); }
    }

    public async ValueTask<StudioPayload?> GetPayloadAsync(string runId, string payloadId, CancellationToken cancellationToken)
    {
        await foreach (StudioEvent item in RetainedAsync(runId, cancellationToken))
        {
            if (TryRead<StudioPayload>(item.Data) is { Id: not null } payload && payload.Id == payloadId) return payload;
            if (item.Kind == "span" && TryRead<StudioSpan>(item.Data) is { Attributes: not null, Events: not null, Links: not null } span)
                foreach (StudioPayload nested in new[] { span.Attributes, span.Events, span.Links })
                    if (nested.Id == payloadId) return nested;
        }
        return null;
    }

    public async ValueTask<StudioSpan?> GetSpanAsync(string runId, string spanId, CancellationToken cancellationToken)
    {
        await foreach (StudioEvent item in RetainedAsync(runId, cancellationToken))
            if (item.Kind == "span" && TryRead<StudioSpan>(item.Data) is { SpanId: not null } span && span.SpanId == spanId) return span;
        return null;
    }

    private async IAsyncEnumerable<StudioEvent> RetainedAsync(string runId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long after = 0;
        while (true)
        {
            StudioEventPage page = await runs.ReadEventsAsync(runId, after, 100, cancellationToken).ConfigureAwait(false);
            foreach (StudioEvent item in page.Events) yield return item;
            if (page.NextCursor <= after || page.Events.Count < 100) yield break;
            after = page.NextCursor;
        }
    }
    private static T? TryRead<T>(JsonElement data) where T : class
    {
        try { return data.Deserialize<T>(); }
        catch (JsonException) { return null; }
    }
}
public sealed class StudioEntityUnavailableException() : InvalidOperationException("The entity is unavailable for this profile.");

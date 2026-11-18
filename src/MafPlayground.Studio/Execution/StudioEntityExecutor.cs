using System.Text.Json;
using MafPlayground.Studio.Capture;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Registry;
using MafPlayground.Studio.Contracts;

namespace MafPlayground.Studio.Execution;

public sealed class StudioEntityExecutor(IAIEntityInvoker invoker, IAIEntityInspection inspection,
    StudioExecutionProfileBinder profiles, IStudioEventSink events, TimeProvider clock, StudioPayloadStore payloads, StudioActivityListener listener, StudioOptions options) : IStudioEntityExecutor
{
    public async ValueTask<StudioEntityExecutionResult> ExecuteAsync(StudioRunRequest request, StudioExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.EntityId != request.EntityId || context.ProfileId != request.ProfileId || context.SessionId != request.SessionId)
            throw new ArgumentException("Execution context must match the submitted entity, profile and session.");
        profiles.Bind(request.ProfileId);
        AIEntityRegistrationInfo registration = inspection.GetRegistration(request.EntityId);
        AIEntityAvailability availability = inspection.GetAvailability(request.EntityId);
        if (!availability.IsAvailable) throw new InvalidOperationException(availability.Reason);
        _ = listener;
        using StudioCaptureScope? capture = options.EnableCapture ? new(context, events, payloads, clock) : null;
        WorkflowEventMapper observer = new(events, payloads, clock, context.RunId);
        if (!registration.SupportsProfiles)
        {
            observer.Record(new("capability_unavailable", JsonSerializer.SerializeToElement(new { capability = "profiles" })));
            if (profiles.HasOverrides) throw new NotSupportedException("This entity has no registered profile construction capability.");
        }
        if (!registration.SupportsStreaming)
            observer.Record(new("capability_unavailable", JsonSerializer.SerializeToElement(new { capability = "streaming" })));
        AIEntityInvocationResult result = await invoker.InvokeAsync(request.EntityId, request.Input,
            new AIExecutionContext(context.RunId, context.Caller, context.SessionId, request.ProfileId,
                options.EnableCapture ? observer : null), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new(JsonSerializer.SerializeToElement(result.Output, AIJsonSerializerOptions.Default), result.Outcome switch
        {
            AIOutcomeClassification.Succeeded => StudioOutcomeClassification.Succeeded,
            AIOutcomeClassification.PartiallyFailed => StudioOutcomeClassification.PartiallyFailed,
            AIOutcomeClassification.Failed => StudioOutcomeClassification.Failed,
            _ => StudioOutcomeClassification.Unclassified,
        });
    }
}

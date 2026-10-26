using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using MafPlayground.AI.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using MafPlayground.AI.Observability;
using MafPlayground.AI.Runtime.Agents;
using MafPlayground.AI.Runtime.Workflows;

namespace MafPlayground.AI.Runtime.Registry;

internal sealed class AIEntityRegistry(
    IEnumerable<AIEntityRegistration> registrations,
    IServiceProvider services,
    AIEntityExecutionScope executionScope) : IAIEntityRegistry, IAIEntityInspection
{
    private readonly IReadOnlyDictionary<string, AIEntityRegistration> _registrations =
        BuildMap(registrations);

    public IReadOnlyList<AIEntityDescriptor> Entities => _registrations.Values
        .Select(registration => registration.Descriptor)
        .OrderBy(descriptor => descriptor.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public AIEntityDescriptor? Find(string entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        return _registrations.TryGetValue(entityId, out AIEntityRegistration? registration)
            ? registration.Descriptor
            : null;
    }

    public IAIEntity<TInput, TOutput> Resolve<TInput, TOutput>(string entityId)
    {
        AIEntityRegistration registration = GetRequired(entityId);
        if (registration.Descriptor.InputType != typeof(TInput) ||
            registration.Descriptor.OutputType != typeof(TOutput))
        {
            throw new InvalidOperationException(
                $"AI entity '{entityId}' is registered for " +
                $"{registration.Descriptor.InputType.Name} -> {registration.Descriptor.OutputType.Name}.");
        }

        return (IAIEntity<TInput, TOutput>)ResolveInstance(registration);
    }

    internal object ResolveInstance(AIEntityRegistration registration) =>
        executionScope.ProfileId is not null && registration.ProfileFactory is not null
            ? executionScope.Resolve(registration, services)
            : services.GetRequiredService(registration.ImplementationType);

    public AIEntityRegistrationInfo GetRegistration(string entityId) => GetRequired(entityId).Info;

    public AIEntityAvailability GetAvailability(string entityId) =>
        GetRequired(entityId).Availability?.Invoke(services) ?? AIEntityAvailability.Available;

    public IMafAgentSource? GetAgentSource(string entityId, AIEntityInspectionContext context)
    {
        ValidateContext(context);
        AIEntityRegistration registration = GetRequired(entityId);
        return executionScope.ProfileId is not null && registration.ProfileFactory is not null
            ? ResolveInstance(registration) as IMafAgentSource
            : registration.AgentSourceFactory?.Invoke(services, context);
    }

    public IMafWorkflowSource? GetWorkflowSource(string entityId, AIEntityInspectionContext context)
    {
        ValidateContext(context);
        AIEntityRegistration registration = GetRequired(entityId);
        return executionScope.ProfileId is not null && registration.ProfileFactory is not null
            ? ResolveInstance(registration) as IMafWorkflowSource
            : registration.WorkflowSourceFactory?.Invoke(services, context);
    }

    private void ValidateContext(AIEntityInspectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ProfileId);
        if (executionScope.ProfileId is not null && executionScope.ProfileId != context.ProfileId)
            throw new InvalidOperationException("Native inspection must use the bound execution profile.");
    }

    internal AIEntityRegistration GetRequired(string entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        return _registrations.TryGetValue(entityId, out AIEntityRegistration? registration)
            ? registration
            : throw new AIEntityNotFoundException(entityId);
    }

    internal static IReadOnlyDictionary<string, AIEntityRegistration> BuildMap(
        IEnumerable<AIEntityRegistration> registrations)
    {
        Dictionary<string, AIEntityRegistration> result =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (AIEntityRegistration registration in registrations)
        {
            if (!result.TryAdd(registration.Descriptor.Id, registration))
            {
                throw new InvalidOperationException(
                    $"AI entity ID '{registration.Descriptor.Id}' is registered more than once.");
            }
        }

        return result;
    }
}

internal sealed class AIEntityInvoker(
    AIEntityRegistry registry) : IAIEntityInvoker
{
    private static readonly ActivitySource ActivitySource = new(AITelemetry.RuntimeSourceName);
    private static readonly MethodInfo InvokeTypedMethod = typeof(AIEntityInvoker)
        .GetMethod(nameof(InvokeTypedAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public async ValueTask<AIEntityInvocationResult> InvokeAsync(
        string entityId,
        JsonElement input,
        AIExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentNullException.ThrowIfNull(context);
        AIEntityRegistration registration = registry.GetRequired(entityId);
        long started = Stopwatch.GetTimestamp();
        using Activity? activity = ActivitySource.StartActivity("execute_ai_entity");
        activity?.SetTag("maf_playground.execution.id", context.ExecutionId);
        activity?.SetTag(AITelemetry.EntityNameTag, registration.Descriptor.Id);
        activity?.SetTag(AITelemetry.EntityTypeTag, registration.Descriptor.Kind.ToString());
        MethodInfo method = InvokeTypedMethod.MakeGenericMethod(
            registration.Descriptor.InputType,
            registration.Descriptor.OutputType);
        try
        {
            Task<object?> task = (Task<object?>)method.Invoke(
                this,
                [registration, input, context, cancellationToken])!;
            object? output = await task.ConfigureAwait(false);
            AITelemetry.RecordOperation(
                "execute",
                registration.Descriptor.Kind.ToString(),
                registration.Descriptor.Id,
                "success",
                Stopwatch.GetElapsedTime(started));
            return new AIEntityInvocationResult(
                context.ExecutionId,
                registration.Descriptor.Id,
                context.SessionId,
                output, registration.OutcomeClassifier?.Invoke(output) ?? AIOutcomeClassification.Unclassified);
        }
        catch (Exception exception)
        {
            AITelemetry.RecordOperation(
                "execute",
                registration.Descriptor.Kind.ToString(),
                registration.Descriptor.Id,
                "error",
                Stopwatch.GetElapsedTime(started),
                exception.GetType().FullName);
            throw;
        }
    }

    private async Task<object?> InvokeTypedAsync<TInput, TOutput>(
        AIEntityRegistration registration,
        JsonElement input,
        AIExecutionContext context,
        CancellationToken cancellationToken)
    {
        TInput value;
        try
        {
            value = input.Deserialize<TInput>(AIJsonSerializerOptions.Default)
                ?? throw new JsonException("The input cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new AIEntityInputException("The AI entity input is invalid.", exception);
        }

        if (!registration.Descriptor.SupportsSessions && context.SessionId is not null)
        {
            throw new AISessionNotSupportedException(registration.Descriptor.Id);
        }

        var entity = (IAIEntity<TInput, TOutput>)registry.ResolveInstance(registration);
        return await entity.ExecuteAsync(value, context, cancellationToken).ConfigureAwait(false);
    }
}

public static class AIJsonSerializerOptions
{
    public static JsonSerializerOptions Default { get; } =
        new(JsonSerializerDefaults.Web);
}

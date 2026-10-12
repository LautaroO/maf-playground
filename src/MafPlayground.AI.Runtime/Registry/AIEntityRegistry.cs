using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using MafPlayground.AI.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using MafPlayground.AI.Observability;

namespace MafPlayground.AI.Runtime.Registry;

internal sealed class AIEntityRegistry(
    IEnumerable<AIEntityRegistration> registrations,
    IServiceProvider services) : IAIEntityRegistry
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

        return (IAIEntity<TInput, TOutput>)services.GetRequiredService(
            registration.ImplementationType);
    }

    internal AIEntityRegistration GetRequired(string entityId) =>
        _registrations.TryGetValue(entityId, out AIEntityRegistration? registration)
            ? registration
            : throw new AIEntityNotFoundException(entityId);

    private static IReadOnlyDictionary<string, AIEntityRegistration> BuildMap(
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
    AIEntityRegistry registry,
    IServiceProvider services) : IAIEntityInvoker
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
                output);
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

        var entity = (IAIEntity<TInput, TOutput>)services.GetRequiredService(
            registration.ImplementationType);
        return await entity.ExecuteAsync(value, context, cancellationToken).ConfigureAwait(false);
    }
}

public static class AIJsonSerializerOptions
{
    public static JsonSerializerOptions Default { get; } =
        new(JsonSerializerDefaults.Web);
}

using System.Text.Json;

namespace MafPlayground.AI.Abstractions;

public interface IAIEntity<in TInput, TOutput>
{
    ValueTask<TOutput> ExecuteAsync(
        TInput input,
        AIExecutionContext context,
        CancellationToken cancellationToken);
}

public sealed record AICallerContext
{
    public AICallerContext(string subjectId, string? tenantId = null)
    {
        SubjectId = string.IsNullOrWhiteSpace(subjectId)
            ? throw new ArgumentException("A caller subject ID is required.", nameof(subjectId))
            : subjectId.Trim();
        TenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim();
    }

    public string SubjectId { get; }

    public string? TenantId { get; }
}

public sealed record AIExecutionContext(
    string ExecutionId,
    AICallerContext Caller,
    string? SessionId = null,
    string? ProfileId = null,
    IAIExecutionObserver? Observer = null)
{
    public static AIExecutionContext Create(
        AICallerContext caller,
        string? sessionId = null) =>
        new(Guid.NewGuid().ToString("N"), caller, sessionId);
}

public enum AIEntityKind
{
    Agent,
    Workflow,
}

public sealed record AIEntityDescriptor(
    string Id,
    AIEntityKind Kind,
    string Description,
    Type InputType,
    Type OutputType,
    bool SupportsSessions,
    string? AuthorizationPolicy,
    bool AllowAnonymous);

public interface IAIEntityRegistry
{
    IReadOnlyList<AIEntityDescriptor> Entities { get; }

    AIEntityDescriptor? Find(string entityId);

    IAIEntity<TInput, TOutput> Resolve<TInput, TOutput>(string entityId);
}

public interface IAIEntityInvoker
{
    ValueTask<AIEntityInvocationResult> InvokeAsync(
        string entityId,
        JsonElement input,
        AIExecutionContext context,
        CancellationToken cancellationToken);
}

public sealed record AIEntityInvocationResult(
    string ExecutionId,
    string EntityId,
    string? SessionId,
    object? Output,
    AIOutcomeClassification Outcome = AIOutcomeClassification.Unclassified);

public sealed class AIEntityNotFoundException(string entityId)
    : KeyNotFoundException($"AI entity '{entityId}' is not registered.")
{
    public string EntityId { get; } = entityId;
}

public sealed class AIEntityInputException(string message, Exception? innerException = null)
    : ArgumentException(message, innerException);

public sealed class AIProviderFailureException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class AISessionConcurrencyException(string sessionId)
    : InvalidOperationException($"AI session '{sessionId}' was updated concurrently.")
{
    public string SessionId { get; } = sessionId;
}

public sealed class AISessionOwnershipException(string sessionId)
    : UnauthorizedAccessException($"AI session '{sessionId}' belongs to another caller.")
{
    public string SessionId { get; } = sessionId;
}

public sealed class AISessionNotSupportedException(string entityId)
    : ArgumentException($"AI entity '{entityId}' does not support sessions.")
{
    public string EntityId { get; } = entityId;
}

public enum AIOutcomeClassification { Unclassified, Succeeded, PartiallyFailed, Failed }

public sealed record AIExecutionEvent(string Kind, System.Text.Json.JsonElement Data, string? ExecutorId = null, string? InvocationId = null);

public interface IAIExecutionObserver
{
    void Record(AIExecutionEvent executionEvent);
}

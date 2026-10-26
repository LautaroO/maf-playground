using System.Text.Json.Serialization;
using MafPlayground.AI.Abstractions;
using Microsoft.Agents.AI.Workflows;

namespace MafPlayground.Studio.Tests.Fixtures;

[JsonConverter(typeof(JsonStringEnumConverter<ReviewPriority>))]
public enum ReviewPriority { Normal, Urgent }

public sealed record ReviewItem(string Title, ReviewPriority Priority, string? Note = null);
public sealed record ReviewRequest(
    [property: JsonPropertyName("review_items")] ReviewItem[] Items,
    int Attempts = 1);
public sealed record ReviewResult(string[] Titles, int Count);

public sealed class ReviewEntity : IAIEntity<ReviewRequest, ReviewResult>
{
    public ValueTask<ReviewResult> ExecuteAsync(
        ReviewRequest input,
        AIExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ReviewResult(
            input.Items.Select(item => item.Title).ToArray(), input.Items.Length));
    }
}

public sealed class UnconstructableEntity : IAIEntity<string, string>
{
    public UnconstructableEntity() => throw new InvalidOperationException("Discovery constructed an entity.");

    public ValueTask<string> ExecuteAsync(string input, AIExecutionContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Inspection executed an entity.");
}

public sealed class NeverRunExecutor(string id) : Executor<string, string>(id)
{
    public override ValueTask<string> HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Topology inspection executed a handler.");
}

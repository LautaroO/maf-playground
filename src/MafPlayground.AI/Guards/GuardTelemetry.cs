using System.Diagnostics;
using System.Diagnostics.Metrics;
using MafPlayground.AI.Guards.Content;

namespace MafPlayground.AI.Guards;

public static class GuardTelemetry
{
    public const string MeterName = "MafPlayground.AI.Guards";
    public const string DecisionMetricName = "maf_playground.ai.guard.decision.count";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Decisions = Meter.CreateCounter<long>(
        DecisionMetricName,
        description: "Number of deterministic AI guard decisions.");

    public static void RecordContentDecision(
        ContentOrigin origin,
        GuardAction action,
        IReadOnlyList<ContentFinding> findings)
    {
        TagList tags = new()
        {
            { "maf_playground.guard.name", "pii" },
            { "maf_playground.guard.origin", origin.ToString().ToLowerInvariant() },
            { "maf_playground.guard.action", action.ToString().ToLowerInvariant() },
        };
        Decisions.Add(1, tags);
        Activity.Current?.AddEvent(new ActivityEvent(
            "ai.guard.content",
            tags: new ActivityTagsCollection
            {
                { "maf_playground.guard.origin", origin.ToString().ToLowerInvariant() },
                { "maf_playground.guard.action", action.ToString().ToLowerInvariant() },
                { "maf_playground.guard.finding_count", findings.Count },
            }));
    }

    public static void RecordBudgetDecision(string action, string resource)
    {
        TagList tags = new()
        {
            { "maf_playground.guard.name", "budget" },
            { "maf_playground.guard.resource", resource },
            { "maf_playground.guard.action", action },
        };
        Decisions.Add(1, tags);
        Activity.Current?.AddEvent(new ActivityEvent(
            "ai.guard.budget",
            tags: new ActivityTagsCollection
            {
                { "maf_playground.guard.resource", resource },
                { "maf_playground.guard.action", action },
            }));
    }
}

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MafPlayground.AI;

public static class AITelemetry
{
    public const string AgentSourceName = "MafPlayground.Agents";
    public const string WorkflowSourceName = "Microsoft.Agents.AI.Workflows";

    public const string OperationMeterName = "MafPlayground.AI.Operations";
    public const string OperationCountMetricName = "maf_playground.ai.operation.count";
    public const string OperationFailureMetricName = "maf_playground.ai.operation.failure.count";
    public const string OperationDurationMetricName = "maf_playground.ai.operation.duration";

    public const string OperationNameTag = "maf_playground.operation.name";
    public const string EntityTypeTag = "maf_playground.entity.type";
    public const string EntityNameTag = "maf_playground.entity.name";
    public const string OutcomeTag = "maf_playground.outcome";
    public const string ErrorTypeTag = "error.type";

    private static readonly Meter OperationMeter = new(OperationMeterName);
    private static readonly Counter<long> OperationCount = OperationMeter.CreateCounter<long>(
        OperationCountMetricName,
        description: "Number of AI application operations by outcome.");
    private static readonly Counter<long> OperationFailureCount =
        OperationMeter.CreateCounter<long>(
            OperationFailureMetricName,
            description: "Number of failed AI application operations by error category.");
    private static readonly Histogram<double> OperationDuration =
        OperationMeter.CreateHistogram<double>(
            OperationDurationMetricName,
            unit: "s",
            description: "Duration of AI application operations in seconds.");

    public static void RecordOperation(
        string operationName,
        string entityType,
        string entityName,
        string outcome,
        TimeSpan duration,
        string? errorType = null,
        string? providerName = null,
        string? modelName = null,
        string? branchName = null)
    {
        TagList tags = new()
        {
            { OperationNameTag, operationName },
            { EntityTypeTag, entityType },
            { EntityNameTag, entityName },
            { OutcomeTag, outcome },
        };
        if (!string.IsNullOrWhiteSpace(errorType))
        {
            tags.Add(ErrorTypeTag, errorType);
        }
        if (!string.IsNullOrWhiteSpace(providerName))
        {
            tags.Add("gen_ai.provider.name", providerName);
        }
        if (!string.IsNullOrWhiteSpace(modelName))
        {
            tags.Add("gen_ai.request.model", modelName);
        }
        if (!string.IsNullOrWhiteSpace(branchName))
        {
            tags.Add("maf_playground.workflow.branch", branchName);
        }

        OperationCount.Add(1, tags);
        OperationDuration.Record(Math.Max(0, duration.TotalSeconds), tags);
        if (!string.IsNullOrWhiteSpace(errorType))
        {
            OperationFailureCount.Add(1, tags);
        }

        Activity? activity = Activity.Current;
        activity?.SetTag(OperationNameTag, operationName);
        activity?.SetTag(EntityTypeTag, entityType);
        activity?.SetTag(EntityNameTag, entityName);
        activity?.SetTag(OutcomeTag, outcome);
        activity?.SetTag("gen_ai.provider.name", providerName);
        activity?.SetTag("gen_ai.request.model", modelName);
        activity?.SetTag("maf_playground.workflow.branch", branchName);
        if (!string.IsNullOrWhiteSpace(errorType))
        {
            activity?.SetTag(ErrorTypeTag, errorType);
            activity?.SetStatus(ActivityStatusCode.Error, errorType);
        }
    }
}

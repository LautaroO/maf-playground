using MafPlayground.AI.Agents.BasicRagAgent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace MafPlayground.Evals.RepositoryHelp;

public sealed class RepositoryHelpContractEvaluator : IEvaluator
{
    public const string ContractPassMetricName = "RepositoryHelp.ContractPass";
    public const string FactCoverageMetricName = "RepositoryHelp.FactCoverage";
    public const string CitationCoverageMetricName = "RepositoryHelp.CitationCoverage";
    public const string CommandAccuracyMetricName = "RepositoryHelp.CommandAccuracy";
    public const string RefusalAccuracyMetricName = "RepositoryHelp.RefusalAccuracy";

    private static readonly string[] MetricNames =
    [
        ContractPassMetricName,
        FactCoverageMetricName,
        CitationCoverageMetricName,
        CommandAccuracyMetricName,
        RefusalAccuracyMetricName,
    ];

    public IReadOnlyCollection<string> EvaluationMetricNames => MetricNames;

    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(modelResponse);
        cancellationToken.ThrowIfCancellationRequested();
        RepositoryHelpEvaluationContext context = additionalContext?
            .OfType<RepositoryHelpEvaluationContext>()
            .SingleOrDefault() ?? throw new ArgumentException(
                $"{nameof(RepositoryHelpEvaluationContext)} is required.",
                nameof(additionalContext));
        RepositoryHelpExpectations expected = context.Expectations;
        string response = modelResponse.Text ?? string.Empty;

        double factCoverage = Coverage(expected.ExpectedFacts, response);
        double citationCoverage = Coverage(expected.ExpectedCitations, response);
        bool commandAccurate = expected.ExpectedInvocation is null || response.Contains(
            $"`{expected.ExpectedInvocation}`",
            StringComparison.Ordinal);
        bool refusalAccurate = expected.ShouldRefuse
            ? string.Equals(
                response.Trim(),
                CitationValidator.NoEvidenceAnswer,
                StringComparison.Ordinal)
            : !string.Equals(
                response.Trim(),
                CitationValidator.NoEvidenceAnswer,
                StringComparison.Ordinal);
        bool passed = factCoverage == 1 &&
            citationCoverage == 1 &&
            commandAccurate &&
            refusalAccurate;

        EvaluationMetric[] metrics =
        [
            CreateBooleanMetric(
                ContractPassMetricName,
                passed,
                passed ? "All deterministic repository-help contracts passed." :
                    "One or more deterministic repository-help contracts failed."),
            CreateNumericMetric(
                FactCoverageMetricName,
                factCoverage,
                "Fraction of required literal facts present in the response."),
            CreateNumericMetric(
                CitationCoverageMetricName,
                citationCoverage,
                "Fraction of required citations present in the response."),
            CreateBooleanMetric(
                CommandAccuracyMetricName,
                commandAccurate,
                expected.ExpectedInvocation is null
                    ? "No exact command was required."
                    : "The expected live-catalog invocation must appear verbatim in inline code."),
            CreateBooleanMetric(
                RefusalAccuracyMetricName,
                refusalAccurate,
                expected.ShouldRefuse
                    ? "The no-evidence response must match the deterministic refusal contract."
                    : "Supported cases must not return the no-evidence response."),
        ];
        foreach (EvaluationMetric metric in metrics)
        {
            metric.AddOrUpdateContext(context);
        }

        return ValueTask.FromResult(new EvaluationResult(metrics));
    }

    private static double Coverage(IReadOnlyList<string> expected, string response)
    {
        if (expected.Count == 0)
        {
            return 1;
        }

        int matches = expected.Count(item => response.Contains(
            item,
            StringComparison.OrdinalIgnoreCase));
        return (double)matches / expected.Count;
    }

    private static BooleanMetric CreateBooleanMetric(
        string name,
        bool value,
        string reason) => new(name, value, reason)
        {
            Interpretation = new EvaluationMetricInterpretation(
                value ? EvaluationRating.Good : EvaluationRating.Unacceptable,
                failed: !value,
                reason),
        };

    private static NumericMetric CreateNumericMetric(
        string name,
        double value,
        string reason) => new(name, value, reason)
        {
            Interpretation = new EvaluationMetricInterpretation(
                value == 1 ? EvaluationRating.Good : EvaluationRating.Unacceptable,
                failed: value != 1,
                reason),
        };
}

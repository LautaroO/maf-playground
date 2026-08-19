using System.Text.Json;
using Microsoft.Extensions.AI.Evaluation;

namespace MafPlayground.Evals.RepositoryHelp;

public sealed record RepositoryHelpExpectations(
    bool ShouldRefuse,
    IReadOnlyList<string> ExpectedFacts,
    IReadOnlyList<string> ExpectedCitations,
    string? ExpectedInvocation);

public sealed class RepositoryHelpEvaluationContext : EvaluationContext
{
    public const string ContextName = "repository-help-expectations";

    public RepositoryHelpEvaluationContext(RepositoryHelpExpectations expectations)
        : base(ContextName, JsonSerializer.Serialize(expectations))
    {
        Expectations = expectations;
    }

    public RepositoryHelpExpectations Expectations { get; }
}

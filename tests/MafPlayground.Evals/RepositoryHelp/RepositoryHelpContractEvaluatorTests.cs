using MafPlayground.AI.Agents.BasicRagAgent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MafPlayground.Evals.RepositoryHelp;

public sealed class RepositoryHelpContractEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_PassesExactFactsCitationsAndLiveCommand()
    {
        RepositoryHelpContractEvaluator evaluator = new();
        RepositoryHelpEvaluationContext context = new(new RepositoryHelpExpectations(
            ShouldRefuse: false,
            ExpectedFacts: ["DevUI"],
            ExpectedCitations: ["[CLI, source: cli-reference.md]"],
            ExpectedInvocation: "dotnet run --project src/MafPlayground.CLI -- devui"));
        const string response =
            "Run `dotnet run --project src/MafPlayground.CLI -- devui` to start DevUI. [CLI, source: cli-reference.md]";

        EvaluationResult result = await evaluator.EvaluateAsync(
            [new ChatMessage(ChatRole.User, "How do I run DevUI?")],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, response)),
            additionalContext: [context]);

        Assert.True(result.Get<BooleanMetric>(
            RepositoryHelpContractEvaluator.ContractPassMetricName).Value);
        Assert.False(result.Get<BooleanMetric>(
            RepositoryHelpContractEvaluator.ContractPassMetricName)
            .Interpretation?.Failed);
    }

    [Fact]
    public async Task EvaluateAsync_FailsWhenCommandIsNotCopiedVerbatim()
    {
        RepositoryHelpContractEvaluator evaluator = new();
        RepositoryHelpEvaluationContext context = new(new RepositoryHelpExpectations(
            ShouldRefuse: false,
            ExpectedFacts: [],
            ExpectedCitations: [],
            ExpectedInvocation: "dotnet run --project src/MafPlayground.CLI -- devui"));

        EvaluationResult result = await evaluator.EvaluateAsync(
            [new ChatMessage(ChatRole.User, "How do I run DevUI?")],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Run `devui`.")),
            additionalContext: [context]);

        Assert.False(result.Get<BooleanMetric>(
            RepositoryHelpContractEvaluator.CommandAccuracyMetricName).Value);
        Assert.True(result.Get<BooleanMetric>(
            RepositoryHelpContractEvaluator.ContractPassMetricName)
            .Interpretation?.Failed);
    }

    [Fact]
    public async Task EvaluateAsync_PassesDeterministicNoEvidenceRefusal()
    {
        RepositoryHelpContractEvaluator evaluator = new();
        RepositoryHelpEvaluationContext context = new(new RepositoryHelpExpectations(
            ShouldRefuse: true,
            ExpectedFacts: [],
            ExpectedCitations: [],
            ExpectedInvocation: null));

        EvaluationResult result = await evaluator.EvaluateAsync(
            [new ChatMessage(ChatRole.User, "What is the production API key?")],
            new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                CitationValidator.NoEvidenceAnswer)),
            additionalContext: [context]);

        Assert.True(result.Get<BooleanMetric>(
            RepositoryHelpContractEvaluator.RefusalAccuracyMetricName).Value);
    }
}

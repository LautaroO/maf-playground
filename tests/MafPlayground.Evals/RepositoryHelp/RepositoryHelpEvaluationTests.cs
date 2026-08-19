using System.Diagnostics;
using MafPlayground.AI;
using MafPlayground.AI.Agents.RepositoryHelpAgent;
using MafPlayground.AI.Configuration;
using MafPlayground.AI.Resilience;
using MafPlayground.CLI;
using MafPlayground.CLI.Documentation;
using MafPlayground.Providers.Google;
using MafPlayground.Providers.Ollama;
using MafPlayground.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace MafPlayground.Evals.RepositoryHelp;

public sealed class RepositoryHelpEvaluationTests(ITestOutputHelper output)
{
    private const string CliCitation =
        "[MafPlayground CLI reference, source: cli-reference.md]";

    [ModelEvaluationFact]
    public async Task RepositoryHelp_MeetsDatasetContractsAndRecordsQualityMetrics()
    {
        IReadOnlyList<RepositoryHelpEvalCase> cases = await
            RepositoryHelpEvalDataset.LoadAsync(
                RepositoryHelpDatasetTests.GetDatasetPath());
        AIModelSelection subjectModel = ParseRequiredSelection("AI_MODEL");
        AIModelSelection judgeModel = ParseOptionalSelection(
            "EVALUATION_JUDGE_MODEL",
            subjectModel);
        IConfiguration configuration = CreateProviderConfiguration();
        IRepositoryCliCommandCatalog commandCatalog =
            new SystemCommandLineRepositoryCliCommandCatalog(
                Parser.CreateRootCommand());
        using ServiceProvider judgeServices = CreateJudgeServices(
            judgeModel,
            configuration);
        IChatClient judgeClient = judgeServices.GetRequiredService<IChatClient>();
        string executionName = Environment.GetEnvironmentVariable(
            "EVALUATION_EXECUTION_NAME") ??
            $"local-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        string resultsPath = Environment.GetEnvironmentVariable(
            "EVALUATION_RESULTS_PATH") ?? Path.Combine(
                Directory.GetCurrentDirectory(),
                "eval-results",
                "repository-help");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(15));
        List<string> failedContracts = [];

        foreach (RepositoryHelpEvalCase evalCase in cases)
        {
            IReadOnlyList<KnowledgeSearchResult> searchResults =
                evalCase.ToSearchResults();
            using ServiceProvider subjectServices = CreateSubjectServices(
                subjectModel,
                configuration,
                commandCatalog,
                searchResults);
            RepositoryHelpAgent agent = subjectServices
                .GetRequiredService<RepositoryHelpAgent>();
            Stopwatch subjectDuration = Stopwatch.StartNew();
            string response = (await agent.Agent.RunAsync(
                evalCase.Question,
                cancellationToken: timeout.Token)).Text;
            subjectDuration.Stop();

            RepositoryHelpExpectations expectations = CreateExpectations(
                evalCase,
                searchResults,
                commandCatalog);
            IReadOnlyList<EvaluationContext> contexts = CreateContexts(
                expectations,
                searchResults,
                commandCatalog,
                evalCase.ExpectedCommandPath);
            IEvaluator[] evaluators = CreateEvaluators(
                contexts.OfType<GroundednessEvaluatorContext>().Any(),
                searchResults.Count > 0);
            ReportingConfiguration reporting = DiskBasedReportingConfiguration.Create(
                storageRootPath: resultsPath,
                evaluators: evaluators,
                chatConfiguration: new ChatConfiguration(judgeClient),
                enableResponseCaching: true,
                cachingKeys: [subjectModel.ToString(), judgeModel.ToString(), "repository-help.v1"],
                executionName: executionName,
                tags: [evalCase.Category, evalCase.ExpectedLanguage]);
            await using ScenarioRun scenario = await reporting.CreateScenarioRunAsync(
                scenarioName: evalCase.Id,
                cancellationToken: timeout.Token);
            EvaluationResult result = await scenario.EvaluateAsync(
                [new ChatMessage(ChatRole.User, evalCase.Question)],
                new ChatResponse(new ChatMessage(ChatRole.Assistant, response))
                {
                    ModelId = subjectModel.Model,
                },
                contexts,
                timeout.Token);
            result.AddOrUpdateMetadataInAllMetrics(
                "subject-model",
                subjectModel.ToString());
            result.AddOrUpdateMetadataInAllMetrics(
                "subject-duration-ms",
                subjectDuration.Elapsed.TotalMilliseconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

            BooleanMetric contract = result.Get<BooleanMetric>(
                RepositoryHelpContractEvaluator.ContractPassMetricName);
            output.WriteLine("{0}: {1}", evalCase.Id, response);
            foreach (EvaluationMetric metric in result.Metrics.Values)
            {
                output.WriteLine(
                    "  {0}: {1}; rating={2}; failed={3}; reason={4}",
                    metric.Name,
                    metric is NumericMetric numeric ? numeric.Value :
                        metric is BooleanMetric boolean ? boolean.Value : null,
                    metric.Interpretation?.Rating,
                    metric.Interpretation?.Failed,
                    metric.Reason);
                foreach (EvaluationDiagnostic diagnostic in metric.Diagnostics ?? [])
                {
                    output.WriteLine(
                        "    {0}: {1}",
                        diagnostic.Severity,
                        diagnostic.Message);
                }
            }

            if (contract.Value != true)
            {
                failedContracts.Add(evalCase.Id);
            }
        }

        Assert.True(
            failedContracts.Count == 0,
            $"Deterministic contracts failed for: {string.Join(", ", failedContracts)}.");
    }

    private static IEvaluator[] CreateEvaluators(
        bool hasGroundingContext,
        bool hasRetrievedContext)
    {
        List<IEvaluator> evaluators =
        [
            new RepositoryHelpContractEvaluator(),
            new RelevanceEvaluator(),
        ];
        if (hasGroundingContext)
        {
            evaluators.Add(new GroundednessEvaluator());
        }
        if (hasRetrievedContext)
        {
            evaluators.Add(new RetrievalEvaluator());
        }
        return [.. evaluators];
    }

    private static IReadOnlyList<EvaluationContext> CreateContexts(
        RepositoryHelpExpectations expectations,
        IReadOnlyList<KnowledgeSearchResult> searchResults,
        IRepositoryCliCommandCatalog commandCatalog,
        string? commandPath)
    {
        List<EvaluationContext> contexts =
        [new RepositoryHelpEvaluationContext(expectations)];
        List<string> grounding = searchResults
            .Select(result => result.Text)
            .ToList();
        if (commandPath is not null && commandCatalog.Find(commandPath) is { } command)
        {
            grounding.Add(
                $"Command `{command.CommandPath}` uses the exact invocation `{command.Invocation}`. {command.Description}");
        }
        if (grounding.Count > 0)
        {
            contexts.Add(new GroundednessEvaluatorContext(
                string.Join(Environment.NewLine, grounding)));
        }
        if (searchResults.Count > 0)
        {
            contexts.Add(new RetrievalEvaluatorContext(
                searchResults.Select(result => result.Text)));
        }
        return contexts;
    }

    private static RepositoryHelpExpectations CreateExpectations(
        RepositoryHelpEvalCase evalCase,
        IReadOnlyList<KnowledgeSearchResult> searchResults,
        IRepositoryCliCommandCatalog commandCatalog)
    {
        RepositoryCliCommand? expectedCommand = evalCase.ExpectedCommandPath is null
            ? null
            : commandCatalog.Find(evalCase.ExpectedCommandPath) ??
                throw new InvalidDataException(
                    $"Dataset case '{evalCase.Id}' references unknown command path '{evalCase.ExpectedCommandPath}'.");
        string[] citations =
        [
            .. searchResults.Select(result => result.Citation),
            .. expectedCommand is null ? [] : new[] { CliCitation },
        ];
        return new RepositoryHelpExpectations(
            evalCase.ShouldRefuse,
            evalCase.ExpectedFacts,
            citations.Distinct(StringComparer.Ordinal).ToArray(),
            expectedCommand?.Invocation);
    }

    private static ServiceProvider CreateSubjectServices(
        AIModelSelection selection,
        IConfiguration configuration,
        IRepositoryCliCommandCatalog commandCatalog,
        IReadOnlyList<KnowledgeSearchResult> searchResults)
    {
        ServiceCollection services = new();
        AddProviders(services, configuration);
        services
            .AddAICore(selection)
            .AddRepositoryHelpAgent();
        services.AddSingleton<IKnowledgeSearchFactory>(
            new FixtureKnowledgeSearchFactory(searchResults));
        services.AddSingleton(commandCatalog);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateJudgeServices(
        AIModelSelection selection,
        IConfiguration configuration)
    {
        ServiceCollection services = new();
        AddProviders(services, configuration);
        services.AddAICore(selection);
        services.Configure<AIResilienceOptions>(options =>
            options.ModelCallTimeout = TimeSpan.FromMinutes(3));
        return services.BuildServiceProvider();
    }

    private static void AddProviders(
        IServiceCollection services,
        IConfiguration configuration) => services
        .AddGoogleGenAIProvider(configuration)
        .AddOllamaProvider(configuration);

    private static IConfiguration CreateProviderConfiguration()
    {
        Dictionary<string, string?> values = new()
        {
            [$"{OllamaProviderOptions.ConfigurationSectionName}:Endpoint"] =
                Environment.GetEnvironmentVariable(
                    "AI__PROVIDERS__OLLAMA__ENDPOINT") ??
                "http://localhost:11434",
            [$"{GoogleGenAIProviderOptions.ConfigurationSectionName}:ApiKey"] =
                Environment.GetEnvironmentVariable("GEMINI_API_KEY") ??
                Environment.GetEnvironmentVariable("GOOGLE_API_KEY"),
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static AIModelSelection ParseRequiredSelection(string variable)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        if (!AIModelSelection.TryParse(value, out AIModelSelection? selection))
        {
            throw new InvalidOperationException(
                $"{variable} must use provider:model format.");
        }
        return selection;
    }

    private static AIModelSelection ParseOptionalSelection(
        string variable,
        AIModelSelection fallback)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : AIModelSelection.Parse(value);
    }
}

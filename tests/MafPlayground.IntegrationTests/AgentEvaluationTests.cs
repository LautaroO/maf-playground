using System.Diagnostics;
using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Workflows.Translation;
using MafPlayground.Providers.Ollama;
using MafPlayground.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace MafPlayground.IntegrationTests;

[Collection(OllamaCollection.Name)]
public sealed class AgentEvaluationTests(ITestOutputHelper output)
{
    [ModelEvaluationFact]
    public async Task BasicRag_GroundsSupportedFactAndRefusesUnsupportedQuestion()
    {
        ServiceCollection services = new();
        services.AddOllamaProvider(OllamaProviderContractTests.CreateConfiguration());
        services.AddSingleton<IKnowledgeSearchFactory>(new EvaluationSearchFactory());
        services
            .AddAICore(OllamaProviderContractTests.GetOllamaSelection())
            .AddBasicRagAgent();
        using ServiceProvider provider = services.BuildServiceProvider();
        BasicRagAgent agent = provider.GetRequiredService<BasicRagAgent>();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));

        string supported = (await agent.Agent.RunAsync(
            "How long does a password-reset link remain valid?",
            cancellationToken: timeout.Token)).Text;
        string unsupported = (await agent.Agent.RunAsync(
            "What is the launch code for the Mars office?",
            cancellationToken: timeout.Token)).Text;

        Assert.Contains("30", supported, StringComparison.Ordinal);
        Assert.Contains("[Help, page 2, source: help.pdf]", supported, StringComparison.Ordinal);
        Assert.Equal(CitationValidator.NoEvidenceAnswer, unsupported);
    }

    [ModelEvaluationFact]
    public async Task Translation_PreservesNumbersAcrossLanguages()
    {
        ServiceCollection services = new();
        services.AddOllamaProvider(OllamaProviderContractTests.CreateConfiguration());
        services
            .AddAICore(OllamaProviderContractTests.GetOllamaSelection())
            .AddTranslationWorkflow();
        services.Configure<TranslationWorkflowOptions>(options =>
        {
            options.SupportedTargetLanguages = ["es", "fr"];
            options.MaxTranslationRetries = 1;
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        TranslationWorkflowRunner runner = provider
            .GetRequiredService<TranslationWorkflowRunner>();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));

        TranslationWorkflowResult result = await runner.RunAsync(
            new TranslationWorkflowRequest(
                "The invoice is due in 14 days.",
                ["es", "fr"]),
            timeout.Token);

        Assert.All(result.Translations, translation =>
        {
            Assert.True(translation.IsValid, translation.Error);
            Assert.Contains("14", translation.TranslatedText, StringComparison.Ordinal);
        });
    }

    [ModelEvaluationFact]
    public async Task Translation_RepairsReportedIssuesAcrossLanguages()
    {
        ServiceCollection services = new();
        services.AddOllamaProvider(OllamaProviderContractTests.CreateConfiguration());
        services.AddAICore(OllamaProviderContractTests.GetOllamaSelection());
        services.AddSingleton<ITranslationModel>(provider =>
            new ForcedRepairTranslationModel(
                new ChatClientTranslationModel(provider.GetRequiredService<IChatClient>()),
                output));
        services.AddTranslationWorkflow();
        services.Configure<TranslationWorkflowOptions>(options =>
        {
            options.SupportedTargetLanguages = ["es", "fr", "pt-BR"];
            options.MaxTranslationRetries = 1;
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        TranslationWorkflowRunner runner = provider
            .GetRequiredService<TranslationWorkflowRunner>();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(5));
        const string source =
            "Your order 247 is ready for pickup at 18:30.";
        IReadOnlyDictionary<string, string> expectedPlacement =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["es"] = "pedido 247",
                ["fr"] = "commande 247",
                ["pt-BR"] = "pedido 247",
            };
        foreach (string language in expectedPlacement.Keys)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            TranslationWorkflowResult result = await runner.RunAsync(
                new TranslationWorkflowRequest(source, [language]),
                timeout.Token);
            elapsed.Stop();
            ValidatedTranslation translation = Assert.Single(result.Translations);
            output.WriteLine(
                "{0}: {1} ms; attempts={2}; repaired={3}",
                translation.TargetLanguage,
                elapsed.ElapsedMilliseconds,
                translation.Attempts,
                translation.TranslatedText);
            Assert.True(translation.IsValid, translation.Error);
            Assert.Equal(2, translation.Attempts);
            Assert.Empty(translation.Issues);
            Assert.Contains("247", translation.TranslatedText, StringComparison.Ordinal);
            Assert.Contains("18:30", translation.TranslatedText, StringComparison.Ordinal);
            Assert.Contains(
                expectedPlacement[language],
                translation.TranslatedText,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class ForcedRepairTranslationModel(
        ITranslationModel model,
        ITestOutputHelper output)
        : ITranslationModel
    {
        private static readonly IReadOnlyDictionary<string, string> InitialDrafts =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["es"] = "Su pedido está listo para recoger a las 18:30.",
                ["fr"] = "Votre commande est prête à être retirée à 18:30.",
                ["pt-BR"] = "Seu pedido está pronto para retirada às 18:30.",
            };

        public async Task<string> TranslateAsync(
            TranslationDraftRequest request,
            CancellationToken cancellationToken)
        {
            if (request.ValidationFeedback is null)
            {
                return InitialDrafts[request.TargetLanguage];
            }

            try
            {
                return await model.TranslateAsync(request, cancellationToken);
            }
            catch (Exception exception)
            {
                output.WriteLine("repair exception: {0}", exception);
                throw;
            }
        }

        public Task<TranslationValidation> ValidateAsync(
            TranslationValidationRequest request,
            CancellationToken cancellationToken) =>
            request.PreviousBlockingIssues.Count == 0
                ? Task.FromResult(new TranslationValidation(
                    false,
                    1,
                    [
                        new TranslationIssue(
                            TranslationIssueSeverity.Blocking,
                            TranslationIssueCode.MissingData,
                            "Restore order number 247 immediately after the translated word for order."),
                    ]))
                : model.ValidateAsync(request, cancellationToken);
    }

    private sealed class EvaluationSearchFactory : IKnowledgeSearchFactory
    {
        public IKnowledgeSearch Create(
            KnowledgeBaseId knowledgeBaseId,
            KnowledgeSearchOptions searchOptions) => new EvaluationSearch();
    }

    private sealed class EvaluationSearch : IKnowledgeSearch
    {
        public Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<KnowledgeSearchResult> results = query.Contains(
                "password",
                StringComparison.OrdinalIgnoreCase)
                ? [new KnowledgeSearchResult(
                    "help.pdf",
                    "Help",
                    "Password-reset links are single-use and expire after 30 minutes.",
                    2,
                    "Passwords",
                    0.99)]
                : [];
            return Task.FromResult(results);
        }
    }
}

using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Workflows.Translation;
using MafPlayground.Providers.Ollama;
using MafPlayground.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MafPlayground.IntegrationTests;

[Collection(OllamaCollection.Name)]
public sealed class AgentEvaluationTests
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

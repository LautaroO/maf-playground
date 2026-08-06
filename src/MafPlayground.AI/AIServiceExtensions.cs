using MafPlayground.AI.Guards;
using MafPlayground.AI.Guards.Budget;
using MafPlayground.AI.Guards.Content;
using MafPlayground.AI.Resilience;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI;

public static class AIServiceExtensions
{
    public static IServiceCollection AddAIServices(
        this IServiceCollection serviceCollection,
        AIModelSelection modelSelection)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);
        ArgumentNullException.ThrowIfNull(modelSelection);

        serviceCollection.AddOptions<AgentTelemetryOptions>();
        serviceCollection.AddOptions<AIGuardOptions>()
            .Validate(ValidateGuardOptions, "AI guard configuration is invalid.");
        serviceCollection.AddOptions<Agents.BasicAgent.BasicAgentOptions>()
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.GuardProfile),
                "Basic agent requires a guard profile.");
        serviceCollection.AddOptions<AIResilienceOptions>()
            .Validate(
                options => options.ModelCallTimeout > TimeSpan.Zero,
                "AI:Resilience:ModelCallTimeout must be greater than zero.");
        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton(modelSelection);
        serviceCollection.AddSingleton<AIProviderRegistry>();
        serviceCollection.AddSingleton<GuardProfileResolver>();
        serviceCollection.AddSingleton<GuardExecutionContextAccessor>();
        serviceCollection.AddSingleton<IContentInspector, RegexPiiContentInspector>();
        serviceCollection.AddSingleton<ContentGuard>();
        serviceCollection.AddSingleton<AgentGuardPipeline>();
        serviceCollection.AddSingleton<WorkflowGuardCoordinator>();
        serviceCollection.AddSingleton<IChatClientDecorator, TimeoutChatClientDecorator>();
        serviceCollection.AddSingleton<IChatClientDecorator, BudgetChatClientDecorator>();
        serviceCollection.AddSingleton<IChatClientDecorator, ContentGuardChatClientDecorator>();
        serviceCollection.AddSingleton<IChatClient>(serviceProvider =>
        {
            IChatClient chatClient = serviceProvider
                .GetRequiredService<AIProviderRegistry>()
                .CreateChatClient(modelSelection);

            foreach (IChatClientDecorator decorator in
                serviceProvider.GetServices<IChatClientDecorator>())
            {
                chatClient = decorator.Decorate(chatClient, modelSelection);
            }

            return chatClient;
        });
        serviceCollection.AddSingleton<Tools.CurrentDateTimeTool>();
        serviceCollection.AddSingleton<UserContextProvider>();
        serviceCollection.AddSingleton<Agents.BasicAgent.BasicAgent>();
        serviceCollection.AddOptions<Agents.BasicRagAgent.BasicRagAgentOptions>()
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.KnowledgeBase),
                "Basic RAG agent requires a knowledge base.")
            .Validate(
                options => options.Retrieval.TopK > 0,
                "Basic RAG agent retrieval TopK must be greater than zero.")
            .Validate(
                options => options.Retrieval.MinimumSimilarity is >= 0 and <= 1,
                "Basic RAG agent retrieval MinimumSimilarity must be between zero and one.")
            .Validate(
                options => options.Retrieval.MaximumAdditionalSearches >= 0,
                "Basic RAG agent retrieval MaximumAdditionalSearches cannot be negative.")
            .Validate(
                options => options.Retrieval.MaximumQueryCharacters > 0,
                "Basic RAG agent retrieval MaximumQueryCharacters must be greater than zero.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.GuardProfile),
                "Basic RAG agent requires a guard profile.");
        serviceCollection.AddSingleton<Agents.BasicRagAgent.RagInvocationContextAccessor>();
        serviceCollection.AddSingleton<Agents.BasicRagAgent.RagContextProvider>(provider =>
        {
            Agents.BasicRagAgent.BasicRagAgentOptions options = provider
                .GetRequiredService<IOptions<Agents.BasicRagAgent.BasicRagAgentOptions>>()
                .Value;
            Retrieval.IKnowledgeSearch search = provider
                .GetRequiredService<Retrieval.IKnowledgeSearchFactory>()
                .Create(
                    new Retrieval.KnowledgeBaseId(options.KnowledgeBase),
                    new Retrieval.KnowledgeSearchOptions
                    {
                        TopK = options.Retrieval.TopK,
                        MinimumSimilarity = options.Retrieval.MinimumSimilarity,
                        MaximumQueryCharacters = options.Retrieval.MaximumQueryCharacters,
                        MetadataFilters = options.Retrieval.MetadataFilters,
                    });
            return new Agents.BasicRagAgent.RagContextProvider(
                search,
                options.Retrieval,
                provider.GetRequiredService<Agents.BasicRagAgent.RagInvocationContextAccessor>(),
                provider.GetRequiredService<ContentGuard>(),
                provider.GetRequiredService<GuardProfileResolver>().Resolve(options.GuardProfile));
        });
        serviceCollection.AddSingleton<Agents.BasicRagAgent.CitationValidator>();
        serviceCollection.AddSingleton<Agents.BasicRagAgent.BasicRagAgent>();
        serviceCollection.AddOptions<Workflows.Translation.TranslationWorkflowOptions>()
            .Validate(
                options => options.MaxTargetLanguages > 0,
                "TranslationWorkflow:MaxTargetLanguages must be greater than zero.")
            .Validate(
                options => options.MaxInputCharacters > 0,
                "TranslationWorkflow:MaxInputCharacters must be greater than zero.")
            .Validate(
                options => options.MaxTranslationRetries >= 0,
                "TranslationWorkflow:MaxTranslationRetries cannot be negative.")
            .Validate(
                options => options.MinimumValidationConfidence is >= 0 and <= 1,
                "TranslationWorkflow:MinimumValidationConfidence must be between zero and one.")
            .Validate(
                options => options.SupportedTargetLanguages is { Length: > 0 },
                "TranslationWorkflow:SupportedTargetLanguages must contain at least one language.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.GuardProfile),
                "Translation workflow requires a guard profile.");
        serviceCollection.AddSingleton<Workflows.Translation.ITranslationModel,
            Workflows.Translation.ChatClientTranslationModel>();
        serviceCollection.AddSingleton<Workflows.Translation.TranslationService>();
        serviceCollection.AddSingleton<Workflows.Translation.TranslationWorkflowFactory>();
        serviceCollection.AddSingleton<Workflows.Translation.TranslationWorkflowRunner>();

        return serviceCollection;
    }

    private static bool ValidateGuardOptions(AIGuardOptions options)
    {
        if (options.Profiles.Count == 0)
        {
            return false;
        }

        foreach ((string name, GuardProfileOptions profile) in options.Profiles)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                profile.Content.MaxInputCharacters <= 0)
            {
                return false;
            }

            BudgetGuardOptions budget = profile.Budget;
            if (budget.MaxCostPerRun < 0 ||
                budget.MaxModelCalls <= 0 ||
                budget.MaxToolCalls <= 0 ||
                budget.MaxInputTokens <= 0 ||
                budget.MaxOutputTokens <= 0 ||
                budget.MaxOutputTokensPerCall <= 0 ||
                budget.MaxOutputTokensPerCall > budget.MaxOutputTokens ||
                budget.EstimatedCharactersPerToken <= 0 ||
                string.IsNullOrWhiteSpace(budget.Currency))
            {
                return false;
            }
        }

        return true;
    }
}

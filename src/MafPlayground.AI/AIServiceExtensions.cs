using MafPlayground.AI.Resilience;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        serviceCollection.AddOptions<AIResilienceOptions>()
            .Validate(
                options => options.ModelCallTimeout > TimeSpan.Zero,
                "AI:Resilience:ModelCallTimeout must be greater than zero.");
        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton(modelSelection);
        serviceCollection.AddSingleton<AIProviderRegistry>();
        serviceCollection.AddSingleton<IChatClientDecorator, TimeoutChatClientDecorator>();
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
        serviceCollection.AddSingleton<Agents.BasicRagAgent.RagInvocationContextAccessor>();
        serviceCollection.AddSingleton<Agents.BasicRagAgent.RagContextProvider>();
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
                "TranslationWorkflow:SupportedTargetLanguages must contain at least one language.");
        serviceCollection.AddSingleton<Workflows.Translation.ITranslationModel,
            Workflows.Translation.ChatClientTranslationModel>();
        serviceCollection.AddSingleton<Workflows.Translation.TranslationService>();
        serviceCollection.AddSingleton<Workflows.Translation.TranslationWorkflowFactory>();
        serviceCollection.AddSingleton<Workflows.Translation.TranslationWorkflowRunner>();

        return serviceCollection;
    }
}

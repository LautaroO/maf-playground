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
        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton(modelSelection);
        serviceCollection.AddSingleton<AIProviderRegistry>();
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
                options => options.MaxRepairAttempts >= 0,
                "TranslationWorkflow:MaxRepairAttempts cannot be negative.")
            .Validate(
                options => options.MinimumValidationConfidence is >= 0 and <= 1,
                "TranslationWorkflow:MinimumValidationConfidence must be between zero and one.")
            .Validate(
                options => options.ModelCallTimeout > TimeSpan.Zero,
                "TranslationWorkflow:ModelCallTimeout must be greater than zero.")
            .Validate(
                options => options.DevUITargetLanguages is { Length: > 0 },
                "TranslationWorkflow:DevUITargetLanguages must contain at least one language.");
        serviceCollection.AddSingleton<Workflows.Translation.ITranslationModel,
            Workflows.Translation.ChatClientTranslationModel>();
        serviceCollection.AddSingleton<Workflows.Translation.TranslationBranchProcessor>();
        serviceCollection.AddSingleton<Workflows.Translation.TranslationWorkflowFactory>();
        serviceCollection.AddSingleton<Workflows.Translation.TranslationWorkflowRunner>();

        return serviceCollection;
    }
}

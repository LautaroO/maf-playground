using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Runtime;
using MafPlayground.AI.Workflows.Translation;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.AI;

public static class SampleAIServiceExtensions
{
    public static AIBuilder AddBasicAgentEntity(this AIBuilder builder) =>
        AddBasicServices(builder).AddAgent<BasicAgentEntity, BasicAgentRequest, BasicAgentResponse>(
            "basic-agent",
            entity => entity
                .WithDescription("General conversational agent.")
                .BindRuntimeOptions("AI:Entities:basic-agent")
                .EnableSessions()
                .RequireAuthorization("AI.Basic"));

    public static AIBuilder AddTranslationWorkflowEntity(this AIBuilder builder) =>
        AddTranslationServices(builder).AddWorkflow<TranslationWorkflowEntity,
            TranslationWorkflowRequest,
            TranslationWorkflowResult>(
            "translation-workflow",
            entity => entity
                .WithDescription("Parallel validated translation.")
                .BindRuntimeOptions("AI:Entities:translation-workflow")
                .RequireAuthorization("AI.Translation"));

    public static AIBuilder AddMafPlaygroundSampleEntities(this AIBuilder builder) =>
        builder.AddBasicAgentEntity().AddTranslationWorkflowEntity();

    public static IServiceCollection AddAIServices(
        this IServiceCollection services,
        AIModelSelection modelSelection) => services
        .AddAICore(modelSelection)
        .AddBasicAgent()
        .AddBasicRagAgent()
        .AddTranslationWorkflow();

    private static AIBuilder AddBasicServices(AIBuilder builder)
    {
        builder.Services.AddBasicAgentEntityServices();
        return builder;
    }

    private static AIBuilder AddTranslationServices(AIBuilder builder)
    {
        builder.Services.AddTranslationWorkflow();
        return builder;
    }
}

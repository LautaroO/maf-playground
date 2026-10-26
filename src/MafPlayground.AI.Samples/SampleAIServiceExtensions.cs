using MafPlayground.AI.Agents;
using MafPlayground.AI.Agents.HelpAgent;
using MafPlayground.AI.Runtime.Registry;
using MafPlayground.Retrieval;
using MafPlayground.AI.Runtime.Agents;
using MafPlayground.AI.Runtime.Configuration;
using MafPlayground.AI.Runtime.Providers;
using MafPlayground.AI.Runtime.Sessions;
using MafPlayground.AI.Runtime.Workflows;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Observability;
using MafPlayground.AI.Tools;
using Microsoft.Extensions.Options;
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
                .WithInputExample(new BasicAgentRequest("What time is it for me?"))
                .WithMafAgentSource<IBasicAgentSource>()
                .WithProfileFactory<BasicAgentEntity>((services, context) => new(
                    new BasicAgentSource(services.GetRequiredService<IMafAgentFactory>(), services.GetRequiredService<CurrentDateTimeTool>(),
                        services.GetRequiredService<UserContextProvider>(), context.RuntimeOptions, context.RegisterResource), services.GetServices<IAgentSessionStore>()))
                .WithStreamingEvents()
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
                .WithInputExample(new TranslationWorkflowRequest("Hello, how are you?", ["es", "fr"]))
                .WithMafWorkflowSource<ITranslationWorkflowSource>()
                .WithProfileFactory<TranslationWorkflowEntity>((services, context) =>
                {
                    TranslationWorkflowOptions configured = services.GetRequiredService<IOptions<TranslationWorkflowOptions>>().Value;
                    var options = Options.Create(new TranslationWorkflowOptions
                    {
                        SupportedTargetLanguages = configured.SupportedTargetLanguages.ToArray(),
                        GuardProfile = context.RuntimeOptions.GuardProfile,
                        MaxTargetLanguages = configured.MaxTargetLanguages,
                        MaxInputCharacters = configured.MaxInputCharacters,
                        MaxTranslationRetries = configured.MaxTranslationRetries,
                        MinimumValidationConfidence = configured.MinimumValidationConfidence,
                    });
                    var guards = services.GetRequiredService<WorkflowGuardCoordinator>();
                    var model = new DeferredTranslationModel(() => new ChatClientTranslationModel(
                        context.Own(services.GetRequiredService<IAIChatClientFactory>().Create(context.RuntimeOptions))));
                    var factory = new TranslationWorkflowFactory(new TranslationService(model, options, guards), options,
                        services.GetRequiredService<IOptions<AgentTelemetryOptions>>(), guards);
                    return new TranslationWorkflowEntity(new TranslationWorkflowSource(factory), services.GetRequiredService<IMafWorkflowRunner>(),
                        services.GetRequiredService<IAIEntityRuntimeOptionsResolver>(), context.RuntimeOptions);
                })
                .WithStreamingEvents()
                .WithOutcomeClassifier<TranslationWorkflowResult>(TranslationOutcomeClassifier.Classify)
                .BindRuntimeOptions("AI:Entities:translation-workflow")
                .RequireAuthorization("AI.Translation"));

    public static AIBuilder AddMafPlaygroundSampleEntities(this AIBuilder builder) =>
        builder.AddBasicAgentEntity().AddBasicRagAgentEntity().AddHelpAgentEntity().AddTranslationWorkflowEntity();

    public static AIBuilder AddBasicRagAgentEntity(this AIBuilder builder)
    {
        builder.Services.AddBasicRagAgent();
        return builder.AddAgent<BasicRagAgentEntity, BasicRagAgentRequest, BasicRagAgentResponse>("basic-rag-agent", entity => entity
            .WithDescription("A grounded assistant answering from the configured knowledge base.")
            .WithInputExample(new BasicRagAgentRequest("How long does a password-reset link remain valid?"))
            .WithMafAgentSource((services, _) => new SampleAgentSource(() => services.GetRequiredService<BasicRagAgent>().Agent))
            .WithProfileFactory<BasicRagAgentEntity>((services, context) => new(() => GroundedAgentProfiles.CreateRag(services, context), services.GetServices<IAgentSessionStore>()))
            .WithAvailability(services => GroundedAvailability(services, help: false))
            .WithStreamingEvents().EnableSessions().RequireAuthorization("AI.BasicRag"));
    }

    public static AIBuilder AddHelpAgentEntity(this AIBuilder builder)
    {
        builder.Services.AddHelpAgentServices();
        return builder.AddAgent<HelpAgentEntity, HelpAgentRequest, HelpAgentResponse>("repository-help-agent", entity => entity
            .WithDescription("A grounded assistant for host-provided documentation and capabilities.")
            .WithInputExample(new HelpAgentRequest("What can this application do?"))
            .WithMafAgentSource((services, _) => new SampleAgentSource(() => services.GetRequiredService<HelpAgent>().Agent))
            .WithProfileFactory<HelpAgentEntity>((services, context) => new(() => GroundedAgentProfiles.CreateHelp(services, context), services.GetServices<IAgentSessionStore>()))
            .WithAvailability(services => GroundedAvailability(services, help: true))
            .WithStreamingEvents().EnableSessions().RequireAuthorization("AI.Help"));
    }

    private static AIEntityAvailability GroundedAvailability(IServiceProvider services, bool help)
    {
        IServiceProviderIsService registrations = services.GetRequiredService<IServiceProviderIsService>();
        if (!registrations.IsService(typeof(IKnowledgeSearchFactory))) return AIEntityAvailability.Unavailable("Knowledge search is not configured.");
        if (help && (!registrations.IsService(typeof(HelpAgentDefinition)) || !registrations.IsService(typeof(IHelpCapabilityCatalog))))
            return AIEntityAvailability.Unavailable("The host help definition and capability catalog must be configured.");
        return AIEntityAvailability.Available;
    }

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

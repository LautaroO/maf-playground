using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Agents.HelpAgent;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Resilience;
using MafPlayground.AI.Runtime;
using MafPlayground.AI.Workflows.Translation;
using MafPlayground.CLI.Documentation;
using MafPlayground.CLI.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.CLI.Extensions;

internal static class LocalRuntimeExtensions
{
    public static void AddLocalRuntime(this IServiceCollection services, IConfiguration configuration, AIModelSelection modelSelection, bool includeRetrievalAgents = true)
    {
        services.AddLocalUserContext();
        services
            .AddAICore(modelSelection)
            .AddBasicAgent()
            .AddTranslationWorkflow();
        if (includeRetrievalAgents)
        {
            services.AddBasicRagAgent().AddHelpAgent(RepositoryHelpAgentConfiguration.Definition);
        }
        services.ConfigureLocalEntityOptions(configuration);
        services.ConfigureAIEntities().AddMafPlaygroundSampleEntities();
        services.AddInMemoryAgentSessions();
        services.AddConfiguredAIProviders(configuration);
    }

    public static void ConfigureLocalEntityOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IHelpCapabilityCatalog>(
            new SystemCommandLineHelpCapabilityCatalog(Parser.CreateRootCommand()));
        services.Configure<BasicAgentOptions>(
            configuration.GetSection(BasicAgentOptions.ConfigurationSectionName));
        services.Configure<AIGuardOptions>(
            configuration.GetSection(AIGuardOptions.ConfigurationSectionName));
        services.Configure<BasicRagAgentOptions>(
            configuration.GetSection(BasicRagAgentOptions.ConfigurationSectionName));
        services.Configure<HelpAgentOptions>(
            configuration.GetSection(
                RepositoryHelpAgentConfiguration.ConfigurationSectionName));
        services.Configure<AIResilienceOptions>(
            configuration.GetSection(AIResilienceOptions.ConfigurationSectionName));
        services.Configure<TranslationWorkflowOptions>(
            configuration.GetSection("AI:Workflows:Translation"));
    }
}

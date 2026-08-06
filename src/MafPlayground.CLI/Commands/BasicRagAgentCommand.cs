using System.CommandLine;
using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Resilience;
using MafPlayground.Observability;
using MafPlayground.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MafPlayground.CLI.Commands;

public static class BasicRagAgentCommand
{
    public static Command Create(Func<BasicRagAgentCommandOptions, CancellationToken, Task<int>>? runAsync = null)
    {
        runAsync ??= RunAsync;
        Option<string?> model = new("--model", "-m") { Description = "Chat model in provider:model format. Falls back to AI_MODEL." };
        Option<string?> prompt = new("--prompt", "-p") { Description = "Run one prompt and exit. Omit for an interactive session." };
        Option<bool> watch = new("--watch") { Description = "Show agent lifecycle events while streaming." };
        Option<string[]> filters = new("--filter")
        {
            Description = "Require document metadata in key=value format. Repeat for multiple filters.",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        Command command = new("basic-rag", "Run the grounded Basic RAG agent.");
        command.Options.Add(model);
        command.Options.Add(prompt);
        command.Options.Add(watch);
        command.Options.Add(filters);
        command.SetAction((result, cancellationToken) => runAsync(new(result.GetValue(model), result.GetValue(prompt), result.GetValue(watch), result.GetValue(filters) ?? []), cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(BasicRagAgentCommandOptions commandOptions, CancellationToken cancellationToken)
    {
        using ILoggerFactory loggerFactory = CommandLogging.CreateLoggerFactory();
        ILogger logger = loggerFactory.CreateLogger(typeof(BasicRagAgentCommand));
        KnowledgeMetadata filterOverrides;
        try
        {
            filterOverrides = MetadataOptionParser.Parse(
                commandOptions.Filters,
                "--filter");
        }
        catch (ArgumentException exception)
        {
            logger.LogError("{ErrorMessage}", exception.Message);
            return 2;
        }
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();

        if (!AIModelSelection.TryParse(commandOptions.Model ?? builder.Configuration["AI_MODEL"], out AIModelSelection? chatSelection))
        {
            logger.LogError("A chat model is required in provider:model format. Use --model or AI_MODEL");
            return 2;
        }
        builder.Services.AddLocalUserContext();
        builder.Services.AddAIServices(chatSelection);
        builder.Services.Configure<AIGuardOptions>(
            builder.Configuration.GetSection(AIGuardOptions.ConfigurationSectionName));
        builder.Services.Configure<BasicRagAgentOptions>(
            builder.Configuration.GetSection(BasicRagAgentOptions.ConfigurationSectionName));
        if (filterOverrides.Count > 0)
        {
            builder.Services.PostConfigure<BasicRagAgentOptions>(options =>
                options.Retrieval.MetadataFilters =
                    new Dictionary<string, string>(filterOverrides.Values));
        }
        builder.Services.Configure<AIResilienceOptions>(
            builder.Configuration.GetSection(AIResilienceOptions.ConfigurationSectionName));
        builder.Services.AddConfiguredAIProviders(builder.Configuration);
        try
        {
            builder.Services.AddConfiguredRetrieval(builder.Configuration);
        }
        catch (KnowledgeBaseConfigurationException exception)
        {
            logger.LogError("{ErrorMessage}", exception.Message);
            return 2;
        }
        builder.Services.AddMafPlaygroundObservability(builder.Configuration);
        builder.Services.AddSingleton<InteractiveAgentConsole>();

        try
        {
            using IHost host = builder.Build();
            await host.StartAsync(cancellationToken);
            try
            {
                BasicRagAgent ragAgent = host.Services.GetRequiredService<BasicRagAgent>();
                InteractiveAgentConsole console = host.Services.GetRequiredService<InteractiveAgentConsole>();
                return await console.RunAsync(
                    ragAgent.Agent,
                    chatSelection,
                    commandOptions.Prompt,
                    commandOptions.Watch,
                    cancellationToken);
            }
            finally { await host.StopAsync(CancellationToken.None); }
        }
        catch (Exception exception) when (exception is AIProviderNotFoundException or EmbeddingProviderNotFoundException or KnowledgeBaseConfigurationException or OptionsValidationException)
        {
            logger.LogError("{ErrorMessage}", exception.Message);
            return 2;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return 130; }
    }
}

public sealed record BasicRagAgentCommandOptions(
    string? Model,
    string? Prompt,
    bool Watch,
    string[] Filters);

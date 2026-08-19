using System.CommandLine;
using MafPlayground.AI;
using MafPlayground.AI.Agents.RepositoryHelpAgent;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Resilience;
using MafPlayground.CLI.Execution;
using MafPlayground.CLI.Extensions;
using MafPlayground.CLI.Documentation;
using MafPlayground.Observability;
using MafPlayground.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MafPlayground.CLI.Commands;

public static class RepositoryHelpAgentCommand
{
    public static Command Create(
        Func<RepositoryHelpAgentCommandOptions, CancellationToken, Task<int>>? runAsync = null)
    {
        runAsync ??= RunAsync;
        Option<string?> model = new("--model", "-m")
        {
            Description = "Chat model in provider:model format. Falls back to AI_MODEL.",
        };
        Option<string?> prompt = new("--prompt", "-p")
        {
            Description = "Run one question and exit. Omit for an interactive session.",
        };
        Option<bool> watch = new("--watch")
        {
            Description = "Show agent lifecycle events while streaming.",
        };
        Command command = new(
            "repository-help",
            "Ask grounded questions about the repository and its CLI.");
        command.Options.Add(model);
        command.Options.Add(prompt);
        command.Options.Add(watch);
        command.SetAction((result, cancellationToken) => runAsync(
            new RepositoryHelpAgentCommandOptions(
                result.GetValue(model),
                result.GetValue(prompt),
                result.GetValue(watch)),
            cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(
        RepositoryHelpAgentCommandOptions commandOptions,
        CancellationToken cancellationToken)
    {
        using ILoggerFactory loggerFactory = CommandLogging.CreateLoggerFactory();
        ILogger logger = loggerFactory.CreateLogger(typeof(RepositoryHelpAgentCommand));
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                ContentRootPath = AppContext.BaseDirectory,
            });
        builder.Logging.ClearProviders();

        if (!AIModelSelection.TryParse(
                commandOptions.Model ?? builder.Configuration["AI_MODEL"],
                out AIModelSelection? chatSelection))
        {
            logger.LogError(
                "A chat model is required in provider:model format. Use --model or AI_MODEL");
            return 2;
        }

        builder.Services
            .AddAICore(chatSelection)
            .AddRepositoryHelpAgent();
        builder.Services.AddSingleton<IRepositoryCliCommandCatalog>(
            new SystemCommandLineRepositoryCliCommandCatalog(Parser.CreateRootCommand()));
        builder.Services.Configure<AIGuardOptions>(
            builder.Configuration.GetSection(AIGuardOptions.ConfigurationSectionName));
        builder.Services.Configure<RepositoryHelpAgentOptions>(
            builder.Configuration.GetSection(
                RepositoryHelpAgentOptions.ConfigurationSectionName));
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
                RepositoryHelpAgent agent = host.Services
                    .GetRequiredService<RepositoryHelpAgent>();
                InteractiveAgentConsole console = host.Services
                    .GetRequiredService<InteractiveAgentConsole>();
                return await console.RunAsync(
                    agent.Agent,
                    chatSelection,
                    commandOptions.Prompt,
                    commandOptions.Watch,
                    cancellationToken);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None);
            }
        }
        catch (Exception exception) when (
            exception is AIProviderNotFoundException or
            EmbeddingProviderNotFoundException or
            KnowledgeBaseConfigurationException or
            GuardConfigurationException or
            OptionsValidationException)
        {
            logger.LogError("{ErrorMessage}", exception.Message);
            return 2;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 130;
        }
    }
}

public sealed record RepositoryHelpAgentCommandOptions(
    string? Model,
    string? Prompt,
    bool Watch);

using System.CommandLine;
using MafPlayground.AI;
using MafPlayground.CLI.Execution;
using MafPlayground.CLI.Extensions;
using MafPlayground.CLI.Helpers;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Resilience;
using MafPlayground.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MafPlayground.CLI.Commands;

public static class BasicAgentCommand
{
    public static Command Create(
        Func<BasicAgentCommandOptions, CancellationToken, Task<int>>? runAsync = null)
    {
        runAsync ??= RunAsync;

        Option<string?> modelOption = new("--model", "-m")
        {
            Description = "Model selector in provider:model format. Falls back to AI_MODEL."
        };
        Option<string?> promptOption = new("--prompt", "-p")
        {
            Description = "Run one prompt and exit. Omit to start an interactive session."
        };
        Option<bool> watchOption = new("--watch")
        {
            Description = "Show agent lifecycle and tool-call events while streaming.",
        };

        Command command = new("basic", "Run the Basic agent.");
        command.Options.Add(modelOption);
        command.Options.Add(promptOption);
        command.Options.Add(watchOption);
        command.SetAction((parseResult, cancellationToken) =>
            runAsync(
                new BasicAgentCommandOptions(
                    parseResult.GetValue(modelOption),
                    parseResult.GetValue(promptOption),
                    parseResult.GetValue(watchOption)),
                cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(
        BasicAgentCommandOptions commandOptions,
        CancellationToken cancellationToken)
    {
        using ILoggerFactory commandLoggerFactory = CommandLogging.CreateLoggerFactory();
        ILogger logger = commandLoggerFactory.CreateLogger(typeof(BasicAgentCommand));

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                ContentRootPath = AppContext.BaseDirectory,
            });
        builder.Logging.ClearProviders();

        ConfigurationManager configuration = builder.Configuration;
        string? modelSelector = commandOptions.Model
            ?? configuration["AI_MODEL"];

        if (!AIModelSelection.TryParse(modelSelector, out AIModelSelection? modelSelection))
        {
            logger.LogError(
                "An AI model is required in provider:model format. Use --model or set AI_MODEL");
            return 2;
        }

        builder.Services.AddLocalUserContext();
        builder.Services
            .AddAICore(modelSelection)
            .AddBasicAgent();
        builder.Services.Configure<BasicAgentOptions>(
            configuration.GetSection(BasicAgentOptions.ConfigurationSectionName));
        builder.Services.Configure<AIGuardOptions>(
            configuration.GetSection(AIGuardOptions.ConfigurationSectionName));
        builder.Services.Configure<AIResilienceOptions>(
            configuration.GetSection(AIResilienceOptions.ConfigurationSectionName));
        builder.Services.AddConfiguredAIProviders(configuration);
        builder.Services.AddMafPlaygroundObservability(configuration);
        builder.Services.AddSingleton<InteractiveAgentConsole>();

        try
        {
            using IHost host = builder.Build();
            bool hostStarted = false;

            try
            {
                await host.StartAsync(cancellationToken);
                hostStarted = true;

                BasicAgent basicAgent = host.Services.GetRequiredService<BasicAgent>();
                InteractiveAgentConsole console =
                    host.Services.GetRequiredService<InteractiveAgentConsole>();
                return await console.RunAsync(
                    basicAgent.Agent,
                    modelSelection,
                    commandOptions.Prompt,
                    commandOptions.Watch,
                    cancellationToken);
            }
            finally
            {
                if (hostStarted)
                {
                    await host.StopAsync(CancellationToken.None);
                }
            }
        }
        catch (AIProviderNotFoundException exception)
        {
            logger.LogError("{ErrorMessage}", exception.Message);
            return 2;
        }
        catch (OptionsValidationException exception)
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

public sealed record BasicAgentCommandOptions(string? Model, string? Prompt, bool Watch = false);

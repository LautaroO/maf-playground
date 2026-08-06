using System.CommandLine;
using System.Text.Json;
using MafPlayground.AI;
using MafPlayground.AI.Resilience;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Workflows.Translation;
using MafPlayground.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MafPlayground.CLI.Commands;

public static class TranslateWorkflowCommand
{
    public static Command Create(
        Func<TranslateWorkflowCommandOptions, CancellationToken, Task<int>>? runAsync = null)
    {
        runAsync ??= RunAsync;

        Option<string?> modelOption = new("--model", "-m")
        {
            Description = "Model selector in provider:model format. Falls back to AI_MODEL.",
        };
        Option<string?> textOption = new("--text", "-t")
        {
            Description = "Source text to translate.",
        };
        Option<string?> languagesOption = new("--languages", "-l")
        {
            Description = "Comma-separated target language identifiers, for example es,fr,pt-BR.",
        };
        Option<bool> watchOption = new("--watch")
        {
            Description = "Stream native workflow execution events to the terminal.",
        };
        Command command = new("translate", "Translate text concurrently and validate each result.");
        command.Options.Add(modelOption);
        command.Options.Add(textOption);
        command.Options.Add(languagesOption);
        command.Options.Add(watchOption);
        command.SetAction((parseResult, cancellationToken) =>
            runAsync(
                new TranslateWorkflowCommandOptions(
                    parseResult.GetValue(modelOption),
                    parseResult.GetValue(textOption),
                    parseResult.GetValue(languagesOption),
                    parseResult.GetValue(watchOption)),
                cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(
        TranslateWorkflowCommandOptions commandOptions,
        CancellationToken cancellationToken)
    {
        using ILoggerFactory commandLoggerFactory = CommandLogging.CreateLoggerFactory();
        ILogger logger = commandLoggerFactory.CreateLogger(typeof(TranslateWorkflowCommand));

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                ContentRootPath = AppContext.BaseDirectory,
            });
        builder.Logging.ClearProviders();

        ConfigurationManager configuration = builder.Configuration;
        string? modelSelector = commandOptions.Model ?? configuration["AI_MODEL"];
        if (!AIModelSelection.TryParse(modelSelector, out AIModelSelection? modelSelection))
        {
            logger.LogError(
                "An AI model is required in provider:model format. Use --model or set AI_MODEL");
            return 2;
        }

        string[] languages = commandOptions.Languages?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        TranslationWorkflowRequest request = new(
            commandOptions.Text ?? string.Empty,
            languages);

        builder.Services
            .AddAICore(modelSelection)
            .AddTranslationWorkflow();
        builder.Services.Configure<AIGuardOptions>(
            configuration.GetSection(AIGuardOptions.ConfigurationSectionName));
        builder.Services.Configure<AIResilienceOptions>(
            configuration.GetSection(AIResilienceOptions.ConfigurationSectionName));
        builder.Services.Configure<TranslationWorkflowOptions>(
            configuration.GetSection("AI:Workflows:Translation"));
        builder.Services.AddConfiguredAIProviders(configuration);
        builder.Services.AddMafPlaygroundObservability(configuration);

        try
        {
            using IHost host = builder.Build();
            bool hostStarted = false;
            try
            {
                await host.StartAsync(cancellationToken);
                hostStarted = true;

                TranslationWorkflowRunner runner =
                    host.Services.GetRequiredService<TranslationWorkflowRunner>();
                TranslationWorkflowResult result = commandOptions.Watch
                    ? await RunWithEventsAsync(runner, request, cancellationToken)
                    : await runner.RunAsync(request, cancellationToken);
                Console.WriteLine(JsonSerializer.Serialize(
                    result,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    {
                        WriteIndented = true,
                    }));
                return result.Translations.All(translation => translation.IsValid) ? 0 : 1;
            }
            finally
            {
                if (hostStarted)
                {
                    await host.StopAsync(CancellationToken.None);
                }
            }
        }
        catch (ArgumentException exception)
        {
            logger.LogError("{ErrorMessage}", exception.Message);
            return 2;
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

    private static async Task<TranslationWorkflowResult> RunWithEventsAsync(
        TranslationWorkflowRunner runner,
        TranslationWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        WorkflowExecutionConsole executionConsole = new(Console.Error);
        TranslationWorkflowResult? result = null;

        await foreach (Microsoft.Agents.AI.Workflows.WorkflowEvent workflowEvent in
                       runner.RunStreamingAsync(request, cancellationToken))
        {
            await executionConsole.RenderAsync(workflowEvent, cancellationToken);
            if (workflowEvent is Microsoft.Agents.AI.Workflows.WorkflowOutputEvent output)
            {
                result = output.As<TranslationWorkflowResult>() ?? result;
            }
        }

        return result ?? throw new InvalidOperationException(
            "The translation workflow completed without producing a result.");
    }
}

public sealed record TranslateWorkflowCommandOptions(
    string? Model,
    string? Text,
    string? Languages,
    bool Watch = false);

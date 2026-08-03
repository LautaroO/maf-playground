using System.CommandLine;
using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.Observability;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MafPlayground.CLI.Commands;

public static class DevUICommand
{
    public static Command Create(
        Func<DevUICommandOptions, CancellationToken, Task<int>>? runAsync = null)
    {
        runAsync ??= RunAsync;

        Option<string?> modelOption = new("--model", "-m")
        {
            Description = "Model selector in provider:model format. Falls back to AI_MODEL."
        };
        Option<string?> urlOption = new("--url")
        {
            Description = "HTTP URL for DevUI. Falls back to DEVUI_URL."
        };

        Command command = new("devui", "Run the local Agent Framework DevUI.");
        command.Options.Add(modelOption);
        command.Options.Add(urlOption);
        command.SetAction((parseResult, cancellationToken) =>
            runAsync(
                new DevUICommandOptions(
                    parseResult.GetValue(modelOption),
                    parseResult.GetValue(urlOption)),
                cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(
        DevUICommandOptions commandOptions,
        CancellationToken cancellationToken)
    {
        using ILoggerFactory commandLoggerFactory = CommandLogging.CreateLoggerFactory();
        ILogger logger = commandLoggerFactory.CreateLogger(typeof(DevUICommand));

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });

        string? modelSelector = commandOptions.Model
            ?? builder.Configuration["AI_MODEL"];

        if (!AIModelSelection.TryParse(modelSelector, out AIModelSelection? modelSelection))
        {
            logger.LogError(
                "An AI model is required in provider:model format. Use --model or set AI_MODEL");
            return 2;
        }

        string url = commandOptions.Url
            ?? builder.Configuration["DEVUI_URL"]
            ?? "http://localhost:5050";
        builder.WebHost.UseUrls(url);

        builder.Services.AddAIServices(modelSelection);
        builder.Services.AddConfiguredAIProviders(builder.Configuration);
        builder.Services.AddMafPlaygroundObservability(builder.Configuration);
        builder.AddAIAgent(
            "basic-agent",
            (services, _) => services.GetRequiredService<BasicAgent>().Agent);
        builder.AddDevUI();
        builder.Services.AddOpenAIResponses();
        builder.Services.AddOpenAIConversations();

        try
        {
            await using WebApplication app = builder.Build();

            _ = app.Services.GetRequiredService<BasicAgent>();

            app.MapOpenAIResponses();
            app.MapOpenAIConversations();
            app.MapDevUI();
            app.MapGet("/", () => Results.Redirect("/devui"));

            logger.LogInformation("DevUI is available at {DevUIUrl}/devui", url);
            await app.RunAsync(cancellationToken);
            return 0;
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

public sealed record DevUICommandOptions(string? Model, string? Url);

using System.CommandLine;
using MafPlayground.AI;
using MafPlayground.AI.Resilience;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Workflows.Translation;
using MafPlayground.CLI.DevUI;
using MafPlayground.CLI.Inspection;
using MafPlayground.Observability;
using MafPlayground.Retrieval;
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
            ContentRootPath = AppContext.BaseDirectory,
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

        builder.Services.AddLocalUserContext();
        builder.Services.AddAIServices(modelSelection);
        builder.Services.Configure<BasicRagAgentOptions>(
            builder.Configuration.GetSection(BasicRagAgentOptions.ConfigurationSectionName));
        builder.Services.Configure<AIResilienceOptions>(
            builder.Configuration.GetSection(AIResilienceOptions.ConfigurationSectionName));
        builder.Services.Configure<TranslationWorkflowOptions>(
            builder.Configuration.GetSection("AI:Workflows:Translation"));
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
        builder.Services.AddDevUITracing();
        foreach (LocalEntityDescriptor descriptor in LocalEntityCatalog.All)
        {
            if (descriptor.Kind == LocalEntityKind.Agent)
            {
                builder.AddAIAgent(
                    descriptor.Id,
                    (services, _) => descriptor.CreateAgent!(services));
            }
            else
            {
                builder.AddWorkflow(
                    descriptor.Id,
                    (services, workflowName) =>
                        descriptor.CreateHostedWorkflow!(services, workflowName),
                    ServiceLifetime.Transient);
            }
        }
        builder.AddDevUI();
        builder.Services.AddOpenAIResponses();
        builder.Services.AddOpenAIConversations();

        try
        {
            await using WebApplication app = builder.Build();

            _ = app.Services.GetRequiredService<BasicAgent>();
            _ = app.Services.GetRequiredService<BasicRagAgent>();

            app.UseDevUITracing();
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
        catch (EmbeddingProviderNotFoundException exception)
        {
            logger.LogError("{ErrorMessage}", exception.Message);
            return 2;
        }
        catch (KnowledgeBaseConfigurationException exception)
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

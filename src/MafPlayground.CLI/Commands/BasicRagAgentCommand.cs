using System.CommandLine;
using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicRagAgent;
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
        Option<string?> embeddingModel = new("--embedding-model") { Description = "Embedding model in provider:model format. Falls back to AI_EMBEDDING_MODEL." };
        Option<string?> prompt = new("--prompt", "-p") { Description = "Run one prompt and exit. Omit for an interactive session." };
        Command command = new("basic-rag", "Run the grounded Basic RAG agent.");
        command.Options.Add(model);
        command.Options.Add(embeddingModel);
        command.Options.Add(prompt);
        command.SetAction((result, cancellationToken) => runAsync(new(result.GetValue(model), result.GetValue(embeddingModel), result.GetValue(prompt)), cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(BasicRagAgentCommandOptions commandOptions, CancellationToken cancellationToken)
    {
        using ILoggerFactory loggerFactory = CommandLogging.CreateLoggerFactory();
        ILogger logger = loggerFactory.CreateLogger(typeof(BasicRagAgentCommand));
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();

        if (!AIModelSelection.TryParse(commandOptions.Model ?? builder.Configuration["AI_MODEL"], out AIModelSelection? chatSelection))
        {
            logger.LogError("A chat model is required in provider:model format. Use --model or AI_MODEL");
            return 2;
        }
        if (!EmbeddingModelSelection.TryParse(commandOptions.EmbeddingModel ?? builder.Configuration["AI_EMBEDDING_MODEL"], out EmbeddingModelSelection? embeddingSelection))
        {
            logger.LogError("An embedding model is required in provider:model format. Use --embedding-model or AI_EMBEDDING_MODEL");
            return 2;
        }

        builder.Services.AddLocalUserContext();
        builder.Services.AddAIServices(chatSelection);
        builder.Services.AddConfiguredAIProviders(builder.Configuration);
        builder.Services.AddConfiguredRetrieval(builder.Configuration, embeddingSelection!);
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
                return await console.RunAsync(ragAgent.Agent, chatSelection, commandOptions.Prompt, cancellationToken);
            }
            finally { await host.StopAsync(CancellationToken.None); }
        }
        catch (Exception exception) when (exception is AIProviderNotFoundException or EmbeddingProviderNotFoundException or OptionsValidationException)
        {
            logger.LogError("{ErrorMessage}", exception.Message);
            return 2;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return 130; }
    }
}

public sealed record BasicRagAgentCommandOptions(string? Model, string? EmbeddingModel, string? Prompt);

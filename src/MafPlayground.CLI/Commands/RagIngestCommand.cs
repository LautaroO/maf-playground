using System.CommandLine;
using MafPlayground.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MafPlayground.CLI.Commands;

public static class RagIngestCommand
{
    public static Command Create(Func<RagIngestCommandOptions, CancellationToken, Task<int>>? runAsync = null)
    {
        runAsync ??= RunAsync;
        Option<string?> path = new("--path") { Description = "Document file to ingest." };
        Option<string?> sourceRoot = new("--source-root") { Description = "Optional root used to create stable relative source identifiers." };
        Option<string?> embeddingModel = new("--embedding-model") { Description = "Embedding model in provider:model format. Falls back to AI_EMBEDDING_MODEL." };
        Command command = new("ingest", "Extract, chunk, embed, and index a document.");
        command.Options.Add(path); command.Options.Add(sourceRoot); command.Options.Add(embeddingModel);
        command.SetAction((result, cancellationToken) => runAsync(new(result.GetValue(path), result.GetValue(sourceRoot), result.GetValue(embeddingModel)), cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(RagIngestCommandOptions commandOptions, CancellationToken cancellationToken)
    {
        using ILoggerFactory loggerFactory = CommandLogging.CreateLoggerFactory();
        ILogger logger = loggerFactory.CreateLogger(typeof(RagIngestCommand));
        if (string.IsNullOrWhiteSpace(commandOptions.Path)) { logger.LogError("--path is required."); return 2; }
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
        if (!EmbeddingModelSelection.TryParse(commandOptions.EmbeddingModel ?? builder.Configuration["AI_EMBEDDING_MODEL"], out EmbeddingModelSelection? selection))
        {
            logger.LogError("An embedding model is required in provider:model format. Use --embedding-model or AI_EMBEDDING_MODEL");
            return 2;
        }
        builder.Services.AddConfiguredAIProviders(builder.Configuration);
        builder.Services.AddConfiguredRetrieval(builder.Configuration, selection!);
        try
        {
            using IHost host = builder.Build();
            IngestionResult result = await host.Services
                .GetRequiredService<KnowledgeIngestionService>()
                .IngestAsync(commandOptions.Path, commandOptions.SourceRoot, cancellationToken);
            logger.LogInformation(
                "Document {SourceId}: {Chunks} chunks. Skipped: {Skipped}",
                result.SourceId,
                result.Chunks,
                result.Skipped);
            foreach (string warning in result.Warnings)
            {
                logger.LogWarning("{ExtractionWarning}", warning);
            }
            return result.Chunks == 0 && !result.Skipped ? 1 : 0;
        }
        catch (FileNotFoundException exception)
        {
            logger.LogError(
                "Document was not found at {DocumentPath}",
                exception.FileName ?? commandOptions.Path);
            return 2;
        }
        catch (NotSupportedException exception)
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

public sealed record RagIngestCommandOptions(string? Path, string? SourceRoot, string? EmbeddingModel);

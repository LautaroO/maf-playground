using System.CommandLine;
using MafPlayground.CLI.Extensions;
using MafPlayground.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MafPlayground.CLI.Commands;

public static class RagIngestCommand
{
    public static Command Create(Func<RagIngestCommandOptions, CancellationToken, Task<int>>? runAsync = null)
    {
        runAsync ??= RunAsync;
        Option<string?> path = new("--path") { Description = "Document file to ingest." };
        Option<string?> sourceRoot = new("--source-root") { Description = "Optional root used to create stable relative source identifiers." };
        Option<string?> knowledgeBase = new("--knowledge-base") { Description = "Configured knowledge base to ingest into." };
        Option<string[]> metadata = new("--metadata")
        {
            Description = "Document metadata in key=value format. Repeat for multiple values.",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        Command command = new("ingest", "Extract, chunk, embed, and index a document.");
        command.Options.Add(path); command.Options.Add(sourceRoot); command.Options.Add(knowledgeBase); command.Options.Add(metadata);
        command.SetAction((result, cancellationToken) => runAsync(new(result.GetValue(path), result.GetValue(sourceRoot), result.GetValue(knowledgeBase), result.GetValue(metadata) ?? []), cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(RagIngestCommandOptions commandOptions, CancellationToken cancellationToken)
    {
        using ILoggerFactory loggerFactory = CommandLogging.CreateLoggerFactory();
        ILogger logger = loggerFactory.CreateLogger(typeof(RagIngestCommand));
        if (string.IsNullOrWhiteSpace(commandOptions.Path)) { logger.LogError("--path is required."); return 2; }
        if (string.IsNullOrWhiteSpace(commandOptions.KnowledgeBase)) { logger.LogError("--knowledge-base is required."); return 2; }
        KnowledgeMetadata metadata;
        try
        {
            metadata = MetadataOptionParser.Parse(commandOptions.Metadata, "--metadata");
        }
        catch (ArgumentException exception)
        {
            logger.LogError("{ErrorMessage}", exception.Message);
            return 2;
        }
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
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
        try
        {
            using IHost host = builder.Build();
            IngestionResult result = await host.Services
                .GetRequiredService<KnowledgeIngestionService>()
                .IngestAsync(
                    new KnowledgeBaseId(commandOptions.KnowledgeBase),
                    commandOptions.Path,
                    commandOptions.SourceRoot,
                    metadata,
                    cancellationToken);
            logger.LogInformation(
                "Knowledge base {KnowledgeBase}, document {SourceId}: {Chunks} chunks. Skipped: {Skipped}",
                result.KnowledgeBase,
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
        catch (UnauthorizedAccessException)
        {
            logger.LogError("The document is outside the allowed source root.");
            return 2;
        }
        catch (DocumentResourceLimitException exception)
        {
            logger.LogError("{ErrorMessage}", exception.Message);
            return 2;
        }
        catch (Exception exception) when (
            exception is KnowledgeBaseConfigurationException or
            EmbeddingProviderNotFoundException or
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

public sealed record RagIngestCommandOptions(
    string? Path,
    string? SourceRoot,
    string? KnowledgeBase,
    string[] Metadata);

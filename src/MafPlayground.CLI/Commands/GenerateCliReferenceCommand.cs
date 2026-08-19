using System.CommandLine;
using MafPlayground.CLI.Documentation;
using Microsoft.Extensions.Logging;

namespace MafPlayground.CLI.Commands;

public static class GenerateCliReferenceCommand
{
    public static Command Create(
        Func<GenerateCliReferenceCommandOptions, CancellationToken, Task<int>>? runAsync = null)
    {
        runAsync ??= RunAsync;
        Option<string?> output = new("--output", "-o")
        {
            Description = "Markdown file to create or replace.",
            Required = true,
        };
        Command command = new(
            "generate-cli-reference",
            "Generate the repository-help CLI reference from the live command tree.");
        command.Options.Add(output);
        command.SetAction((result, cancellationToken) => runAsync(
            new GenerateCliReferenceCommandOptions(result.GetValue(output)),
            cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(
        GenerateCliReferenceCommandOptions options,
        CancellationToken cancellationToken)
    {
        using ILoggerFactory loggerFactory = CommandLogging.CreateLoggerFactory();
        ILogger logger = loggerFactory.CreateLogger(typeof(GenerateCliReferenceCommand));
        if (string.IsNullOrWhiteSpace(options.Output))
        {
            logger.LogError("--output is required.");
            return 2;
        }

        string outputPath = Path.GetFullPath(options.Output);
        string directory = Path.GetDirectoryName(outputPath)!;
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            string markdown = RepositoryHelpCliReferenceGenerator.Generate(
                Parser.CreateRootCommand());
            await File.WriteAllTextAsync(
                temporaryPath,
                markdown,
                cancellationToken);
            File.Move(temporaryPath, outputPath, overwrite: true);
            logger.LogInformation("CLI reference written to {OutputPath}", outputPath);
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError("Could not write the CLI reference to {OutputPath}", outputPath);
            return 2;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // The primary write result is more useful than a best-effort cleanup failure.
            }
        }
    }
}

public sealed record GenerateCliReferenceCommandOptions(string? Output);

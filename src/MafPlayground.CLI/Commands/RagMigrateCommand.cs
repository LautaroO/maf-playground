using System.CommandLine;
using MafPlayground.Retrieval;
using MafPlayground.Retrieval.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MafPlayground.CLI.Commands;

public static class RagMigrateCommand
{
    public static Command Create(Func<RagMigrateCommandOptions, CancellationToken, Task<int>>? runAsync = null)
    {
        runAsync ??= RunAsync;
        Command command = new("migrate", "Apply retrieval database migrations.");
        command.SetAction((_, cancellationToken) => runAsync(new(), cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(RagMigrateCommandOptions _, CancellationToken cancellationToken)
    {
        using ILoggerFactory loggerFactory = CommandLogging.CreateLoggerFactory();
        ILogger logger = loggerFactory.CreateLogger(typeof(RagMigrateCommand));
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
        builder.Services.AddPostgresRetrieval(builder.Configuration);
        using IHost host = builder.Build();
        await host.Services.GetRequiredService<IRetrievalDatabaseInitializer>().MigrateAsync(cancellationToken);
        logger.LogInformation("Retrieval database migrations applied.");
        return 0;
    }
}

public sealed record RagMigrateCommandOptions;

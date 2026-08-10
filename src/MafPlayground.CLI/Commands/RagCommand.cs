using System.CommandLine;

namespace MafPlayground.CLI.Commands;

public sealed class RagCommand : ICliCommand
{
    public int Order => 300;

    Command ICliCommand.Create() => Create();

    public static Command Create(
        Func<RagMigrateCommandOptions, CancellationToken, Task<int>>? migrateAsync = null,
        Func<RagIngestCommandOptions, CancellationToken, Task<int>>? ingestAsync = null)
    {
        Command command = new("rag", "Manage the local RAG knowledge base.");
        Command database = new("database", "Manage the retrieval database.");
        database.Subcommands.Add(RagMigrateCommand.Create(migrateAsync));
        command.Subcommands.Add(database);
        command.Subcommands.Add(RagIngestCommand.Create(ingestAsync));
        return command;
    }
}

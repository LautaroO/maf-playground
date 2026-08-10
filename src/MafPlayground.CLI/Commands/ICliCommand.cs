using System.CommandLine;

namespace MafPlayground.CLI.Commands;

public interface ICliCommand
{
    int Order { get; }

    Command Create();
}

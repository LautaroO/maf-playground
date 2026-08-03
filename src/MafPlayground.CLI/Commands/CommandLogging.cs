using Microsoft.Extensions.Logging;

namespace MafPlayground.CLI.Commands;

internal static class CommandLogging
{
    public static ILoggerFactory CreateLoggerFactory() =>
        LoggerFactory.Create(logging => logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        }));
}

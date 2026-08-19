using System.CommandLine;
using System.Text;

namespace MafPlayground.CLI.Documentation;

public static class RepositoryHelpCliReferenceGenerator
{
    public static string Generate(RootCommand rootCommand)
    {
        ArgumentNullException.ThrowIfNull(rootCommand);

        StringBuilder builder = new();
        builder.AppendLine("# MafPlayground CLI reference");
        builder.AppendLine();
        builder.AppendLine(
            "> Generated deterministically from the System.CommandLine command tree. Do not edit manually.");
        builder.AppendLine();
        builder.AppendLine(
            "Use `dotnet run --project src/MafPlayground.CLI -- --help` for terminal help at any time.");
        builder.AppendLine();
        AppendCommand(builder, rootCommand, ["maf-playground"], headingLevel: 2);
        return builder.ToString();
    }

    private static void AppendCommand(
        StringBuilder builder,
        Command command,
        IReadOnlyList<string> path,
        int headingLevel)
    {
        string commandPath = string.Join(' ', path);
        builder.AppendLine($"{new string('#', Math.Min(headingLevel, 6))} `{commandPath}`");
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(command.Description))
        {
            builder.AppendLine(command.Description.Trim());
            builder.AppendLine();
        }

        builder.AppendLine("Usage:");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.Append(BuildInvocation(command, path.Skip(1).ToArray()));
        if (command.Subcommands.Any(subcommand => !subcommand.Hidden))
        {
            builder.Append(" [command]");
        }
        if (command.Arguments.Any(argument =>
                !argument.Hidden && argument.Arity.MinimumNumberOfValues == 0))
        {
            foreach (Argument argument in command.Arguments.Where(argument =>
                         !argument.Hidden && argument.Arity.MinimumNumberOfValues == 0))
            {
                builder.Append($" [{argument.Name}]");
            }
        }
        if (command.Options.Any(option =>
                IsDocumentedOption(option) && !option.Required))
        {
            builder.Append(" [options]");
        }
        builder.AppendLine();
        builder.AppendLine("```");
        builder.AppendLine();

        AppendArguments(builder, command.Arguments.Where(argument => !argument.Hidden));
        AppendOptions(builder, command.Options.Where(IsDocumentedOption));
        AppendSubcommands(builder, command.Subcommands.Where(subcommand => !subcommand.Hidden));

        foreach (Command subcommand in command.Subcommands.Where(subcommand => !subcommand.Hidden))
        {
            AppendCommand(
                builder,
                subcommand,
                [.. path, subcommand.Name],
                headingLevel + 1);
        }
    }

    private static void AppendArguments(
        StringBuilder builder,
        IEnumerable<Argument> arguments)
    {
        Argument[] visibleArguments = arguments.ToArray();
        if (visibleArguments.Length == 0)
        {
            return;
        }

        builder.AppendLine("Arguments:");
        builder.AppendLine();
        builder.AppendLine("| Name | Required | Description |");
        builder.AppendLine("| --- | --- | --- |");
        foreach (Argument argument in visibleArguments)
        {
            builder.AppendLine(
                $"| `{Escape(argument.Name)}` | {(argument.Arity.MinimumNumberOfValues > 0 ? "yes" : "no")} | {Escape(argument.Description)} |");
        }
        builder.AppendLine();
    }

    private static void AppendOptions(
        StringBuilder builder,
        IEnumerable<Option> options)
    {
        Option[] visibleOptions = options.ToArray();
        if (visibleOptions.Length == 0)
        {
            return;
        }

        builder.AppendLine("Options:");
        builder.AppendLine();
        builder.AppendLine("| Option | Required | Description |");
        builder.AppendLine("| --- | --- | --- |");
        foreach (Option option in visibleOptions)
        {
            string names = string.Join(
                ", ",
                new[] { option.Name }.Concat(option.Aliases).Select(name => $"`{Escape(name)}`"));
            builder.AppendLine(
                $"| {names} | {(option.Required ? "yes" : "no")} | {Escape(option.Description)} |");
        }
        builder.AppendLine();
    }

    private static void AppendSubcommands(
        StringBuilder builder,
        IEnumerable<Command> subcommands)
    {
        Command[] visibleSubcommands = subcommands.ToArray();
        if (visibleSubcommands.Length == 0)
        {
            return;
        }

        builder.AppendLine("Subcommands:");
        builder.AppendLine();
        builder.AppendLine("| Command | Description |");
        builder.AppendLine("| --- | --- |");
        foreach (Command subcommand in visibleSubcommands)
        {
            builder.AppendLine(
                $"| `{Escape(subcommand.Name)}` | {Escape(subcommand.Description)} |");
        }
        builder.AppendLine();
    }

    private static string Escape(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim()
                .Replace("|", "\\|", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);

    private static bool IsDocumentedOption(Option option) =>
        !option.Hidden &&
        option.Name is not "--help" and not "--version";

    internal static string BuildInvocation(
        Command command,
        IReadOnlyList<string> path)
    {
        StringBuilder builder = new(
            "dotnet run --project src/MafPlayground.CLI --");
        if (path.Count > 0)
        {
            builder.Append(' ');
            builder.Append(string.Join(' ', path));
        }
        foreach (Argument argument in command.Arguments.Where(argument =>
                     !argument.Hidden && argument.Arity.MinimumNumberOfValues > 0))
        {
            builder.Append(" <");
            builder.Append(argument.Name);
            builder.Append('>');
        }
        foreach (Option option in command.Options.Where(option =>
                     IsDocumentedOption(option) && option.Required))
        {
            builder.Append(' ');
            builder.Append(option.Name);
            AppendOptionValuePlaceholder(builder, option);
        }
        return builder.ToString();
    }

    private static void AppendOptionValuePlaceholder(
        StringBuilder builder,
        Option option)
    {
        if (option.ValueType != typeof(bool))
        {
            builder.Append(" <value>");
        }
    }
}

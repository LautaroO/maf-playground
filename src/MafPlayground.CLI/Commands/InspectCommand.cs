using System.CommandLine;
using MafPlayground.CLI.Inspection;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MafPlayground.CLI.Commands;

public static class InspectCommand
{
    public static Command Create(
        Func<InspectCommandOptions, CancellationToken, Task<int>>? runAsync = null)
    {
        runAsync ??= RunAsync;

        Command command = new("inspect", "Inspect local agents and workflows.");
        command.Subcommands.Add(CreateListCommand(runAsync));
        command.Subcommands.Add(CreateEntityCommand("agent", allowDiagram: false, runAsync));
        command.Subcommands.Add(CreateEntityCommand("workflow", allowDiagram: true, runAsync));
        return command;
    }

    private static Command CreateListCommand(
        Func<InspectCommandOptions, CancellationToken, Task<int>> runAsync)
    {
        Command command = new("list", "List locally registered agents and workflows.");
        command.SetAction((_, cancellationToken) =>
            runAsync(new InspectCommandOptions(List: true), cancellationToken));
        return command;
    }

    private static Command CreateEntityCommand(
        string entityKind,
        bool allowDiagram,
        Func<InspectCommandOptions, CancellationToken, Task<int>> runAsync)
    {
        Argument<string> idArgument = new("id")
        {
            Description = $"The local {entityKind} identifier.",
        };
        Option<bool> viewInputOption = new("--view-input")
        {
            Description = "Print the required input JSON Schema and an example.",
        };
        Command command = new(entityKind, $"Inspect a local {entityKind}.");
        command.Arguments.Add(idArgument);
        command.Options.Add(viewInputOption);

        Option<bool>? diagramOption = null;
        if (allowDiagram)
        {
            diagramOption = new Option<bool>("--diagram")
            {
                Description = "Print the native MAF workflow graph as Mermaid.",
            };
            command.Options.Add(diagramOption);
        }

        command.SetAction((parseResult, cancellationToken) =>
            runAsync(
                new InspectCommandOptions(
                    EntityKind: entityKind,
                    EntityId: parseResult.GetValue(idArgument),
                    ViewInput: parseResult.GetValue(viewInputOption),
                    Diagram: diagramOption is not null && parseResult.GetValue(diagramOption)),
                cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(
        InspectCommandOptions commandOptions,
        CancellationToken cancellationToken)
    {
        using ILoggerFactory loggerFactory = CommandLogging.CreateLoggerFactory();
        ILogger logger = loggerFactory.CreateLogger(typeof(InspectCommand));

        if (commandOptions.List)
        {
            foreach (LocalEntityDescriptor entity in LocalEntityCatalog.All)
            {
                Console.WriteLine(
                    $"{entity.Kind.ToString().ToLowerInvariant(),-8} {entity.Id,-24} {entity.Description}");
            }

            return 0;
        }

        if (!TryParseEntityKind(commandOptions.EntityKind, out LocalEntityKind kind) ||
            string.IsNullOrWhiteSpace(commandOptions.EntityId))
        {
            logger.LogError("An entity kind and identifier are required");
            return 2;
        }

        LocalEntityDescriptor? descriptor = LocalEntityCatalog.Find(
            kind,
            commandOptions.EntityId);
        if (descriptor is null)
        {
            logger.LogError(
                "No local {EntityKind} named {EntityId} is registered",
                kind,
                commandOptions.EntityId);
            return 2;
        }

        if (!commandOptions.ViewInput && !commandOptions.Diagram)
        {
            Console.WriteLine($"{descriptor.Id} ({descriptor.Kind.ToString().ToLowerInvariant()})");
            Console.WriteLine(descriptor.Description);
            return 0;
        }

        if (commandOptions.ViewInput)
        {
            EntityInputRenderer renderer = new(Console.Out);
            await renderer.RenderAsync(descriptor, cancellationToken);
        }

        if (!commandOptions.Diagram)
        {
            return 0;
        }

        return await RenderWorkflowDiagramAsync(
            descriptor,
            logger,
            cancellationToken);
    }

    private static async Task<int> RenderWorkflowDiagramAsync(
        LocalEntityDescriptor descriptor,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                ContentRootPath = AppContext.BaseDirectory,
            });
        builder.Logging.ClearProviders();

        try
        {
            Workflow workflow = descriptor.CreateWorkflowDiagram?.Invoke(builder.Configuration)
                ?? throw new InvalidOperationException(
                    $"Entity '{descriptor.Id}' does not provide a workflow graph.");
            Console.WriteLine(WorkflowVisualizer.ToMermaidString(workflow));
            await Console.Out.FlushAsync(cancellationToken);
            return 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            logger.LogError("{ErrorMessage}", exception.Message);
            return 2;
        }
    }

    private static bool TryParseEntityKind(
        string? value,
        out LocalEntityKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind);
}

public sealed record InspectCommandOptions(
    bool List = false,
    string? EntityKind = null,
    string? EntityId = null,
    bool ViewInput = false,
    bool Diagram = false);

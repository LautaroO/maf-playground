using System.CommandLine;
using MafPlayground.CLI.Inspection;
using MafPlayground.CLI.Tui;
using Microsoft.Extensions.Hosting;

namespace MafPlayground.CLI.Commands;

internal sealed class CasesCommand : ICliCommand
{
    public int Order => 520;

    public Command Create()
    {
        var entity = new Option<string>("--entity") { Required = true, Description = "Local entity whose saved cases to evaluate." };
        var model = new Option<string?>("--model") { Description = "Model selector, defaults to AI_MODEL." };
        var cases = new Option<string?>("--cases-dir") { Description = "Saved case library root, defaults to the TUI library." };
        var captures = new Option<string>("--artifacts") { DefaultValueFactory = _ => "maf-captures", Description = "Directory for failed-run JSON captures." };
        var run = new Command("run", "Evaluate saved cases and write a replayable capture for each failure.");
        run.Options.Add(entity);
        run.Options.Add(model);
        run.Options.Add(cases);
        run.Options.Add(captures);
        run.SetAction((args, token) => RunAsync(args.GetValue(entity)!, args.GetValue(model), args.GetValue(cases),
            args.GetValue(captures)!, Console.Out, TuiCommand.ExecuteAsync, token));
        var command = new Command("cases", "Run saved TUI regression cases in automation.");
        command.Subcommands.Add(run);
        return command;
    }

    internal static async Task<int> RunAsync(string entityId, string? model, string? library, string artifacts, TextWriter output,
        Func<LocalEntityDescriptor, string, string?, Action<Action<InspectorRun>>, CancellationToken, Task> execute, CancellationToken token)
    {
        var entity = LocalEntityCatalog.All.FirstOrDefault(e => e.Id == entityId);
        if (entity is null) { output.WriteLine("Unknown entity. Use inspect list."); return 2; }
        var configuration = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory }).Configuration;
        model ??= configuration["AI_MODEL"];
        if (!AIModelSelection.TryParse(model, out _)) { output.WriteLine("Set AI_MODEL or pass --model provider:model."); return 2; }
        try
        {
            var cases = new CaseStore(entityId, library).Read();
            if (cases.Length == 0) { output.WriteLine($"No saved cases for {entityId}."); return 2; }
            int passed = 0, finished = 0;
            await CaseLibrary.ExecuteAsync(cases, async (item, ct) =>
            {
                var (capture, failures) = await LocalCaseRunner.RunAsync(entity, model, item, execute,
                    descriptor => WorkflowGraph.For(descriptor, configuration), ct).ConfigureAwait(false);
                if (failures.Count > 0 && !ct.IsCancellationRequested)
                {
                    Directory.CreateDirectory(artifacts);
                    string path = Path.Combine(artifacts, $"{item.Id:N}-{Guid.NewGuid():N}.json");
                    capture.Save(path);
                    output.WriteLine("Capture  " + path);
                }
                return new(item.Name, failures);
            }, name => output.WriteLine($"RUN   {name}"), outcome =>
            {
                finished++;
                if (outcome.Failures.Count == 0) { passed++; output.WriteLine($"PASS  {outcome.Name}"); }
                else foreach (var failure in outcome.Failures) output.WriteLine($"FAIL  {outcome.Name}  {failure.Message}");
            }, token).ConfigureAwait(false);
            output.WriteLine($"{passed}/{cases.Length} passed, {finished}/{cases.Length} finished.");
            return token.IsCancellationRequested ? 130 : passed == cases.Length ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException or ArgumentException)
        {
            output.WriteLine(InspectorRun.Clean(ex.Message));
            return 2;
        }
    }
}

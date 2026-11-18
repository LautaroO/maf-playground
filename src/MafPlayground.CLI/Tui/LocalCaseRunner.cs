using System.Text.Json;
using MafPlayground.CLI.Inspection;

namespace MafPlayground.CLI.Tui;

internal static class LocalCaseRunner
{
    public static async Task<(LocalCapture Capture, List<CaseFailure> Failures)> RunAsync(
        LocalEntityDescriptor entity, string? model, StudioCase item,
        Func<LocalEntityDescriptor, string, string?, Action<Action<InspectorRun>>, CancellationToken, Task> execute,
        Func<LocalEntityDescriptor, WorkflowGraph> graphFor, CancellationToken token)
    {
        var started = DateTimeOffset.Now;
        JsonElement result = default;
        // Evaluate the model output before the terminal display applies its length limit.
        var run = new InspectorRun(output => result = JsonSerializer.SerializeToElement(output, InspectorRun.Json));
        run.Start();
        string input = item.Input.Pretty();
        try
        {
            // Parallel tool completions can publish from multiple workers.
            var gate = new object();
            await execute(entity, input, model, update => { lock (gate) update(run); }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { run.Cancel(); }
        catch (Exception ex) { run.Fail(ex.Message); }
        var graph = entity.Kind == LocalEntityKind.Agent
            ? new WorkflowGraph(entity.Id, run.ToolNodes.Prepend(entity.Id), run.ToolNodes.Select(tool => (entity.Id, tool))) : graphFor(entity);
        var capture = run.Capture(entity.Id, model, input, started, graph);
        var calls = capture.Invocations.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value.Count)).ToArray();
        var failures = item.Checks.Evaluate(run.Status, result, calls, item.Checks.Node is null || capture.Nodes.Contains(item.Checks.Node));
        return (capture, failures);
    }
}

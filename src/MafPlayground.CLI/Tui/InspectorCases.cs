using MafPlayground.CLI.Inspection;
using Terminal.Gui.Views;

namespace MafPlayground.CLI.Tui;

internal sealed partial class InspectorScreen
{
    private readonly CaseLibrary _cases = new();

    public void OpenCases()
    {
        if (!Pending.IsCompleted || !_updates.IsEmpty) { MessageBox.Query(StudioTerminal.App, "Cases", "Wait for the current run to finish.", "Close"); return; }
        var entity = _entity;
        _cases.Open(entity.Id, _model ?? "local", _input, json => _input = json, () =>
        {
            if (entity.CreateAgent is null && entity.StreamWorkflow is null)
            {
                MessageBox.Query(StudioTerminal.App, "Archived entity", "This entity is unavailable in the local catalog.", "Close");
                return false;
            }
            ArchiveCurrent();
            return AIModelSelection.TryParse(_model, out _) || ChooseModel();
        }, (item, token) => ExecuteCaseAsync(entity, _model, item, token), _token);
        SetNeedsDraw();
    }

    public void CompareModels()
    {
        if (!Pending.IsCompleted || !_updates.IsEmpty) { MessageBox.Query(StudioTerminal.App, "Compare models", "Wait for the current run to finish.", "Close"); return; }
        var entity = _entity;
        if (entity.CreateAgent is null && entity.StreamWorkflow is null)
        {
            MessageBox.Query(StudioTerminal.App, "Compare models", "This entity is unavailable in the local catalog.", "Close");
            return;
        }
        if (PromptModel("Baseline model", _model) is not { } baseline ||
            PromptModel("Candidate model", null) is not { } candidate) return;
        CaseComparison.Open(entity.Id, baseline, candidate,
            (model, item, token) => ExecuteCaseAsync(entity, model, item, token), (a, b) =>
            {
                if (a.Local is { } left && b.Local is { } right)
                    GraphInspectionDialog.Compare(GraphEvidence.From(left), GraphEvidence.From(right), LocalCapture.Compare(left, right));
            }, _token);
        SetNeedsDraw();
    }

    private async Task<CaseOutcome> ExecuteCaseAsync(LocalEntityDescriptor entity, string? model, StudioCase item, CancellationToken token)
    {
        var (capture, failures) = await LocalCaseRunner.RunAsync(entity, model, item, _execute, Graph, token).ConfigureAwait(false);
        return new(item.Name, failures, () =>
        {
            Load(capture);
            var excess = failures.FirstOrDefault(f => f.Node is not null);
            if (excess?.Node is { } node) Inspect(node, excess.Index);
            else if (capture.Invocations.Any(pair => pair.Value.Any(inv => inv.Failed || inv.Rejected))) NextError();
            else _page = 2;
        }, () => Remember(capture))
        {
            Local = capture,
            Duration = capture.Elapsed,
            InputTokens = capture.InputTokens,
            OutputTokens = capture.OutputTokens
        };
    }
}

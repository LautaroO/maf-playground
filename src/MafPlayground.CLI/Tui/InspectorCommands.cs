using Terminal.Gui.Views;

namespace MafPlayground.CLI.Tui;

internal sealed partial class InspectorScreen
{
    private StudioAction[]? _commands;
    internal StudioAction[] Commands => _commands ??=
    [
        new("View graph", "1", () => SelectPage(0), "grafo"),
        new("View events", "2", () => SelectPage(1), "eventos timeline"),
        new("View final result", "3", () => SelectPage(2), "resultado salida output"),
        new("Invocation timeline", "4", ShowTimeline, "timing duration duración línea de tiempo", HasRun),
        new("Toggle raw result", "j", ToggleRawResult, "json resultado resumen", HasRun),
        new("Compare models or profiles on saved cases", "a", CompareModels, "evaluation suite comparar modelos perfiles", Idle),
        new("Edit input", "i", Edit, "editar entrada JSON", Idle),
        new("Run entity", "r", Run, "ejecutar correr", ReadyToRun),
        new("Saved inputs & evaluations", "t", OpenCases, "casos pruebas guardar entradas suite regression tests", Idle),
        new("Replay recorded graph", "g", Replay, "reproducir grafo pasos step timeline", HasRun),
        new("Compare runs on graph", "b", () => History(true), "comparar corridas diferencias diff", Comparable),
        new("Change model", "m", () => ChooseModel(), "cambiar modelo provider", Idle),
        new("Cancel running entity", "x", () => _job?.Cancel(), "cancelar", Active),
        new("Show input schema", "v", () => ShowText("Input schema", InspectorRun.Format(Inspection.EntityInputRenderer.Schema(_entity))), "esquema"),
        new("Jump to next error", "e", NextError, "siguiente fallo rechazado"),
        new("Open run history", "h", () => History(false), "historial", Idle),
        new("Save capture", "s", () => CaptureFile(true), "guardar captura exportar", Saveable),
        new("Open capture", "o", () => CaptureFile(false), "abrir captura importar", Idle),
        new("Return to catalog", "c", () => CatalogRequested?.Invoke(), "catalogo inicio home"),
        new("Navigation keys", "?", ShowKeys, "teclas ayuda help"),
        new("Quit", "q", () => StudioTerminal.App.RequestStop(), "salir")
    ];

    private string? Idle() => Pending.IsCompleted && _updates.IsEmpty ? null : "Wait for the active run.";
    private string? Active() => Pending.IsCompleted ? "No active run." : null;
    private string? HasRun() => _run.Status == "Ready" ? "Run an entity or open a capture." : null;
    private string? Saveable() => Idle() ?? HasRun();
    private string? ReadyToRun() => Idle() ?? (_entity.CreateAgent is null && _entity.StreamWorkflow is null ? "Archived entity." : null);
    private string? Comparable() => Saveable() ?? (_history.Any(c => c.Started != _started || c.Entity != _entity.Id) ? null : "Needs a second capture.");

    private void SelectPage(int page)
    {
        _page = page;
        _scroll = 0;
        _inspect = false;
    }

    private static void ShowKeys() => MessageBox.Query(StudioTerminal.App, "Keys",
        "Ctrl+P or : find actions   Esc catalog   q quit\n" +
        "1 graph   2 events (Enter inspect)   3 result   4 timeline   j raw/summary\n" +
        "↑↓ node / scroll   ←→ edge / detail tab\n" +
        "Tab inspector   [ ] invocation   e next error\n" +
        "r run   x cancel   i input   m model   v schema\n" +
        "a compare models   t saved cases   g replay   b compare   h history\ns save capture   o open capture", "Close");
}

internal sealed partial class RemoteWorkspace
{
    private StudioAction[]? _commands;
    private StudioAction[] Commands => _commands ??=
    [
        new("View graph", "1", () => ViewPanel(Panel.Graph), "grafo"),
        new("View events", "2", () => ViewPanel(Panel.Events), "eventos timeline"),
        new("View final result", "3", () => ViewPanel(Panel.Result), "resultado salida output"),
        new("Invocation timeline", "4", ShowTimeline, "timing duration duración línea de tiempo", HasRun),
        new("Toggle raw result", "j", ToggleRawResult, "json resultado resumen", HasRun),
        new("Compare models or profiles on saved cases", "a", CompareModels, "evaluation suite comparar modelos perfiles", () => Idle() ?? (_profiles.Length < 2 ? "Configure a second Studio profile." : null)),
        new("Edit input", "i", Edit, "editar entrada JSON", Idle),
        new("Run entity", "r", Submit, "ejecutar correr", ReadyToRun),
        new("Saved inputs & evaluations", "t", OpenCases, "casos pruebas guardar entradas suite regression tests", Idle),
        new("Replay recorded graph", "g", Replay, "reproducir grafo pasos step timeline", HasRun),
        new("Compare runs on graph", "b", () => History(true), "comparar corridas diferencias diff", HasRun),
        new("Change execution profile", "p", Profile, "cambiar modelo perfil model", Idle),
        new("Cancel execution", "x", Cancel, "cancelar", Active),
        new("Open run history", "h", () => History(false), "historial"),
        new("Manage sessions", "s", Sessions, "sesiones", SessionsAvailable),
        new("View conversation", "", Conversation, "conversacion mensajes"),
        new("View metrics", "", () => ViewPanel(Panel.Metrics), "metricas tokens"),
        new("Inspect spans", "", () => ViewPanel(Panel.Spans), "traces trazas"),
        new("Inspect failures", "", () => ViewPanel(Panel.Failures), "errores fallos"),
        new("Prepare a rerun", "", PrepareRerun, "reejecutar repetir corrida", HasRun),
        new("Show input schema", "", ShowSchema, "esquema"),
        new("Refresh journal", "", RefreshJournal, "reconectar actualizar", HasRun),
        new("Return to catalog", "c", Catalog, "catalogo inicio home"),
        new("Quit", "q", () => StudioTerminal.App.RequestStop(), "salir")
    ];

    private string? Idle() => _busy ? "Wait for the pending request." : null;
    private string? HasRun() => _run is null ? "Open a run first." : null;
    private string? Active() => Idle() ?? (_run is null || _run.Snapshot.Field("isTerminal").ValueKind == System.Text.Json.JsonValueKind.True ? "Select an active run." : null);
    private string? ReadyToRun() => Idle() ?? (_draft.Entity.Field("isAvailable").ValueKind == System.Text.Json.JsonValueKind.True ? null : "Entity unavailable.");
    private string? SessionsAvailable() => Idle() ?? (_draft.Entity.Field("supportsSessions").ValueKind == System.Text.Json.JsonValueKind.True ? null : "Entity has no sessions.");

    private void ShowSchema() => Show("Input schema", _draft.Entity.Field("inputSchema").Pretty());

    private void RefreshJournal()
    {
        _pollStopped = false;
        _nextPoll = default;
        Poll(true);
    }
}

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MafPlayground.CLI.Tui;

internal static class RemoteStudio
{
    public static async Task<int> RunAsync(Uri host, string? entity, string? inputFile, CancellationToken token)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            var services = new ServiceCollection();
            StudioClient.AddHttpClient(services);
            using var provider = services.BuildServiceProvider();
            using var client = new StudioClient(host, provider.GetRequiredService<IHttpClientFactory>());
            var instance = await client.InstanceAsync(token);
            string instanceId = instance.GetProperty("hostInstanceId").GetString() ?? throw new InvalidDataException("Missing Studio instance ID.");
            var profiles = (await client.ProfilesAsync(token)).Items().Select(p => p.GetString()!).ToArray();
            string profile = profiles.FirstOrDefault() ?? throw new InvalidDataException("Studio has no execution profiles.");
            var draft = await RemoteDraft.LoadAsync(client, profile, entity, token);
            string? input = inputFile is null ? null : await File.ReadAllTextAsync(inputFile, token);
            var terminal = new StudioTerminal(stop);
            bool interrupted;
            RemoteWorkspace? workspace = null;
            EventHandler<Key>? paletteKey = null;
            try
            {
                workspace = new RemoteWorkspace(client, host.Authority, instanceId, profiles, draft, input, stop.Token);
                paletteKey = ActionPicker.AtRoot(workspace.AvailableActions);
                StudioTerminal.App.Keyboard.KeyDown += paletteKey;
                workspace.Start(entity is null);
                StudioTerminal.RunRoot();
                interrupted = stop.IsCancellationRequested;
            }
            finally
            {
                StudioTerminal.App.Keyboard.KeyDown -= paletteKey;
                terminal.Dispose();
                await terminal.CancelAsync().ConfigureAwait(false);
                if (workspace is not null) await workspace.Pending.ConfigureAwait(false);
            }
            return interrupted ? 130 : 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 130; }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or JsonException or
            HttpRequestException or OperationCanceledException or KeyNotFoundException or InvalidOperationException)
        {
            Console.Error.WriteLine(InspectorRun.Clean(error.Message));
            return 2;
        }
    }
}

internal sealed record RemoteDraft(string Profile, JsonElement[] Catalog, JsonElement Entity, JsonElement Topology)
{
    public string Id => Entity.GetProperty("id").GetString()!;

    public static async Task<RemoteDraft> LoadAsync(StudioClient client, string profile, string? id, CancellationToken token)
    {
        var catalog = (await client.EntitiesAsync(profile, token)).Items();
        var entity = id is null ? catalog.FirstOrDefault(e => e.Field("isAvailable").ValueKind == JsonValueKind.True)
            : catalog.FirstOrDefault(e => e.Str("id") == id);
        if (id is null && entity.ValueKind == JsonValueKind.Undefined) entity = catalog.FirstOrDefault();
        if (entity.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException(id is null ? "Studio has no entities." : "Unknown Studio entity.");
        var topology = entity.Field("isAvailable").ValueKind == JsonValueKind.True
            ? await client.TopologyAsync(entity.GetProperty("id").GetString()!, profile, token) : default;
        return new(profile, catalog, entity, topology);
    }
}

internal sealed partial class RemoteWorkspace
{
    private sealed record EntityState(string Input, string? Session, string RequestId, RemoteJournal? Run, Panel Panel);
    private readonly Dictionary<(string Profile, string Id), EntityState> _visited = [];
    private EntityCatalogView? _catalog;
    private MenuBar _menu = null!;
    private string? _startupInput;
    private readonly StudioClient _client;
    private readonly string _instance;
    private readonly string[] _profiles;
    private readonly CancellationToken _token;
    private readonly ConcurrentQueue<Action> _updates = new();
    private readonly Window _window;
    private readonly Label _header = new() { Width = Dim.Fill() };
    private readonly Label _draftLabel = new() { Y = 1, Width = Dim.Fill() };
    private readonly Label _status = new() { Y = Pos.AnchorEnd(1), Width = Dim.Fill() };
    private readonly ListView _list = new() { Y = 2, Width = Dim.Percent(38), Height = Dim.Fill(1) };
    private readonly StudioTextView _detail = new() { Y = 2, X = Pos.Percent(38), Width = Dim.Fill(), Height = Dim.Fill(1), ReadOnly = true };
    private RemoteDraft _draft;
    private string _input;
    private string? _session;
    private string _requestId = Guid.NewGuid().ToString();
    private RemoteJournal? _run;
    private Task _action = Task.CompletedTask, _poll = Task.CompletedTask;
    private bool _busy, _polling, _pollStopped;
    private DateTimeOffset _nextPoll;
    private string _message = "";
    public Task Pending => Task.WhenAll(_action, _poll, _catalogPoll);

    public RemoteWorkspace(StudioClient client, string host, string instance, string[] profiles, RemoteDraft draft, string? input, CancellationToken token)
    {
        _client = client;
        _instance = instance;
        _profiles = profiles;
        _draft = draft;
        _startupInput = input;
        _input = input ?? draft.Entity.Field("inputExample").Pretty();
        _token = token;
        _window = new Window { Title = "Studio  ·  " + host, Y = 1, Width = Dim.Fill(), Height = Dim.Fill() };
        _window.SetScheme(StudioPalette.Create());
        _window.Add(_header, _draftLabel, _list, _detail, _status);
    }

    internal IEnumerable<StudioAction> AvailableActions() => _catalog?.Actions() ?? Commands;

    public void Start(bool showCatalog)
    {
        _menu = new MenuBar([
            new MenuBarItem("_Workspace", [
                new MenuItem("_Catalog", "", Catalog),
                new MenuItem("_Profile", "", Profile),
                new MenuItem("_Sessions", "", Sessions),
                new MenuItem("_Conversation", "", Conversation),
                new MenuItem("_Quit", "", () => StudioTerminal.App.RequestStop())
            ]),
            new MenuBarItem("_Run", [
                new MenuItem("_Edit input", "", Edit),
                new MenuItem("Input schem_a", "", ShowSchema),
                new MenuItem("Saved inputs and _evaluations", "", OpenCases),
                new MenuItem("Replay _graph", "", Replay),
                new MenuItem("_Submit", "", Submit),
                new MenuItem("_Cancel execution", "", Cancel),
                new MenuItem("_History", "", () => History(false)),
                new MenuItem("Prepare re_run", "", PrepareRerun),
                new MenuItem("Co_mpare against…", "", () => History(true)),
                new MenuItem("Re_fresh journal", "", RefreshJournal)
            ]),
            new MenuBarItem("_Inspect", Enum.GetValues<Panel>().Select(p => new MenuItem(p.ToString(), "", () => ViewPanel(p))))
        ]);
        StudioTerminal.Root.Add(_menu, _window);
        _list.HasFocusChanged += (_, _) => Notice(_message);
        _detail.HasFocusChanged += (_, _) => Notice(_message);
        // ListView's type-to-search consumes printable keys before View.KeyDown.
        StudioTerminal.App.Keyboard.KeyDown += Shortcut;
        _list.ValueChanged += (_, _) => ShowSelection();
        _list.Accepting += (_, e) => { e.Handled = true; InspectSelection(); };
        Refresh();
        _list.SetFocus();
        if (showCatalog) Catalog();
        else _startupInput = null;
        StudioTerminal.App.AddTimeout(TimeSpan.FromMilliseconds(120), () =>
        {
            if (_token.IsCancellationRequested) return false;
            while (_updates.TryDequeue(out var update)) Guard(update);
            if (_catalog is null) Poll();
            else PollCatalog();
            return true;
        });
    }

    private void Shortcut(object? sender, Key e)
    {
        if (e.Handled || StudioTerminal.App.TopRunnableView != StudioTerminal.Root ||
            StudioTerminal.App.Popovers?.GetActivePopover() is not null || _catalog is not null ||
            (!_list.HasFocus && !_detail.HasFocus) || !_window.Enabled) return;
        Guard(() => e.Handled = ActionPicker.Dispatch(Commands, e == Key.Esc ? Key.C : e));
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception error) { Notice(error.Message); }
    }

    private void Request<T>(Func<Task<T>> work, Action<T> apply)
    {
        if (_busy) { Notice("A Studio request is pending."); return; }
        _busy = true;
        Notice("Contacting Studio…");
        _action = BackgroundAsync(work, result => { _busy = false; Notice(""); apply(result); }, error => { _busy = false; Notice(error.Message); });
    }

    private async Task BackgroundAsync<T>(Func<Task<T>> work, Action<T> apply, Action<Exception> fail)
    {
        try
        {
            var result = await Task.Run(work, _token).ConfigureAwait(false);
            _updates.Enqueue(() => apply(result));
        }
        catch (Exception error) { _updates.Enqueue(() => fail(error)); }
    }

    private void Notice(string message)
    {
        _message = InspectorRun.Clean(message).Replace('\n', ' ');
        _catalog?.SetMessage(_message);
        string focus = _detail.HasFocus ? "↑↓ scroll   Tab entries" : "↑↓ entry   Enter inspect   Tab details";
        string panel = _panel == Panel.Result ? "   j raw/summary" : "";
        _status.Text = _message.Length == 0 ? $"{focus}{panel}   Ctrl+P actions   Esc catalog" : _message;
    }

    private void Poll(bool force = false)
    {
        if (_run is not { } run || _polling || !force && (_pollStopped || DateTimeOffset.UtcNow < _nextPoll)) return;
        _polling = true;
        long cursor = run.Cursor;
        _poll = BackgroundAsync(async () =>
        {
            var snapshot = await _client.RunAsync(run.Id, _token).ConfigureAwait(false);
            var page = await _client.EventPageAsync(run.Id, cursor, 32, _token).ConfigureAwait(false);
            return (snapshot, page);
        }, data =>
        {
            _polling = false;
            if (_run != run) return;
            if (data.snapshot.Str("hostInstanceId") != _instance) { _pollStopped = true; Notice("Studio restarted. Reconnect to load its current catalog."); return; }
            bool changed = run.Cursor < data.page.Number("nextCursor") || run.Snapshot.ValueKind == JsonValueKind.Undefined ||
                !JsonElement.DeepEquals(run.Snapshot, data.snapshot);
            run.Apply(data.snapshot, data.page);
            _nextPoll = DateTimeOffset.UtcNow.AddMilliseconds(run.Cursor < data.snapshot.Number("latestSequence") ? 0 : run.Finished ? 2000 : 650);
            if (changed) Refresh();
        }, error =>
        {
            _polling = false;
            if (_run != run) return;
            _pollStopped = true;
            Notice(error.Message + " · Use Run / Refresh journal to reconnect.");
        });
    }

    private void SelectRun(string id)
    {
        _run = new(id);
        _pollStopped = false;
        _nextPoll = default;
        ViewPanel(Panel.Graph);
        Poll();
    }

    private void ApplyDraft(RemoteDraft draft, string? input = null)
    {
        _draft = draft;
        _input = input ?? draft.Entity.Field("inputExample").Pretty();
        _session = null;
        _requestId = Guid.NewGuid().ToString();
        _run = null;
        ViewPanel(Panel.Graph);
    }

    private static int Pick(string title, string[] labels)
    {
        if (labels.Length == 0) { MessageBox.Query(StudioTerminal.App, title, "No retained entries.", "Close"); return -1; }
        using var dialog = new Dialog { Title = InspectorRun.Clean(title), Width = Dim.Percent(88), Height = Dim.Percent(75) };
        int index = -1;
        var list = new ListView { Width = Dim.Fill(), Height = Dim.Fill() };
        list.SetSource<string>([.. labels.Select(s => InspectorRun.Clean(s).Replace('\n', ' '))]);
        list.SelectedItem = 0;
        list.Accepting += (_, e) => { e.Handled = true; index = list.SelectedItem ?? -1; StudioTerminal.App.RequestStop(); };
        dialog.Add(list);
        list.SetFocus();
        StudioTerminal.App.Run(dialog);
        return index;
    }

    private static void Show(string title, string body)
    {
        using var dialog = new Dialog { Title = InspectorRun.Clean(title), Width = Dim.Percent(94), Height = Dim.Percent(90) };
        var view = new StudioTextView { Text = InspectorRun.Clean(body), ReadOnly = true, Width = Dim.Fill(), Height = Dim.Fill(), WordWrap = true };
        dialog.Add(view);
        StudioTerminal.App.Run(dialog);
    }

    private void Catalog()
    {
        if (_busy || _catalog is not null) return;
        var entries = _draft.Catalog.Select(e => new CatalogEntry(e.Label("id"), e.Field("kind").ToString() switch
        { "0" => "Agent", "1" => "Workflow", _ => e.Label("kind") },
            InspectorRun.Clean(e.Str("unavailableReason") ?? e.Str("description") ?? ""),
            e.Field("isAvailable").ValueKind == JsonValueKind.True,
            e.Field("inputExample") is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } example
                ? InspectorRun.Clean(example.Pretty()) : null)).ToArray();
        _catalog = new EntityCatalogView(_draft.Profile, entries, id => OpenEntity(id));
        StudioTerminal.Root.Add(_catalog);
        _catalog.Select(_draft.Id);
        _window.Visible = _menu.Visible = false;
        _catalog.FocusList();
        _catalog.SetActivity([], "Loading run history…");
        _nextCatalogPoll = default;
        PollCatalog();
    }

    private void OpenEntity(string id, string? runId = null)
    {
        if (_busy) return;
        if (id == _draft.Id)
        {
            CloseCatalog();
            if (runId is not null) SelectRun(runId);
            return;
        }
        Request(() => RemoteDraft.LoadAsync(_client, _draft.Profile, id, _token), next =>
        {
            if (_startupInput is not null) _input = _draft.Entity.Field("inputExample").Pretty();
            _visited[(_draft.Profile, _draft.Id)] = new(_input, _session, _requestId, _run, _panel);
            ApplyDraft(next, _startupInput);
            if (_visited.Remove((next.Profile, next.Id), out var state))
            {
                (_input, _session, _requestId, _run) = (state.Input, state.Session, state.RequestId, state.Run);
                _pollStopped = false;
                _nextPoll = default;
                ViewPanel(state.Panel);
            }
            CloseCatalog();
            if (runId is not null) SelectRun(runId);
        });
    }

    private void CloseCatalog()
    {
        _startupInput = null;
        if (_catalog is null) return;
        StudioTerminal.Root.Remove(_catalog);
        _catalog.Dispose();
        _catalog = null;
        _menu.Visible = _window.Visible = true;
        _list.SetFocus();
    }

    private void Profile()
    {
        int i = Pick("Execution profile", _profiles);
        if (i >= 0) Request(() => RemoteDraft.LoadAsync(_client, _profiles[i], null, _token), draft => ApplyDraft(draft));
    }

    private void Edit()
    {
        if (_busy) { Notice("A Studio request is pending."); return; }
        if (EditInput(_input, "Input JSON · " + _draft.Entity.Label("inputType"), _draft.Id) is not { } edited) return;
        _input = edited;
        _requestId = Guid.NewGuid().ToString();
    }

    private string? EditInput(string initial, string title, string entity)
    {
        string? edited = null;
        using var dialog = new Dialog { Title = title, Width = Dim.Percent(92), Height = Dim.Percent(80) };
        var editor = new StudioTextView { Text = initial, Width = Dim.Fill(), Height = Dim.Fill(3), TabKeyAddsTab = false };
        var save = new Button { Text = "Apply" };
        save.Accepting += (_, e) =>
        {
            e.Handled = true;
            var text = editor.Text;
            try { using var parsed = JsonDocument.Parse(text); }
            catch (JsonException error) { MessageBox.ErrorQuery(StudioTerminal.App, "Invalid JSON", error.Message, "Edit"); return; }
            edited = text;
            StudioTerminal.App.RequestStop();
        };
        dialog.Add(editor, new Label { Text = "Tab → Apply    Enter confirm    Esc cancel", Y = Pos.Bottom(editor) });
        dialog.AddButton(save);
        dialog.AddButton(CaseLibrary.SaveButton(entity, () => editor.Text, _token));
        editor.SetFocus();
        StudioTerminal.App.Run(dialog);
        return edited;
    }

    private void Submit()
    {
        if (_draft.Entity.Field("isAvailable").ValueKind != JsonValueKind.True) { Notice(_draft.Entity.Str("unavailableReason") ?? "Entity unavailable."); return; }
        Request(() => _client.StartRunAsync(_requestId, _draft.Id, _draft.Profile, JsonSerializer.Deserialize<JsonElement>(_input), _session, _token), receipt =>
        {
            _requestId = Guid.NewGuid().ToString();
            if (receipt.Str("hostInstanceId") != _instance) { Notice("Studio restarted. Reconnect to open the submitted run."); return; }
            SelectRun(receipt.GetProperty("runId").GetString()!);
        });
    }

    private void Cancel()
    {
        if (_run is not { } run || run.Snapshot.Field("isTerminal").ValueKind == JsonValueKind.True) { Notice("Select an active run."); return; }
        Request(async () => { await _client.CancelAsync(run.Id, _token).ConfigureAwait(false); return run.Id; },
            id => { Notice("Cancellation requested for " + id); _pollStopped = false; _nextPoll = default; });
    }

    private void PrepareRerun()
    {
        if (_run?.Snapshot.Field("request") is not { ValueKind: JsonValueKind.Object } request) { Notice("Open a retained run first."); return; }
        Request(() => RemoteDraft.LoadAsync(_client, request.Str("profileId")!, request.Str("entityId"), _token), draft =>
        {
            string? edited = EditInput(request.Field("input").Pretty(), $"Review captured input · {draft.Id} / {draft.Profile} · no session", draft.Id);
            if (edited is null) return;
            ApplyDraft(draft, edited);
            Notice("Rerun draft ready. Press r to submit.");
        });
    }

    private void History(bool compare)
    {
        if (compare && _run is null) { Notice("Open a baseline run first."); return; }
        Request(() => _client.RunsAsync(_token), result =>
        {
            var runs = result.Items().Where(r => r.Str("hostInstanceId") == _instance && (!compare || r.Str("runId") != _run?.Id)).ToArray();
            int i = Pick(compare ? "Compare selected run with…" : "Run history", runs.Select(r =>
                $"{r.Label("submittedAt")}   {r.Field("request").Label("entityId")}   {r.RunStatus()}   {r.Label("runId")}").ToArray());
            if (i < 0) return;
            string id = runs[i].GetProperty("runId").GetString()!;
            if (compare) CompareRuns(_run!.Id, id);
            else SelectRun(id);
        });
    }

    private void Sessions()
    {
        if (_draft.Entity.Field("supportsSessions").ValueKind != JsonValueKind.True) { Notice("This entity does not support sessions."); return; }
        Request(() => _client.SessionsAsync(_token), result =>
        {
            var sessions = result.Items().Where(s => s.Str("entityId") == _draft.Id && s.Str("profileId") == _draft.Profile).ToArray();
            List<string> choices = ["Continue without a session", "Create session"];
            if (_session is not null) choices.Add("Reset current session");
            int offset = choices.Count;
            choices.AddRange(sessions.Select(s => $"{s.Label("id")}   {s.Label("createdAt")}"));
            int i = Pick("Sessions · " + _draft.Id, [.. choices]);
            if (i < 0) return;
            if (i == 1) Request(() => _client.CreateSessionAsync(_draft.Id, _draft.Profile, _token), s => SelectSession(s.Str("id")));
            else if (i == 2 && offset == 3)
            {
                if (MessageBox.Query(StudioTerminal.App, "Reset session", "Replace this session and its saved conversation context?", "Keep", "Reset") == 1)
                    Request(() => _client.ResetSessionAsync(_session!, _token), s => SelectSession(s.Str("id")));
            }
            else SelectSession(i == 0 ? null : sessions[i - offset].Str("id"));
        });
    }

    private void SelectSession(string? id)
    {
        _session = id;
        _requestId = Guid.NewGuid().ToString();
        _run = null;
        ViewPanel(Panel.Graph);
    }

    private void Conversation()
    {
        if (_session is null) { Notice("Select a session to inspect its turns."); return; }
        Request(() => _client.RunsAsync(_token), result =>
        {
            var turns = result.Items().Where(r => r.Field("request").Str("sessionId") == _session).OrderBy(r => r.Str("submittedAt")).ToArray();
            int i = Pick("Conversation · choose a turn", turns.Select(r => $"{r.Label("submittedAt")}  {r.RunStatus()}  {r.Field("request").Field("input")}").ToArray());
            if (i >= 0) SelectRun(turns[i].Str("runId")!);
        });
    }
}

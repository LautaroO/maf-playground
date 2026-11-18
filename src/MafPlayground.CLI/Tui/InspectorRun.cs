using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using MafPlayground.AI.Workflows.Translation;
using Microsoft.Agents.AI.Workflows;

namespace MafPlayground.CLI.Tui;

internal sealed class Invocation(string input)
{
    public string Input { get; } = input;
    internal long StartedAt { get; init; } = Stopwatch.GetTimestamp();
    public TimeSpan? Duration
    {
        get => field ?? (Finished ? null : Stopwatch.GetElapsedTime(StartedAt));
        set;
    }
    public string? CallId { get; init; }
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
    public bool Failed { get; set; }
    public bool Rejected { get; set; }
    public bool Finished { get; set; }
}

// Owned by the UI thread. Event arrival never changes the selected node or invocation.
internal sealed partial class InspectorRun
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };
    private readonly Dictionary<string, List<Invocation>> _attempts = [];
    private readonly List<string> _toolNodes = [];
    private readonly Dictionary<int, (string Id, int Index)> _eventTargets = [];
    private TimeSpan? _savedElapsed;
    public IReadOnlyList<string> ToolNodes => _toolNodes;
    public (string Id, int Index)? EventTarget(int eventIndex) =>
        _eventTargets.TryGetValue(eventIndex, out var target) ? target : null;
    public bool IsActive(string id) => Attempts(id).Any(attempt => !attempt.Finished);
    public List<string> Events { get; } = [];
    public string Result { get; private set; } = "";
    public string Status { get; private set; } = "Ready";
    private Stopwatch? _timer;
    public TimeSpan? Elapsed => _timer?.Elapsed ?? _savedElapsed;

    public void Start()
    {
        Status = "Running";
        _timer = Stopwatch.StartNew();
    }

    public IReadOnlyList<Invocation> Attempts(string id) => _attempts.TryGetValue(id, out var list) ? list : [];

    public void Apply(WorkflowEvent item, long? timestamp = null)
    {
        switch (item)
        {
            case ExecutorInvokedEvent start:
                Begin(start.ExecutorId, start.Data, timestamp);
                break;
            case ExecutorCompletedEvent done:
                End(done.ExecutorId, done.Data, timestamp: timestamp);
                break;
            case ExecutorFailedEvent failure:
                End(failure.ExecutorId, null, failure.Data?.Message ?? "Executor failed", timestamp);
                break;
            case TranslationDraftEvent draft:
                if (Attempts(draft.ExecutorId).ElementAtOrDefault(draft.Attempt - 1) is { } draftAttempt)
                {
                    draftAttempt.Output = Format(draft.Data);
                    if (draft.Error is { } reason)
                    {
                        draftAttempt.Failed = true;
                        draftAttempt.Error = Clean(reason);
                    }
                }
                break;
            case TranslationValidationEvent validated:
                var attempt = Attempts(validated.ExecutorId).ElementAtOrDefault(validated.Validation.Attempts - 1);
                if (attempt is not null)
                {
                    if (!validated.Validation.IsValid)
                    {
                        _eventTargets[Events.Count] = (validated.ExecutorId, validated.Validation.Attempts - 1);
                        Events.Add($"× {validated.ExecutorId}  ·  rejected");
                    }
                    attempt.Output = Format(validated.Validation);
                    attempt.Rejected = !validated.Validation.IsValid;
                    attempt.Error = validated.Validation.IsValid ? "" : Clean(validated.Validation.Error ?? string.Join("\n",
                        validated.Validation.Issues.Select(issue => $"{issue.Code}: {issue.Description}")));
                }
                break;
            case WorkflowOutputEvent output:
                Complete(output.Data);
                if (output.ExecutorId is { } emitter && Attempts(emitter).LastOrDefault() is { } invocation)
                {
                    invocation.Output = Result;
                }
                break;
            case WorkflowErrorEvent error:
                Fail((error.Data as Exception)?.Message ?? "Workflow failed");
                break;
        }
    }

    public void Begin(string id, object? data, long? timestamp = null) =>
        AddInvocation(id, new Invocation(Format(data)) { StartedAt = timestamp ?? Stopwatch.GetTimestamp() });

    internal void BeginTool(string id, Invocation invocation)
    {
        if (!_toolNodes.Contains(id)) _toolNodes.Add(id);
        AddInvocation(id, invocation);
    }

    private void AddInvocation(string id, Invocation invocation)
    {
        if (!_attempts.TryGetValue(id, out var list)) _attempts[id] = list = [];
        _eventTargets[Events.Count] = (id, list.Count);
        list.Add(invocation);
        Events.Add($"▶ {id}  ·  {list.Count}");
    }

    public void End(string id, object? data, string error = "", long? timestamp = null)
    {
        var current = Attempts(id).FirstOrDefault(attempt => !attempt.Finished);
        if (current is null)
        {
            Begin(id, null, timestamp);
            current = Attempts(id)[^1];
        }
        Finish(id, current, Format(data), error, timestamp ?? Stopwatch.GetTimestamp());
    }

    internal void Finish(string id, Invocation current, string output, string error, long timestamp)
    {
        if (current.Finished) return;
        if (current.Output.Length == 0) current.Output = output;
        if (error.Length != 0)
        {
            current.Error = Clean(error);
            current.Failed = true;
        }
        current.Finished = true;
        current.Duration = Stopwatch.GetElapsedTime(current.StartedAt, timestamp);
        _eventTargets[Events.Count] = (id, _attempts[id].IndexOf(current));
        Events.Add($"{(current.Failed || current.Rejected ? "×" : "✓")} {id}");
    }

    public void Cancel()
    {
        Status = "Cancelled";
        _timer?.Stop();
        CloseActive("Cancelled");
        Events.Add("■ Cancelled");
    }

    private void CloseActive(string reason)
    {
        long timestamp = Stopwatch.GetTimestamp();
        foreach (var (id, attempts) in _attempts)
            foreach (var attempt in attempts.Where(attempt => !attempt.Finished))
                Finish(id, attempt, "", reason, timestamp);
    }

    public void Complete(object? output)
    {
        _timer?.Stop();
        Result = Format(output);
        Status = output is TranslationWorkflowResult translation && translation.Translations.Any(t => !t.IsValid)
            ? "Partial result" : "Completed";
    }

    public void Fail(string reason)
    {
        _timer?.Stop();
        CloseActive(reason);
        Status = "Failed";
        Result = Clean(reason);
        Events.Add("× " + Result);
    }

    public static string Format(object? value)
    {
        if (value is null) return "(no payload)";
        try { return Clean(JsonSerializer.Serialize(value, Json)); }
        catch (Exception e) when (e is NotSupportedException or JsonException) { return "(payload unavailable)"; }
    }

    public static string Clean(string text) =>
        string.Concat(text.Take(65536).Where(c => c == '\n' || !char.IsControl(c))) +
        (text.Length > 65536 ? "\n… truncated" : "");
}

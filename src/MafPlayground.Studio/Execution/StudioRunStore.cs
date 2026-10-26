using System.Security.Cryptography;
using MafPlayground.Studio.Capture;
using System.Text;
using System.Text.Json;
using MafPlayground.AI.Abstractions;
using MafPlayground.Studio.Contracts;

namespace MafPlayground.Studio.Execution;

internal sealed class StudioRunStore(StudioOptions options, TimeProvider clock, StudioSourceProvenance provenance)
{
    private readonly StudioPayloadStore payloads = new(options);
    internal object Gate { get; } = new();
    internal string HostInstanceId { get; } = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, Tombstone> requests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RunState> runs = new(StringComparer.Ordinal);
    private readonly HashSet<string> sessions = new(StringComparer.Ordinal);
    private readonly LinkedList<string> completed = new();
    private long metadataBytes;
    private long captureBytes;
    private static readonly JsonElement Truncated = JsonSerializer.SerializeToElement(new { captureState = "truncated", reason = "capture_limit" });

    internal StudioRunReceipt? FindRetry(StudioRunRequest request, AICallerContext caller)
    {
        if (!requests.TryGetValue(RequestKey(request.RequestId), out Tombstone? old)) return null;
        if (old.Fingerprint != Fingerprint(request, caller)) throw new StudioRequestConflictException();
        return old.Receipt;
    }

    internal void CheckSession(string? sessionId)
    {
        if (sessionId is not null && sessions.Contains(sessionId)) throw new StudioSessionBusyException();
    }

    internal RunState Add(StudioRunRequest request, AICallerContext caller)
    {
        long metadata = 1024L + 2L * (request.RequestId.Length + request.EntityId.Length + request.ProfileId.Length +
            (request.SessionId?.Length ?? 0) + caller.SubjectId.Length + (caller.TenantId?.Length ?? 0));
        if (requests.Count >= options.MaxRequestRecords || metadataBytes + metadata > options.MaxMetadataBytes)
            throw new StudioCapacityException("Host-lifetime request metadata is full; restart the host to clear it.");
        CheckSession(request.SessionId);
        int inputBytes = JsonBytes(request.Input);
        if (inputBytes > options.MaxPayloadBytes) throw new ArgumentException("The submitted input exceeds the payload limit.", nameof(request));
        StudioRunReceipt receipt = new(HostInstanceId, Guid.NewGuid().ToString("N"));
        // The accepted job owns the JSON and validated caller, never a request scope.
        StudioRunRequest owned = request with { Input = request.Input.Clone() };
        StudioPayload safeInput = payloads.Capture(owned.Input, "application/json");
        StudioRunRequest inspectable = owned with
        {
            Input = safeInput.Value,
            RequestId = payloads.Redactor.RedactText(owned.RequestId),
            EntityId = payloads.Redactor.RedactText(owned.EntityId),
            ProfileId = payloads.Redactor.RedactText(owned.ProfileId),
            SessionId = owned.SessionId is null ? null : payloads.Redactor.RedactText(owned.SessionId),
        };
        inputBytes = JsonBytes(safeInput.Value);
        EvictForSpace(inputBytes);
        if (captureBytes + inputBytes > options.MaxCaptureBytes) throw new StudioCapacityException("Active runs occupy the capture budget.");
        RunState state = new(receipt, inspectable, new AICallerContext(caller.SubjectId, caller.TenantId), clock.GetUtcNow(), options.MaxEventsPerRun, inputBytes) { ExecutionRequest = owned, Incomplete = safeInput.State is StudioCaptureState.Truncated or StudioCaptureState.Unavailable };
        requests.Add(RequestKey(request.RequestId), new(Fingerprint(request, caller), receipt));
        metadataBytes += metadata;
        runs.Add(receipt.RunId, state);
        if (request.SessionId is not null) sessions.Add(request.SessionId);
        captureBytes += inputBytes;
        Lifecycle(state, "queued");
        return state;
    }

    internal IReadOnlyList<StudioRunSnapshot> List() => runs.Values.OrderByDescending(run => run.SubmittedAt).Select(Snapshot).ToArray();

    internal RunState? Find(string id) => runs.GetValueOrDefault(id);

    internal StudioRunSnapshot Snapshot(RunState state) => new(
        HostInstanceId, state.Receipt.RunId, state.Request, state.Status, state.CancellationRequested,
        state.SubmittedAt, state.StartedAt, state.CompletedAt, state.Result, state.Outcome,
        state.Topology, state.ErrorCategory, state.Journal.LatestSequence, state.CapturedBytes,
        !state.Incomplete && !state.Journal.HasLostEvents, state.Journal.EarliestSequence, state.Journal.LatestSequence, state.Profile, provenance);

    internal void Running(RunState state)
    {
        state.Status = StudioRunStatus.Running;
        state.StartedAt = clock.GetUtcNow();
        Lifecycle(state, "running");
    }

    internal void SetProfile(RunState state, StudioExecutionProfileSnapshot profile)
    {
        profile = profile with { Id = payloads.Redactor.RedactText(profile.Id), Model = profile.Model is null ? null : payloads.Redactor.RedactText(profile.Model), GuardProfile = profile.GuardProfile is null ? null : payloads.Redactor.RedactText(profile.GuardProfile) };
        int bytes = JsonSerializer.SerializeToUtf8Bytes(profile).Length;
        if (bytes > options.MaxPayloadBytes || !MakeSpace(state, bytes)) { state.Incomplete = true; return; }
        state.Profile = profile;
        AddBytes(state, bytes);
    }

    internal void SetTopology(RunState state, StudioTopology topology)
    {
        StudioPayload captured = payloads.Capture(topology, "application/json");
        if (captured.State is StudioCaptureState.Truncated or StudioCaptureState.Unavailable) { state.Incomplete = true; return; }
        topology = captured.Value.Deserialize<StudioTopology>()!;
        int bytes = JsonBytes(captured.Value);
        if (bytes > options.MaxPayloadBytes || !MakeSpace(state, bytes))
        {
            state.Incomplete = true;
            return;
        }
        // Freeze collections supplied by catalog implementations.
        state.Topology = topology with
        {
            Nodes = Array.AsReadOnly(topology.Nodes.ToArray()),
            Edges = Array.AsReadOnly(topology.Edges.Select(edge => edge with
            { SourceIds = Array.AsReadOnly(edge.SourceIds.ToArray()), TargetIds = Array.AsReadOnly(edge.TargetIds.ToArray()) }).ToArray())
        };
        AddBytes(state, bytes);
    }

    internal void Finish(RunState state, StudioRunStatus status, JsonElement? result, string? error, StudioOutcomeClassification outcome)
    {
        if (result is { } value)
        {
            StudioPayload captured = payloads.Capture(value, "application/json");
            value = captured.Value;
            state.Incomplete |= captured.State is StudioCaptureState.Truncated or StudioCaptureState.Unavailable;
            int bytes = JsonBytes(value);
            if (bytes > options.MaxPayloadBytes || !MakeSpace(state, bytes))
            {
                value = Truncated;
                bytes = JsonBytes(value);
                state.Incomplete = true;
            }
            if (MakeSpace(state, bytes))
            {
                state.Result = value.Clone();
                AddBytes(state, bytes);
            }
            else state.Incomplete = true;
        }
        if (state.ExecutionRequest?.SessionId is { } sessionId) sessions.Remove(sessionId);
        state.ExecutionRequest = null; // Release the raw execution envelope on every terminal path.
        state.Outcome = outcome;
        state.Status = status;
        state.ErrorCategory = error;
        state.CompletedAt = clock.GetUtcNow();
        Lifecycle(state, status.ToString());
        completed.AddLast(state.Receipt.RunId);
        while (completed.Count > options.MaxCompletedRuns) EvictOldest();
        state.Journal.Pulse();
    }

    internal bool RequestCancellation(RunState state)
    {
        if (state.IsTerminal) return false;
        if (!state.CancellationRequested)
        {
            state.CancellationRequested = true;
            Lifecycle(state, "cancellation_requested");
        }
        return true;
    }

    internal RunState[] Active() => runs.Values.Where(run => !run.IsTerminal).ToArray();

    internal void Record(StudioEvent item)
    {
        if (!runs.TryGetValue(item.RunId, out RunState? state)) return;
        state.Incomplete |= item.CaptureState is StudioCaptureState.Truncated or StudioCaptureState.Unavailable;
        long sequence = state.Journal.NextSequence();
        if (string.IsNullOrWhiteSpace(item.Kind) || item.Kind.Length > 256 ||
            item.ExecutorId?.Length > 256 || item.InvocationId?.Length > 256 || item.SpanId?.Length > 256 ||
            item.Data.ValueKind == JsonValueKind.Undefined)
        {
            state.Journal.Drop();
            return;
        }
        StudioPayload captured = payloads.Capture(item.Data, "application/json");
        JsonElement data = captured.Value;
        state.Incomplete |= captured.State is StudioCaptureState.Truncated or StudioCaptureState.Unavailable;
        if (JsonBytes(data) > options.MaxPayloadBytes)
        {
            data = Truncated;
            state.Incomplete = true;
        }
        StudioEvent owned = item with
        {
            Sequence = sequence,
            Data = data,
            CaptureState = (StudioCaptureState)Math.Max((int)item.CaptureState, (int)captured.State),
            Kind = payloads.Redactor.RedactText(item.Kind),
            ExecutorId = item.ExecutorId is null ? null : payloads.Redactor.RedactText(item.ExecutorId),
            InvocationId = item.InvocationId is null ? null : payloads.Redactor.RedactText(item.InvocationId),
            SpanId = item.SpanId is null ? null : payloads.Redactor.RedactText(item.SpanId)
        };
        int bytes = JsonSerializer.SerializeToUtf8Bytes(owned).Length;
        if (!MakeSpace(state, bytes))
        {
            state.Journal.Drop();
            return;
        }
        long previous = state.Journal.Bytes;
        state.Journal.Append(owned with { Data = data.Clone() }, bytes);
        AddBytes(state, state.Journal.Bytes - previous);
    }

    private void Lifecycle(RunState state, string kind) => Record(new(state.Receipt.RunId, 0, clock.GetUtcNow(), kind, null, null, null, JsonSerializer.SerializeToElement(new { status = state.Status.ToString(), state.CancellationRequested })));

    private bool MakeSpace(RunState state, int bytes)
    {
        EvictForSpace(bytes);
        // Capture pressure drops journal data, never active cancellation/session control.
        while (captureBytes + bytes > options.MaxCaptureBytes)
        {
            RunState? victim = runs.Values.FirstOrDefault(run => run.Journal.Bytes > 0);
            if (victim is null) return false;
            int removed = victim.Journal.RemoveOldest();
            AddBytes(victim, -removed);
            victim.Journal.Pulse();
        }
        return runs.ContainsKey(state.Receipt.RunId);
    }

    private void AddBytes(RunState state, long bytes)
    {
        state.CapturedBytes += bytes;
        captureBytes += bytes;
    }
    private void EvictForSpace(int bytes)
    {
        while (captureBytes + bytes > options.MaxCaptureBytes && completed.Count > 0) EvictOldest();
    }
    private void EvictOldest()
    {
        string id = completed.First!.Value;
        completed.RemoveFirst();
        RunState state = runs[id];
        runs.Remove(id);
        captureBytes -= state.CapturedBytes;
        state.Journal.Pulse();
    }

    private static int JsonBytes(JsonElement value) => Encoding.UTF8.GetByteCount(value.GetRawText());

    private static string RequestKey(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));

    private static string Fingerprint(StudioRunRequest request, AICallerContext caller)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        { request.EntityId, request.ProfileId, request.SessionId, request.Input, caller.SubjectId, caller.TenantId });
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private sealed record Tombstone(string Fingerprint, StudioRunReceipt Receipt);

    internal sealed class RunState(StudioRunReceipt receipt, StudioRunRequest request, AICallerContext caller, DateTimeOffset submittedAt, int journalCapacity, int inputBytes)
    {
        internal StudioRunRequest? ExecutionRequest { get; set; }
        internal StudioRunReceipt Receipt { get; } = receipt;
        internal StudioRunRequest Request { get; } = request;
        internal AICallerContext Caller { get; } = caller;
        internal CancellationTokenSource Cancellation { get; } = new();
        internal Task? CancellationWork { get; set; }
        internal bool Finalizing { get; set; }
        internal bool TimeoutRequested { get; set; }
        internal StudioEventJournal Journal { get; } = new(journalCapacity);
        internal DateTimeOffset SubmittedAt { get; } = submittedAt;
        internal DateTimeOffset? StartedAt { get; set; }
        internal DateTimeOffset? CompletedAt { get; set; }
        internal StudioExecutionProfileSnapshot? Profile { get; set; }
        internal StudioOutcomeClassification Outcome { get; set; }
        internal StudioRunStatus Status { get; set; } = StudioRunStatus.Queued;
        internal bool IsTerminal => Status is not (StudioRunStatus.Queued or StudioRunStatus.Running);
        internal bool CancellationRequested { get; set; }
        internal bool Incomplete { get; set; }
        internal long CapturedBytes { get; set; } = inputBytes;
        internal JsonElement? Result { get; set; }
        internal StudioTopology? Topology { get; set; }
        internal string? ErrorCategory { get; set; }
    }
}

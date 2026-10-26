using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Http;
using MafPlayground.Studio.Execution;
using Microsoft.Extensions.DependencyInjection;
using MafPlayground.Studio.Tests.Fixtures;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MafPlayground.Studio.Tests.Contract;

public sealed class StudioMcpContractTests
{
    private const string Api = StudioEndpoints.Prefix;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task Official_client_discovers_only_bounded_studio_tools_and_dynamic_entity_contracts()
    {
        await using var host = await StudioTransportHost.Create();
        await using var mcp = await host.Connect();
        var tools = await mcp.ListToolsAsync();
        Assert.Equal(new[] { "studio_cancel_run", "studio_get_events", "studio_get_payload", "studio_get_run", "studio_get_spans", "studio_get_topology", "studio_list_entities", "studio_list_profiles", "studio_list_runs", "studio_reset_session", "studio_start_run" }, tools.Select(t => t.Name).Order());
        Assert.All(tools, t => Assert.NotNull(t.ProtocolTool.Annotations?.ReadOnlyHint));
        var entities = (await Call<StudioMcpPage<StudioEntityDescriptor>>(mcp, "studio_list_entities")).Items;
        var http = (await host.Http.GetFromJsonAsync<StudioEntityDescriptor[]>(Api + "/entities"))!;
        Assert.Equal(http.Select(e => e.Id), entities.Select(e => e.Id));
        var novel = Assert.Single(entities, e => e.Id == "novel-review");
        Assert.True(novel.InputSchema.GetProperty("properties").TryGetProperty("review_items", out _));
        Assert.False(novel.SupportsStreaming);
        var topology = await Call<StudioTopology>(mcp, "studio_get_topology", new() { ["entityId"] = novel.Id, ["profileId"] = "cli-default" });
        Assert.Equal(StudioTopologyStatus.Unavailable, topology.Status);
        Assert.NotEmpty(topology.UnavailableReason!);
        var native = await Call<StudioTopology>(mcp, "studio_get_topology", new() { ["entityId"] = "translation-workflow", ["profileId"] = "cli-default" });
        Assert.Equal(StudioTopologyStatus.Available, native.Status);
        Assert.NotEmpty(native.Nodes);
    }
    [Fact]
    public async Task Http_and_mcp_duplicate_submission_share_the_same_job_and_one_model_call()
    {
        await using var host = await StudioTransportHost.Create();
        await using var mcp = await host.Connect();
        var request = new StudioRunRequest("same-request", "basic-agent", "cli-default", JsonSerializer.SerializeToElement(new { message = "hello" }), null);
        var first = await host.Http.PostAsJsonAsync(Api + "/runs", request);
        var receipt = (await first.Content.ReadFromJsonAsync<StudioRunReceipt>())!;
        var duplicate = await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request });
        Assert.Equal(receipt, duplicate);
        var final = await host.Terminal(receipt.RunId);
        var actual = await Call<StudioRunSnapshot>(mcp, "studio_get_run", new() { ["runId"] = receipt.RunId });
        Assert.Equal(final.Result!.Value.GetRawText(), actual.Result!.Value.GetRawText());
        Assert.Equal(final.RunId, actual.RunId);
        Assert.Single(host.Model.Requests);
    }
    [Fact]
    public async Task Typed_unknown_entity_executes_and_event_cursor_is_bounded_and_validated()
    {
        await using var host = await StudioTransportHost.Create();
        await using var mcp = await host.Connect();
        var request = new StudioRunRequest("novel-request", "novel-review", "cli-default", JsonSerializer.SerializeToElement(new { review_items = new[] { new { title = "One", priority = "Urgent" } } }), null);
        var receipt = await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request });
        var run = await host.Terminal(receipt.RunId);
        Assert.Equal("One", run.Result!.Value.GetProperty("titles")[0].GetString());
        var first = await Call<StudioEventPage>(mcp, "studio_get_events", new() { ["runId"] = receipt.RunId, ["limit"] = 1 });
        Assert.Single(first.Events);
        var next = await Call<StudioEventPage>(mcp, "studio_get_events", new() { ["runId"] = receipt.RunId, ["after"] = first.NextCursor, ["limit"] = 1 });
        Assert.True(next.Events[0].Sequence > first.NextCursor);
        foreach (var arguments in new[] { new Dictionary<string, object?> { ["runId"] = receipt.RunId, ["limit"] = 1001 }, new() { ["runId"] = receipt.RunId, ["after"] = -1 }, new() { ["runId"] = receipt.RunId, ["limit"] = "private-malformed" } })
        {
            var error = await mcp.CallToolAsync("studio_get_events", arguments);
            Assert.True(error.IsError);
            Assert.DoesNotContain("private-malformed", JsonSerializer.Serialize(error));
        }
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("wrong")]
    [InlineData("cookie")]
    [InlineData("foreign-host")]
    [InlineData("foreign-origin")]
    public async Task Bearer_boundary_rejects_before_sdk_binding(string mode)
    {
        await using var host = await StudioTransportHost.Create();
        host.Http.DefaultRequestHeaders.Authorization = null;
        if (mode == "wrong") host.Http.DefaultRequestHeaders.Authorization = new("Bearer", "private-wrong");
        if (mode == "cookie")
        {
            Assert.True(host.Policy.TryExchange(host.Policy.BootstrapCode, out var cookie));
            host.Http.DefaultRequestHeaders.Add("Cookie", StudioAccessPolicy.CookieName + "=" + cookie);
        }
        if (mode.StartsWith("foreign", StringComparison.Ordinal)) host.Http.DefaultRequestHeaders.Authorization = new("Bearer", host.Policy.BearerToken);
        if (mode == "foreign-host") host.Http.DefaultRequestHeaders.Host = "foreign.test:5051";
        if (mode == "foreign-origin") host.Http.DefaultRequestHeaders.Add("Origin", "http://foreign.test:5051");
        using var response = await host.Http.PostAsync("/mcp", new StringContent("malformed-private-body"));
        Assert.Equal(mode.StartsWith("foreign", StringComparison.Ordinal) ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private", body);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<Exception>(() => host.Connect(timeout.Token));
    }
    [Fact]
    public async Task Native_unknown_graph_and_profiles_use_fresh_scopes_and_list_pages_have_explicit_cursors()
    {
        await using var host = await StudioTransportHost.Create();
        await using var mcp = await host.Connect();
        foreach (string profile in new[] { "cli-default", "alternate", "cli-default" })
        {
            var topology = await Call<StudioTopology>(mcp, "studio_get_topology", new() { ["entityId"] = "novel-native", ["profileId"] = profile });
            var http = (await host.Http.GetFromJsonAsync<StudioTopology>(Api + $"/entities/novel-native/topology?profileId={profile}"))!;
            Assert.Equal(http.Nodes.Select(n => n.Id), topology.Nodes.Select(n => n.Id));
            Assert.Contains(topology.Nodes, n => n.Id == "finish-" + profile);
        }
        var first = await Call<StudioMcpPage<StudioEntityDescriptor>>(mcp, "studio_list_entities", new() { ["limit"] = 1 });
        Assert.Single(first.Items);
        Assert.Equal(1, first.NextCursor);
        var second = await Call<StudioMcpPage<StudioEntityDescriptor>>(mcp, "studio_list_entities", new() { ["limit"] = 1, ["after"] = first.NextCursor });
        Assert.NotEqual(first.Items[0].Id, second.Items[0].Id);
        Assert.Equal(new[] { "alternate", "cli-default" }, (await Call<StudioMcpPage<string>>(mcp, "studio_list_profiles")).Items.Order());
        Assert.True((await mcp.CallToolAsync("studio_list_runs", new Dictionary<string, object?> { ["limit"] = 101 })).IsError);
    }
    [Fact]
    public async Task Invalid_typed_arguments_conflicting_retry_and_unavailable_payload_never_execute_again()
    {
        await using var host = await StudioTransportHost.Create();
        await using var mcp = await host.Connect();
        var request = new StudioRunRequest("typed", "novel-review", "cli-default", JsonSerializer.SerializeToElement(new { review_items = new[] { new { title = "One", priority = "Normal" } } }), null);
        var receipt = await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request });
        await host.Terminal(receipt.RunId);
        foreach (var invalid in new[] { request with { Input = JsonSerializer.SerializeToElement("bad") }, request with { Input = JsonSerializer.SerializeToElement(new { review_items = new[] { new { title = "Changed", priority = "Normal" } } }) } })
            Assert.True((await mcp.CallToolAsync("studio_start_run", new Dictionary<string, object?> { ["request"] = invalid })).IsError);
        Assert.True((await mcp.CallToolAsync("studio_start_run", new Dictionary<string, object?> { ["request"] = new { input = "private-value" } })).IsError);
        Assert.True((await mcp.CallToolAsync("studio_get_payload", new Dictionary<string, object?> { ["runId"] = receipt.RunId, ["payloadId"] = "missing" })).IsError);
        Assert.Single((await Call<StudioMcpPage<StudioRunSnapshot>>(mcp, "studio_list_runs")).Items);
        Assert.Empty(host.Model.Requests);
    }
    [Fact]
    public async Task Targeted_span_and_payload_results_match_http_are_redacted_and_have_a_response_byte_limit()
    {
        await using var host = await StudioTransportHost.Create();
        await using var mcp = await host.Connect();
        var request = new StudioRunRequest("captured", "basic-agent", "cli-default", JsonSerializer.SerializeToElement(new { message = "hello" }), null);
        var receipt = await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request });
        await host.Terminal(receipt.RunId);
        var sink = host.App.Services.GetRequiredService<IStudioEventSink>();
        var payload = new StudioPayload("attributes", "application/json", JsonSerializer.SerializeToElement(new { password = "private-value", note = "untrusted captured text" }), StudioCaptureState.Available, null);
        var span = new StudioSpan("trace", "span", "", "test", "call", DateTimeOffset.UtcNow, TimeSpan.Zero, "Ok", null, null, "executor_identity_unavailable", null, null, null, null, payload, payload with { Id = "events" }, payload with { Id = "links" });
        sink.Record(new(receipt.RunId, 0, DateTimeOffset.UtcNow, "span", null, null, "span", JsonSerializer.SerializeToElement(span)));
        var actual = await Call<StudioPayload>(mcp, "studio_get_payload", new() { ["runId"] = receipt.RunId, ["payloadId"] = "attributes" });
        var http = (await host.Http.GetFromJsonAsync<StudioPayload>(Api + $"/runs/{receipt.RunId}/payloads/attributes"))!;
        Assert.Equal(http.Value.GetRawText(), actual.Value.GetRawText());
        Assert.DoesNotContain("private-value", JsonSerializer.Serialize(actual));
        var spans = await Call<StudioMcpSpanPage>(mcp, "studio_get_spans", new() { ["runId"] = receipt.RunId, ["spanId"] = "span" });
        Assert.Equal("span", Assert.Single(spans.Spans).SpanId);
        var oversized = payload with { Id = "large", Value = JsonSerializer.SerializeToElement(new string('x', 150_000)) };
        sink.Record(new(receipt.RunId, 0, DateTimeOffset.UtcNow, "payload", null, null, null, JsonSerializer.SerializeToElement(oversized)));
        var error = await mcp.CallToolAsync("studio_get_payload", new Dictionary<string, object?> { ["runId"] = receipt.RunId, ["payloadId"] = "large" });
        Assert.True(error.IsError);
        Assert.Contains("result_too_large", Assert.IsType<TextContentBlock>(Assert.Single(error.Content)).Text);
    }
    [Fact]
    public async Task New_session_retry_is_one_run_reset_returns_a_fresh_explicit_session_and_busy_reset_is_rejected()
    {
        await using var host = await StudioTransportHost.Create();
        await using var mcp = await host.Connect();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Model.AfterFirstDelta = async token => { reached.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); };
        var request = new StudioRunRequest("new-session", "basic-agent", "cli-default", JsonSerializer.SerializeToElement(new { message = "hello" }), null);
        var receipt = await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request, ["newSession"] = true });
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(receipt, await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request, ["newSession"] = true }));
        var running = await Call<StudioRunSnapshot>(mcp, "studio_get_run", new() { ["runId"] = receipt.RunId });
        Assert.NotNull(running.Request.SessionId);
        Assert.True((await mcp.CallToolAsync("studio_reset_session", new Dictionary<string, object?> { ["sessionId"] = running.Request.SessionId })).IsError);
        await Call<StudioRunReceipt>(mcp, "studio_cancel_run", new() { ["runId"] = receipt.RunId });
        Assert.Equal(StudioRunStatus.Cancelled, (await host.Terminal(receipt.RunId)).Status);
        var replacement = await Call<StudioSession>(mcp, "studio_reset_session", new() { ["sessionId"] = running.Request.SessionId });
        Assert.NotEqual(running.Request.SessionId, replacement.Id);
        Assert.Equal("basic-agent", replacement.EntityId);
        Assert.Equal("cli-default", replacement.ProfileId);
        Assert.Single(host.Model.Requests);
    }
    [Fact]
    public async Task New_session_request_identity_survives_run_eviction_and_conflicts_with_a_prior_sessionless_request()
    {
        await using var host = await StudioTransportHost.Create(maxCompleted: 1);
        await using var mcp = await host.Connect();
        var request = new StudioRunRequest("evict-session", "basic-agent", "cli-default", JsonSerializer.SerializeToElement(new { message = "hello" }), null);
        var receipt = await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request, ["newSession"] = true });
        await host.Terminal(receipt.RunId);
        var otherRequest = request with { RequestId = "other" };
        var other = await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = otherRequest });
        await host.Terminal(other.RunId);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync(Api + "/runs/" + receipt.RunId)).StatusCode);
        var retry = await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request, ["newSession"] = true });
        Assert.Equal(receipt, retry);
        Assert.True((await mcp.CallToolAsync("studio_start_run", new Dictionary<string, object?> { ["request"] = otherRequest, ["newSession"] = true })).IsError);
        foreach (var changed in new[] { request with { ProfileId = "alternate" }, request with { Input = JsonSerializer.SerializeToElement(new { message = "changed" }) } })
            Assert.True((await mcp.CallToolAsync("studio_start_run", new Dictionary<string, object?> { ["request"] = changed, ["newSession"] = true })).IsError);
        // The shared admission service must reject a changed entity even before any adapter preflight.
        await Assert.ThrowsAsync<StudioRequestConflictException>(() => host.App.Services.GetRequiredService<StudioSessionService>()
            .StartNewSessionAsync(request with { EntityId = "another-entity" }, host.Policy.Caller, default).AsTask());
        Assert.Single((await host.Http.GetFromJsonAsync<StudioSession[]>(Api + "/sessions"))!);
        Assert.Equal(2, host.Model.Requests.Count);
    }
    [Fact]
    public async Task Mcp_reset_clears_shared_native_history_and_http_uses_both_explicit_handles()
    {
        await using var host = await StudioTransportHost.Create();
        await using var mcp = await host.Connect();
        var request = new StudioRunRequest("history-first", "basic-agent", "cli-default", JsonSerializer.SerializeToElement(new { message = "hello" }), null);
        var first = await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request, ["newSession"] = true });
        var completed = await host.Terminal(first.RunId);
        string original = completed.Request.SessionId!;
        var again = await host.Http.PostAsJsonAsync(Api + "/runs", request with { RequestId = "history-second", SessionId = original });
        await host.Terminal((await again.Content.ReadFromJsonAsync<StudioRunReceipt>())!.RunId);
        var histories = host.Model.Requests.ToArray();
        Assert.True(histories[1].Length > histories[0].Length);
        var replacement = await Call<StudioSession>(mcp, "studio_reset_session", new() { ["sessionId"] = original });
        foreach (string handle in new[] { original, replacement.Id })
        {
            var submitted = await host.Http.PostAsJsonAsync(Api + "/runs", request with { RequestId = Guid.NewGuid().ToString(), SessionId = handle });
            await host.Terminal((await submitted.Content.ReadFromJsonAsync<StudioRunReceipt>())!.RunId);
            Assert.Equal(histories[0].Length, host.Model.Requests.Last().Length);
        }
    }
    [Fact]
    public async Task Mcp_request_body_is_bounded_before_sdk_binding_including_unknown_content_length()
    {
        await using var host = await StudioTransportHost.Create(maxPayload: 4096);
        string json = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "studio_list_profiles", arguments = new { ignored = new string('x', 9000) } } });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))) };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.TransferEncodingChunked = true;
        using var response = await host.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_request", problem.RootElement.GetProperty("title").GetString());
    }
    [Fact]
    public async Task Rejected_new_session_admission_does_not_consume_a_handle_or_request_identity()
    {
        await using var host = await StudioTransportHost.Create(maxPayload: 4096, maxRequests: 1);
        await using var mcp = await host.Connect();
        var request = new StudioRunRequest("rejected-session", "basic-agent", "cli-default", JsonSerializer.SerializeToElement(new { message = "hello" }), null);
        foreach (var invalid in new[] { request with { Input = JsonSerializer.SerializeToElement("wrong shape") }, request with { Input = JsonSerializer.SerializeToElement(new { message = new string('x', 5000) }) } })
        {
            var result = await mcp.CallToolAsync("studio_start_run", new Dictionary<string, object?> { ["request"] = invalid, ["newSession"] = true });
            Assert.True(result.IsError);
            Assert.Contains("invalid_request", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
            Assert.Empty((await host.Http.GetFromJsonAsync<StudioSession[]>(Api + "/sessions"))!);
        }
        var accepted = await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request, ["newSession"] = true });
        await host.Terminal(accepted.RunId);
        var capacity = await mcp.CallToolAsync("studio_start_run", new Dictionary<string, object?> { ["request"] = request with { RequestId = "capacity-rejected" }, ["newSession"] = true });
        Assert.True(capacity.IsError);
        Assert.Contains("capacity_exceeded", Assert.IsType<TextContentBlock>(Assert.Single(capacity.Content)).Text);
        Assert.Single((await host.Http.GetFromJsonAsync<StudioSession[]>(Api + "/sessions"))!);
        Assert.Equal(accepted, await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request, ["newSession"] = true }));
        Assert.Single(host.Model.Requests);
    }
    [Fact]
    public async Task Reset_at_session_capacity_preserves_committed_native_history()
    {
        await using var host = await StudioTransportHost.Create();
        await using var mcp = await host.Connect();
        var request = new StudioRunRequest("full-first", "basic-agent", "cli-default", JsonSerializer.SerializeToElement(new { message = "remember this turn" }), null);
        var first = await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request, ["newSession"] = true });
        var completed = await host.Terminal(first.RunId);
        var sessions = host.App.Services.GetRequiredService<StudioSessionService>();
        for (int i = 1; i < 1000; i++) await sessions.CreateAsync(new("basic-agent", "cli-default"), default);
        var reset = await mcp.CallToolAsync("studio_reset_session", new Dictionary<string, object?> { ["sessionId"] = completed.Request.SessionId });
        Assert.True(reset.IsError);
        Assert.Contains("capacity_exceeded", Assert.IsType<TextContentBlock>(Assert.Single(reset.Content)).Text);
        using var httpReset = await host.Http.PostAsync(Api + $"/sessions/{completed.Request.SessionId}/reset", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, httpReset.StatusCode);
        var next = await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request with { RequestId = "full-next", SessionId = completed.Request.SessionId, Input = JsonSerializer.SerializeToElement(new { message = "continue" }) } });
        await host.Terminal(next.RunId);
        Assert.Contains(host.Model.Requests.Last(), message => message.Text == "remember this turn");
        Assert.Equal(1000, (await sessions.ListAsync(default)).Count);
    }
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task Paged_spans_preserve_capture_loss_and_cursor_metadata(bool includeValidSpan, bool oversized)
    {
        await using var host = await StudioTransportHost.Create(maxPayload: 4096);
        await using var mcp = await host.Connect();
        var request = new StudioRunRequest("span-loss", "novel-review", "cli-default", JsonSerializer.SerializeToElement(new { review_items = new[] { new { title = "One", priority = "Normal" } } }), null);
        var receipt = await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request });
        await host.Terminal(receipt.RunId);
        var runs = host.App.Services.GetRequiredService<IStudioRunService>();
        long after = (await runs.ReadEventsAsync(receipt.RunId, 0, 1000, default)).NextCursor;
        var sink = host.App.Services.GetRequiredService<IStudioEventSink>();
        var payload = new StudioPayload("attributes", "application/json", JsonSerializer.SerializeToElement(new { }), StudioCaptureState.Available, null);
        var span = new StudioSpan("trace", "retained-span", "", "test", "call", DateTimeOffset.UtcNow, TimeSpan.Zero, "Ok", null, null, "executor_identity_unavailable", null, null, null, null, payload, payload with { Id = "events" }, payload with { Id = "links" });
        if (includeValidSpan)
            sink.Record(new(receipt.RunId, 0, DateTimeOffset.UtcNow, "span", null, null, span.SpanId, JsonSerializer.SerializeToElement(span)));
        var lost = span with { SpanId = "lost-span", Name = oversized ? new string('x', 9000) : "unavailable" };
        sink.Record(new(receipt.RunId, 0, DateTimeOffset.UtcNow, "span", null, null, lost.SpanId, JsonSerializer.SerializeToElement(lost), oversized ? StudioCaptureState.Available : StudioCaptureState.Unavailable));
        var journal = await runs.ReadEventsAsync(receipt.RunId, after, 100, default);
        Assert.Contains(journal.Events, item => item.SpanId == "lost-span" && item.CaptureState == (oversized ? StudioCaptureState.Truncated : StudioCaptureState.Unavailable));
        var page = await Call<StudioMcpSpanPage>(mcp, "studio_get_spans", new() { ["runId"] = receipt.RunId, ["after"] = after });
        Assert.Equal(includeValidSpan ? new[] { "retained-span" } : [], page.Spans.Select(item => item.SpanId));
        Assert.True(page.HasGap);
        Assert.Equal(journal.NextCursor, page.NextCursor);
        Assert.Equal(journal.EarliestRetainedSequence, page.EarliestRetainedSequence);
    }
    [Theory]
    [InlineData("null")]
    [InlineData("\"not a span\"")]
    [InlineData("{\"SpanId\":\"incomplete\"}")]
    [InlineData("{\"TraceId\":\"trace\",\"SpanId\":\"span\",\"ParentSpanId\":\"\",\"Source\":\"test\",\"Name\":\"call\",\"Status\":\"Ok\",\"Attributes\":{},\"Events\":{},\"Links\":{}}")]
    public async Task Paged_spans_report_invalid_retained_dtos_as_unavailable(string json)
    {
        await using var host = await StudioTransportHost.Create();
        await using var mcp = await host.Connect();
        var request = new StudioRunRequest("invalid-span", "novel-review", "cli-default", JsonSerializer.SerializeToElement(new { review_items = new[] { new { title = "One", priority = "Normal" } } }), null);
        var receipt = await Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request });
        await host.Terminal(receipt.RunId);
        var runs = host.App.Services.GetRequiredService<IStudioRunService>();
        long after = (await runs.ReadEventsAsync(receipt.RunId, 0, 1000, default)).NextCursor;
        using var document = JsonDocument.Parse(json);
        host.App.Services.GetRequiredService<IStudioEventSink>().Record(new(receipt.RunId, 0, DateTimeOffset.UtcNow, "span", null, null, "incomplete", document.RootElement.Clone()));
        var page = await Call<StudioMcpSpanPage>(mcp, "studio_get_spans", new() { ["runId"] = receipt.RunId, ["after"] = after });
        Assert.Empty(page.Spans);
        Assert.True(page.HasGap);
        Assert.True(page.NextCursor > after);
    }
    internal static async Task<T> Call<T>(McpClient mcp, string name, Dictionary<string, object?>? arguments = null)
    {
        CallToolResult result = await mcp.CallToolAsync(name, arguments);
        Assert.False(result.IsError == true, JsonSerializer.Serialize(result));
        return JsonSerializer.Deserialize<T>(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, Json)!;
    }
}

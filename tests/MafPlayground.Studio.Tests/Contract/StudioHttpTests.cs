using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MafPlayground.AI;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Contracts;
using MafPlayground.AI.Context;
using MafPlayground.Studio.Tests.Component;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using MafPlayground.Studio.Http;
using MafPlayground.Studio.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Studio.Tests.Contract;

public sealed class StudioHttpTests
{
    private const string Api = "/api/studio/v1";

    [Fact]
    public async Task Bootstrap_requires_same_origin_single_use_form_and_sets_httponly_cookie()
    {
        await using var host = await Fixture.Create();
        host.Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(Api + "/entities")).StatusCode);
        var page = await host.Client.GetAsync("/studio/login");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.DoesNotContain(host.Policy.BootstrapCode, await page.Content.ReadAsStringAsync());
        var form = new Dictionary<string, string> { ["code"] = host.Policy.BootstrapCode };
        var login = await host.Client.PostAsync("/studio/login", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.SeeOther, login.StatusCode);
        string cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsync("/studio/login", new FormUrlEncodedContent(form))).StatusCode);
        host.Client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(Api + "/entities")).StatusCode);
        host.Client.DefaultRequestHeaders.Add("Origin", "http://evil.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync(Api + "/entities")).StatusCode);
    }

    [Fact]
    public async Task Generic_entity_runs_and_retained_queries_use_real_handlers()
    {
        await using var host = await Fixture.Create();
        var entities = await host.Client.GetFromJsonAsync<StudioEntityDescriptor[]>(Api + "/entities");
        Assert.Contains(entities!, entity => entity.Id == "review" && entity.IsAvailable);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(Api + "/entities/review/topology?profileId=cli-default")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(Api + "/profiles")).StatusCode);
        var receipt = await host.Start();
        StudioRunSnapshot run = await host.Terminal(receipt.RunId);
        Assert.Equal(StudioRunStatus.Completed, run.Status);
        Assert.Equal(1, run.Result!.Value.GetProperty("count").GetInt32());
        Assert.Single((await host.Client.GetFromJsonAsync<StudioRunSnapshot[]>(Api + "/runs"))!);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(Api + $"/runs/{run.RunId}/result")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsync(Api + $"/runs/{run.RunId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(Api + "/runs/missing")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(Api + $"/runs/{run.RunId}/payloads/missing")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(Api + $"/runs/{run.RunId}/spans/missing")).StatusCode);
    }

    [Fact]
    public async Task Rejects_invalid_envelopes_profiles_entities_and_limits_with_stable_problems()
    {
        await using var host = await Fixture.Create();
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync(Api + "/runs", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.PostAsJsonAsync(Api + "/runs", Request("unknown"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync(Api + "/runs", Request() with { ProfileId = "token=secret" })).StatusCode);
        // Valid entity shape: one exceeds the payload limit, the other the whole request-body limit.
        foreach (int length in new[] { 5000, 10000 })
        {
            var tooLarge = await host.Client.PostAsJsonAsync(Api + "/runs", Request() with
            { Input = JsonSerializer.SerializeToElement(new { review_items = new[] { new { title = new string('x', length), priority = "Normal" } } }) });
            Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
            Assert.DoesNotContain("xxxx", await tooLarge.Content.ReadAsStringAsync());
        }
        var first = Request();
        var accepted = await host.Client.PostAsJsonAsync(Api + "/runs", first);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync(Api + "/runs", first with { Input = JsonSerializer.SerializeToElement(new { review_items = Array.Empty<object>() }) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync(Api + "/runs/missing/events?after=-1")).StatusCode);
    }

    [Fact]
    public async Task Sse_reconnect_is_ordered_reports_retention_gaps_and_payload_lookup_uses_journal()
    {
        await using var host = await Fixture.Create(maxEvents: 4);
        var receipt = await host.Start();
        await host.Terminal(receipt.RunId);
        var sink = host.App.Services.GetRequiredService<IStudioEventSink>();
        var payload = new StudioPayload("payload-test", "application/json", JsonSerializer.SerializeToElement(new { safe = true }), StudioCaptureState.Available, null);
        for (int i = 0; i < 8; i++) sink.Record(new(receipt.RunId, 0, DateTimeOffset.UtcNow, "tool_result", null, null, null, JsonSerializer.SerializeToElement(payload)));
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(Api + $"/runs/{receipt.RunId}/payloads/payload-test")).StatusCode);
        var request = new HttpRequestMessage(HttpMethod.Get, Api + $"/runs/{receipt.RunId}/events");
        request.Headers.Add("Last-Event-ID", "1");
        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        var ids = new List<long>();
        bool gap = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (ids.Count < 5)
        {
            string? line = await reader.ReadLineAsync(timeout.Token);
            if (line is null) break;
            if (line.StartsWith("id: ", StringComparison.Ordinal)) ids.Add(long.Parse(line[4..]));
            if (line == "event: journal_gap") gap = true;
        }
        Assert.True(gap);
        Assert.Equal(5, ids.Count);
        Assert.True(ids.Zip(ids.Skip(1)).All(pair => pair.First < pair.Second));
    }

    [Fact]
    public async Task Slow_model_run_survives_sse_disconnect_until_explicit_cancel()
    {
        await using var host = await Fixture.Create(samples: true);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Model.AfterFirstDelta = async token => { reached.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); };
        var request = Request("basic-agent") with { Input = JsonSerializer.SerializeToElement(new { message = "hello" }) };
        var accepted = await host.Client.PostAsJsonAsync(Api + "/runs", request);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = (await accepted.Content.ReadFromJsonAsync<StudioRunReceipt>())!;
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using (var response = await host.Client.GetAsync(Api + $"/runs/{receipt.RunId}/events", HttpCompletionOption.ResponseHeadersRead))
        {
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
            Assert.StartsWith("id: ", await reader.ReadLineAsync());
        }
        var snapshot = (await host.Client.GetFromJsonAsync<StudioRunSnapshot>(Api + $"/runs/{receipt.RunId}"))!;
        Assert.False(snapshot.CancellationRequested);
        Assert.False(snapshot.IsTerminal);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.GetAsync(Api + $"/runs/{receipt.RunId}/result")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await host.Client.PostAsync(Api + $"/runs/{receipt.RunId}/cancel", null)).StatusCode);
        Assert.Equal(StudioRunStatus.Cancelled, (await host.Terminal(receipt.RunId)).Status);
    }

    [Fact]
    public async Task Sessions_bind_profiles_restore_history_and_reset_native_state()
    {
        await using var host = await Fixture.Create(samples: true);
        var response = await host.Client.PostAsJsonAsync(Api + "/sessions", new StudioSessionRequest("basic-agent", "cli-default"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = (await response.Content.ReadFromJsonAsync<StudioSession>())!;
        Assert.Single((await host.Client.GetFromJsonAsync<StudioSession[]>(Api + "/sessions"))!);
        var request = Request("basic-agent") with { SessionId = session.Id, Input = JsonSerializer.SerializeToElement(new { message = "first" }) };
        for (int i = 0; i < 2; i++)
        {
            var accepted = await host.Client.PostAsJsonAsync(Api + "/runs", request with { RequestId = Guid.NewGuid().ToString() });
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            var receipt = (await accepted.Content.ReadFromJsonAsync<StudioRunReceipt>())!;
            Assert.Equal(StudioRunStatus.Completed, (await host.Terminal(receipt.RunId)).Status);
        }
        var histories = host.Model.Requests.ToArray();
        Assert.True(histories[1].Length > histories[0].Length);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync(Api + "/runs", request with { RequestId = Guid.NewGuid().ToString(), ProfileId = "alternate" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.PostAsync(Api + $"/sessions/{session.Id}/reset", null)).StatusCode);
        var next = await host.Client.PostAsJsonAsync(Api + "/runs", request with { RequestId = Guid.NewGuid().ToString() });
        var nextReceipt = (await next.Content.ReadFromJsonAsync<StudioRunReceipt>())!;
        await host.Terminal(nextReceipt.RunId);
        Assert.Equal(histories[0].Length, host.Model.Requests.Last().Length);
    }

    [Fact]
    public async Task Capacity_is_429_and_reset_rejects_both_queued_and_active_session_turns()
    {
        await using var host = await Fixture.Create(samples: true, limits: new StudioOptions { MaxConcurrentRuns = 1, QueueCapacity = 1 });
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Model.AfterFirstDelta = async token => { reached.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); };
        var sessions = new List<StudioSession>();
        var receipts = new List<StudioRunReceipt>();
        for (int i = 0; i < 2; i++)
        {
            var created = await host.Client.PostAsJsonAsync(Api + "/sessions", new StudioSessionRequest("basic-agent", "cli-default"));
            var session = (await created.Content.ReadFromJsonAsync<StudioSession>())!;
            sessions.Add(session);
            var accepted = await host.Client.PostAsJsonAsync(Api + "/runs", Request("basic-agent") with { SessionId = session.Id, Input = JsonSerializer.SerializeToElement(new { message = "hello" }) });
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            receipts.Add((await accepted.Content.ReadFromJsonAsync<StudioRunReceipt>())!);
            if (i == 0) await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.Client.PostAsJsonAsync(Api + "/runs", Request("basic-agent") with { Input = JsonSerializer.SerializeToElement(new { message = "hello" }) })).StatusCode);
        foreach (var session in sessions)
            Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsync(Api + $"/sessions/{session.Id}/reset", null)).StatusCode);
        foreach (var receipt in receipts.AsEnumerable().Reverse())
            Assert.Equal(HttpStatusCode.Accepted, (await host.Client.PostAsync(Api + $"/runs/{receipt.RunId}/cancel", null)).StatusCode);
        foreach (var receipt in receipts) await host.Terminal(receipt.RunId);
        foreach (var session in sessions)
            Assert.Equal(HttpStatusCode.NoContent, (await host.Client.PostAsync(Api + $"/sessions/{session.Id}/reset", null)).StatusCode);
    }

    [Fact]
    public async Task Capture_lookups_return_sanitized_spans_and_missing_after_journal_eviction()
    {
        await using var host = await Fixture.Create(maxEvents: 2);
        var receipt = await host.Start();
        await host.Terminal(receipt.RunId);
        var sink = host.App.Services.GetRequiredService<IStudioEventSink>();
        var payload = new StudioPayload("attrs", "application/json", JsonSerializer.SerializeToElement(new { password = "private-value" }), StudioCaptureState.Available, null);
        var span = new StudioSpan("trace", "span", "", "test", "call", DateTimeOffset.UtcNow, TimeSpan.Zero, "Ok", null, null, "executor_identity_unavailable", null, null, null, null, payload, payload with { Id = "events" }, payload with { Id = "links" });
        sink.Record(new(receipt.RunId, 0, DateTimeOffset.UtcNow, "span", null, null, "span", JsonSerializer.SerializeToElement(span)));
        var spanResponse = await host.Client.GetAsync(Api + $"/runs/{receipt.RunId}/spans/span");
        Assert.Equal(HttpStatusCode.OK, spanResponse.StatusCode);
        Assert.DoesNotContain("private-value", await spanResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(Api + $"/runs/{receipt.RunId}/payloads/attrs")).StatusCode);
        for (int i = 0; i < 3; i++) sink.Record(new(receipt.RunId, 0, DateTimeOffset.UtcNow, "test", null, null, null, JsonSerializer.SerializeToElement(new { i })));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(Api + $"/runs/{receipt.RunId}/spans/span")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(Api + $"/runs/{receipt.RunId}/payloads/attrs")).StatusCode);
    }

    [Fact]
    public async Task Unavailable_entities_are_discoverable_and_cannot_be_submitted()
    {
        await using var host = await Fixture.Create(samples: true, noModel: true);
        var entities = (await host.Client.GetFromJsonAsync<StudioEntityDescriptor[]>(Api + "/entities"))!;
        Assert.Equal(4, entities.Count(entity => !entity.IsAvailable));
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync(Api + "/runs", Request("basic-agent") with { Input = JsonSerializer.SerializeToElement(new { message = "hello" }) })).StatusCode);
        Assert.Empty(host.Model.Requests);
    }

    [Fact]
    public async Task Known_configured_secrets_are_redacted_even_in_unlabelled_model_text()
    {
        await using var host = await Fixture.Create(samples: true, configuration: new Dictionary<string, string?> { ["AI:Providers:Fake:ApiKey"] = "classified-value" });
        host.Model.Reply = (_, _) => Task.FromResult("classified-value");
        var accepted = await host.Client.PostAsJsonAsync(Api + "/runs", Request("basic-agent") with { Input = JsonSerializer.SerializeToElement(new { message = "hello" }) });
        var receipt = (await accepted.Content.ReadFromJsonAsync<StudioRunReceipt>())!;
        var run = await host.Terminal(receipt.RunId);
        Assert.Equal(StudioRunStatus.Completed, run.Status);
        Assert.DoesNotContain("classified-value", JsonSerializer.Serialize(run));
        string events = await host.Client.GetStringAsync(Api + $"/runs/{receipt.RunId}/event-page");
        Assert.DoesNotContain("classified-value", events);
    }

    [Fact]
    public async Task Bootstrap_form_has_a_small_body_and_value_budget()
    {
        await using var host = await Fixture.Create();
        var response = await host.Client.PostAsync("/studio/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = new string('x', 5000) }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sse_completes_when_the_host_stops_with_a_connected_watcher()
    {
        await using var host = await Fixture.Create();
        var receipt = await host.Start();
        await host.Terminal(receipt.RunId);
        using var response = await host.Client.GetAsync(Api + $"/runs/{receipt.RunId}/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        Assert.StartsWith("id: ", await reader.ReadLineAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await host.App.StopAsync(timeout.Token);
        _ = await reader.ReadToEndAsync(timeout.Token);
    }

    [Theory]
    [InlineData("missing", 401, "authentication_required")]
    [InlineData("invalid", 401, "authentication_required")]
    [InlineData("foreign-host", 403, "local_access_required")]
    [InlineData("foreign-origin", 403, "local_access_required")]
    [InlineData("bearer", 400, "invalid_request")]
    [InlineData("cookie", 400, "invalid_request")]
    public async Task Development_access_boundary_precedes_missing_and_malformed_query_binding(string mode, int status, string title)
    {
        await using var host = await Fixture.Create();
        host.Client.DefaultRequestHeaders.Authorization = null;
        var secrets = new List<string> { host.Policy.BearerToken, "invalid-private-credential", "private-query-value" };
        if (mode == "cookie")
        {
            var login = await host.Client.PostAsync("/studio/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = host.Policy.BootstrapCode }));
            string cookie = Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';')[0];
            secrets.Add(cookie);
            secrets.Add(cookie[(cookie.IndexOf('=') + 1)..]);
            host.Client.DefaultRequestHeaders.Add("Cookie", cookie);
        }
        else if (mode != "missing") host.Client.DefaultRequestHeaders.Authorization = new("Bearer", mode == "invalid" ? "invalid-private-credential" : host.Policy.BearerToken);
        if (mode == "foreign-host") host.Client.DefaultRequestHeaders.Host = "foreign.test:5051";
        if (mode == "foreign-origin") host.Client.DefaultRequestHeaders.Add("Origin", "http://foreign.test:5051");
        foreach (string path in new[] { Api + "/entities/review/topology", Api + "/binding-probe?count=private-query-value" })
        {
            using var response = await host.Client.GetAsync(path);
            await AssertSafeProblem(response, status, title, secrets);
        }
    }

    [Theory]
    [InlineData(false, 401)]
    [InlineData(true, 500)]
    public async Task Development_access_boundary_precedes_endpoint_service_activation(bool authenticated, int status)
    {
        await using var host = await Fixture.Create();
        if (!authenticated) host.Client.DefaultRequestHeaders.Authorization = null;
        using var response = await host.Client.GetAsync(Api + "/dependency-probe");
        await AssertSafeProblem(response, status, authenticated ? "operation_failed" : "authentication_required", [host.Policy.BearerToken, "dependency-private-detail"]);
        Assert.Equal(authenticated ? 1 : 0, host.App.Services.GetRequiredService<ActivationProbe>().Count);
    }

    private static async Task AssertSafeProblem(HttpResponseMessage response, int status, string title, IEnumerable<string> secrets)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync();
        // Never include response content or credentials in assertion failure output.
        Assert.True(secrets.All(secret => !body.Contains(secret, StringComparison.Ordinal)), "Problem response leaked a credential or private diagnostic.");
        Assert.False(body.Contains("Exception", StringComparison.Ordinal) || body.Contains("StackTrace", StringComparison.Ordinal) || body.Contains("HEADERS", StringComparison.Ordinal) || body.Contains("Microsoft.AspNetCore", StringComparison.Ordinal));
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("correlationId").GetString()));
        Assert.False(problem.RootElement.TryGetProperty("detail", out _));
    }

    private sealed class ActivationProbe { public int Count; }
    private sealed class FailingDependency;

    internal static StudioRunRequest Request(string entity = "review") => new(Guid.NewGuid().ToString("N"), entity, "cli-default", JsonSerializer.SerializeToElement(new { review_items = new[] { new { title = "one", priority = "Normal" } } }), null);

    private sealed class EmptyUser : IUserContextAccessor
    { public UserContext GetCurrent() => new([]); }

    private sealed class Fixture(WebApplication app, HttpClient client, StudioAccessPolicy policy, StudioExecutionFixture.RecordingClient model) : IAsyncDisposable
    {
        internal StudioExecutionFixture.RecordingClient Model => model;
        internal WebApplication App => app;
        internal HttpClient Client => client;
        internal StudioAccessPolicy Policy => policy;
        internal static async Task<Fixture> Create(int maxEvents = 2048, bool samples = false, StudioOptions? limits = null, bool noModel = false, Dictionary<string, string?>? configuration = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.WebHost.UseTestServer();
            if (configuration is not null) builder.Configuration.AddInMemoryCollection(configuration);
            var model = new StudioExecutionFixture.RecordingClient();
            if (samples && !noModel) builder.Configuration["AI:Defaults:Model"] = "fake:model";
            builder.Services.AddSingleton<IChatClientProvider>(new StudioExecutionFixture.ClientProvider(model));
            builder.Services.AddSingleton<IUserContextAccessor, EmptyUser>();
            builder.Services.AddInMemoryAgentSessions();
            builder.Services.AddSingleton(new StudioExecutionProfile("alternate"));
            var entities = builder.Services.AddAIRuntime(builder.Configuration, AIStartupValidationMode.LocalInspection)
                .AddAgent<ReviewEntity, ReviewRequest, ReviewResult>("review", registration => registration.WithDescription("Review generic input").AllowAnonymousAccess());
            if (samples) entities.AddMafPlaygroundSampleEntities();
            builder.Configuration["Studio:Limits:MaxPayloadBytes"] = (limits?.MaxPayloadBytes ?? 4096).ToString();
            builder.Configuration["Studio:Limits:MaxEventsPerRun"] = maxEvents.ToString();
            if (limits is not null)
            {
                builder.Configuration["Studio:Limits:MaxConcurrentRuns"] = limits.MaxConcurrentRuns.ToString();
                builder.Configuration["Studio:Limits:QueueCapacity"] = limits.QueueCapacity.ToString();
            }
            builder.Services.AddStudio(builder.Configuration);
            builder.Services.AddSingleton<ActivationProbe>();
            builder.Services.AddScoped<FailingDependency>(provider =>
            {
                Interlocked.Increment(ref provider.GetRequiredService<ActivationProbe>().Count);
                throw new InvalidOperationException("dependency-private-detail");
            });
            var app = builder.Build();
            app.Use(async (context, next) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; await next(); });
            app.UseStudioAccess();
            app.MapStudio();
            app.MapGet(Api + "/binding-probe", (int count) => count);
            app.MapGet(Api + "/dependency-probe", (FailingDependency dependency) => "unreachable");
            await app.StartAsync();
            var client = app.GetTestClient();
            client.BaseAddress = new("http://localhost:5051");
            var policy = app.Services.GetRequiredService<StudioAccessPolicy>();
            client.DefaultRequestHeaders.Authorization = new("Bearer", policy.BearerToken);
            return new(app, client, policy, model);
        }
        internal async Task<StudioRunReceipt> Start()
        {
            var response = await client.PostAsJsonAsync(Api + "/runs", Request());
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<StudioRunReceipt>())!;
        }
        internal async Task<StudioRunSnapshot> Terminal(string id)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                var run = (await client.GetFromJsonAsync<StudioRunSnapshot>(Api + "/runs/" + id, timeout.Token))!;
                if (run.IsTerminal) return run;
                await Task.Delay(10, timeout.Token);
            }
        }
        public async ValueTask DisposeAsync() { client.Dispose(); await app.StopAsync(); await app.DisposeAsync(); }
    }
}

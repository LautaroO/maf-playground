using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using MafPlayground.AI.Runtime.Providers;
using MafPlayground.AI.Runtime.Execution;
using MafPlayground.Studio.Capture;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;

namespace MafPlayground.Studio.Tests.Component;

public sealed class StudioCorrelationTests
{
    [Fact]
    public async Task Native_executor_guard_failure_records_failed_exit_without_model_work()
    {
        await using StudioExecutionFixture fixture = new(services => services.Configure<MafPlayground.AI.Guards.AIGuardOptions>(options =>
            options.Profiles[MafPlayground.AI.Guards.GuardProfileNames.Default].Content.MaxInputCharacters = 1));
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.RunAsync(new { text = "Too long for guard", targetLanguages = new[] { "es" } }, "translation-workflow"));
        var started = Assert.Single(fixture.Events.Items, x => x.Kind == "executor_started" && x.ExecutorId == "translation-input");
        Assert.Contains(fixture.Events.Items, x => x.Kind == "executor_failed" && x.InvocationId == started.InvocationId);
        Assert.DoesNotContain(fixture.Events.Items, x => x.Kind == "executor_completed" && x.InvocationId == started.InvocationId);
        Assert.Empty(fixture.Client.Requests);
    }

    [Fact]
    public async Task Native_workflow_cancellation_records_only_observed_invocation_termination()
    {
        await using StudioExecutionFixture fixture = new();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.Reply = async (_, token) => { reached.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return "unreachable"; };
        using var cancellation = new CancellationTokenSource();
        var execution = fixture.RunAsync(new { text = "blocked", targetLanguages = new[] { "es" } }, "translation-workflow", token: cancellation.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(fixture.Events.Items, x => x.Kind == "executor_started" && x.ExecutorId == "translate-es");
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        var started = Assert.Single(fixture.Events.Items, x => x.Kind == "executor_started" && x.ExecutorId == "translate-es");
        Assert.True(fixture.Events.Items.Any(x => x.Kind == "executor_cancelled" && x.InvocationId == started.InvocationId), JsonSerializer.Serialize(fixture.Events.Items.Where(x => x.InvocationId == started.InvocationId && (x.Kind.StartsWith("executor_", StringComparison.Ordinal) || x.Kind == "span")).Select(x => new { x.Kind, x.Data })));
        Assert.DoesNotContain(fixture.Events.Items, x => x.Kind == "executor_cancelled" && x.ExecutorId == "validate-es");
    }

    [Fact]
    public async Task RepeatedAndParallelInvocations_PreserveIdentity_AndLateUncorrelatedSpans()
    {
        StudioExecutionFixture.EventCollector sink = new();
        var payloads = new StudioPayloadStore(new());
        using var listener = new StudioActivityListener(sink, payloads, TimeProvider.System);
        StudioExecutionFixture.EventCollector otherHost = new();
        using var otherListener = new StudioActivityListener(otherHost, payloads, TimeProvider.System);
        using var source = new ActivitySource("MafPlayground.AI.Runtime");
        using var parent = new Activity("upstream").Start();
        Activity late;
        using (var scope = new StudioCaptureScope(new("run", "entity", "profile", null, new("local")), sink, payloads, TimeProvider.System))
        {
            for (int i = 0; i < 2; i++)
            {
                using var invocation = AIExecutionObservation.BeginInvocation("repeat", new { attempt = i });
                using var model = source.StartActivity("model")!;
                model.SetTag("gen_ai.usage.input_tokens", 7L);
                model.AddTag("provider.detail", "first");
                model.AddTag("provider.detail", "last");
                model.SetTag("maf_playground.gen_ai.cost", 0.01);
                invocation.Output(new { validated = true });
            }
            await Task.WhenAll(new[] { "es", "fr" }.Select(language => Task.Run(async () =>
            {
                using var invocation = AIExecutionObservation.BeginInvocation(language, language);
                await Task.Yield();
                using var model = source.StartActivity("model")!;
                invocation.Send(new { language }, "aggregate");
            })));
            late = source.StartActivity("unrelated")!;
        }
        late.Dispose();
        var spans = sink.Items.Where(item => item.Kind == "span").Select(item => item.Data.Deserialize<StudioSpan>()!).ToArray();
        Assert.Equal(5, spans.Length);
        Assert.Empty(otherHost.Items);
        Assert.Equal(2, spans.Count(span => span.ExecutorId == "repeat"));
        Assert.Equal(4, spans.Where(span => span.InvocationId is not null).Select(span => span.InvocationId).Distinct().Count());
        Assert.Contains(spans, span => span.ExecutorId == "es");
        Assert.Contains(spans, span => span.ExecutorId == "fr");
        var unknown = Assert.Single(spans, span => span.ExecutorId is null);
        Assert.Equal(late.TraceId.ToString(), unknown.TraceId);
        Assert.Equal(late.SpanId.ToString(), unknown.SpanId);
        Assert.Equal(late.ParentSpanId.ToString(), unknown.ParentSpanId);
        Assert.NotEqual(default(ActivitySpanId).ToString(), unknown.ParentSpanId);
        Assert.Null(unknown.InputTokens);
        Assert.Null(unknown.Cost);
        Assert.Equal("executor_identity_unavailable", unknown.CorrelationUnavailableReason);
        Assert.All(spans.Where(span => span.ExecutorId == "repeat"), span => { Assert.Equal(7, span.InputTokens); Assert.Equal(.01, span.Cost); });
        Assert.DoesNotContain(sink.Items, item => item.Kind == "edge_traversed");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CaptureDoesNotEnableExternalSensitiveTelemetry(bool enabled)
    {
        var exported = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => exported.Enqueue(JsonSerializer.Serialize(new { tags = activity.TagObjects, events = activity.Events.Select(item => item.Tags) })),
        };
        ActivitySource.AddActivityListener(listener);
        await using StudioExecutionFixture fixture = new(services =>
        {
            Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<StudioOptions>(services);
            Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, new StudioOptions { EnableCapture = enabled });
        });
        await fixture.RunAsync(new { message = "private-prompt-value" });
        Assert.Single(fixture.Client.Requests);
        Assert.DoesNotContain("private-prompt-value", string.Join("", exported));
        if (enabled) Assert.Contains(fixture.Events.Items, item => item.Kind == "model_request" && item.Data.ToString().Contains("private-prompt-value", StringComparison.Ordinal));
        else Assert.DoesNotContain(fixture.Events.Items, item => item.Kind == "model_request");
    }

    [Fact]
    public async Task ObserverFailures_AreContainedAcrossAsyncInvocationBoundaries()
    {
        var payloads = new StudioPayloadStore(new());
        using var scope = new StudioCaptureScope(new("run", "entity", "profile", null, new("local")), new ThrowingSink(), payloads, TimeProvider.System);
        AIExecutionObservation.Record("broken", new object());
        using var nested = AIExecutionObservation.Observe(_ => throw new InvalidOperationException("observer failed"));
        AIExecutionObservation.Record("payload", "value");
        await Task.Yield();
        using var invocation = AIExecutionObservation.BeginInvocation("node", "input");
        invocation.Output("output");
    }
    private sealed class ThrowingSink : IStudioEventSink { public void Record(StudioEvent item) => throw new InvalidOperationException("sink failed"); }

    [Fact]
    public async Task Translation_RecordsRealBranchInputsOutputsAndValidation_WithNativeSpanIdentity()
    {
        await using StudioExecutionFixture fixture = new();
        fixture.Client.Reply = (messages, _) => Task.FromResult(messages.Last().Text.Contains("translatedText", StringComparison.OrdinalIgnoreCase)
            ? """{"isValid":true,"confidence":0.95,"issues":[]}"""
            : """{"translatedText":"Hola"}""");
        await fixture.RunAsync(new { text = "Hello", targetLanguages = new[] { "es", "fr" } }, "translation-workflow");
        Assert.Equal(4, fixture.Client.Requests.Count);
        var started = fixture.Events.Items.Where(item => item.Kind == "executor_started").ToArray();
        Assert.NotEmpty(started);
        Assert.All(started, start => Assert.Contains(fixture.Events.Items, end => end.Kind == "executor_completed" && end.InvocationId == start.InvocationId && end.ExecutorId == start.ExecutorId));
        Assert.Contains(fixture.Events.Items, item => item.Kind == "executor_input" && item.Data.ToString().Contains("Hello", StringComparison.Ordinal));
        Assert.Contains(fixture.Events.Items, item => item.Kind == "validation_feedback" && item.Data.ToString().Contains("Hola", StringComparison.Ordinal));
        Assert.Contains(fixture.Events.Items, item => item.Kind == "executor_output");
        Assert.Contains(fixture.Events.Items, item => item.Kind == "workflow_payload");
        var inputs = fixture.Events.Items.Where(item => item.Kind == "executor_input").ToArray();
        Assert.Equal(inputs.Length, inputs.Select(item => item.InvocationId).Distinct().Count());
        var spans = fixture.Events.Items.Where(item => item.Kind == "span").Select(item => item.Data.Deserialize<StudioSpan>()!).ToArray();
        Assert.Contains(spans, span => span.Source == "MafPlayground.AI.Runtime");
        Assert.All(inputs, input => Assert.Contains(spans, span => span.SpanId == input.SpanId && span.InvocationId == input.InvocationId));
        Assert.True(spans.Any(span => span.Source == "Microsoft.Agents.AI.Workflows" && span.InvocationId != null), JsonSerializer.Serialize(spans));
        Assert.All(spans.Where(span => span.InvocationId != null), span => Assert.Contains(inputs, input => input.InvocationId == span.InvocationId && input.ExecutorId == span.ExecutorId));
    }

    [Fact]
    public async Task SuppliedModelUsage_IsCapturedAlongsideTheSingleResponse()
    {
        await using StudioExecutionFixture fixture = new();
        fixture.Client.Respond = (_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer"))
        { Usage = new UsageDetails { InputTokenCount = 17, OutputTokenCount = 31 } });
        _ = fixture.Provider.GetRequiredService<StudioActivityListener>();
        using var capture = new StudioCaptureScope(new("usage", "basic-agent", "cli-default", null, new("local")), fixture.Events,
            fixture.Provider.GetRequiredService<StudioPayloadStore>(), TimeProvider.System);
        using var client = fixture.Provider.GetRequiredService<IAIChatClientFactory>().Create("basic-agent");
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "question")]);
        Assert.Equal("answer", response.Text);
        Assert.Single(fixture.Client.Requests);
        Assert.Contains(fixture.Events.Items, item => item.Kind == "model_response" && item.Data.ToString().Contains("answer", StringComparison.Ordinal));
        var span = Assert.Single(fixture.Events.Items, item => item.Kind == "span").Data.Deserialize<StudioSpan>()!;
        Assert.Equal(17, span.InputTokens);
        Assert.Equal(31, span.OutputTokens);
        Assert.Null(span.Cost);
    }

    [Fact]
    public async Task StreamingModelAndToolExchange_IsCapturedWithoutDuplicateCalls()
    {
        await using StudioExecutionFixture fixture = new();
        fixture.Client.Respond = (messages, _, _) => Task.FromResult(messages.Any(message => message.Contents.OfType<Microsoft.Extensions.AI.FunctionResultContent>().Any())
            ? new Microsoft.Extensions.AI.ChatResponse(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "finished"))
            : new Microsoft.Extensions.AI.ChatResponse(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant,
                [new Microsoft.Extensions.AI.FunctionCallContent("call-1", "get_current_date_time", new Dictionary<string, object?> { ["timeZoneId"] = "UTC" })])));
        await fixture.RunAsync(new { message = "context" });
        Assert.Equal(2, fixture.Client.Requests.Count);
        Assert.Contains(fixture.Events.Items, item => item.Kind == "model_request");
        Assert.Contains(fixture.Events.Items, item => item.Kind == "model_update");
        Assert.Contains(fixture.Events.Items, item => item.Kind == "model_completed");
        Assert.Contains(fixture.Events.Items, item => item.Kind == "tool_arguments");
        Assert.Contains(fixture.Events.Items, item => item.Kind == "tool_result" && item.Data.ToString().Contains("UTC", StringComparison.Ordinal));
    }
}

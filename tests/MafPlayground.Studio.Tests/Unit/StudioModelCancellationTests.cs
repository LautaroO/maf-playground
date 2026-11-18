using System.Text.Json;
using MafPlayground.AI.Runtime.Providers;
using MafPlayground.Studio.Capture;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Tests.Component;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Studio.Tests.Unit;

public sealed class StudioModelCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_is_recorded_without_a_failed_model_span(bool streaming)
    {
        await using StudioExecutionFixture fixture = new();
        TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.Reply = async (_, token) =>
        {
            reached.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return "unreachable";
        };
        _ = fixture.Provider.GetRequiredService<StudioActivityListener>();
        using var capture = new StudioCaptureScope(new("cancelled", "basic-agent", "cli-default", null, new("local")), fixture.Events,
            fixture.Provider.GetRequiredService<StudioPayloadStore>(), TimeProvider.System);
        using var client = fixture.Provider.GetRequiredService<IAIChatClientFactory>().Create("basic-agent");
        using CancellationTokenSource cancellation = new();

        Task execution = InvokeAsync(client, streaming, cancellation.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);

        StudioEvent cancelled = Assert.Single(fixture.Events.Items, item => item.Kind == "model_cancelled");
        Assert.Equal("caller_cancelled", cancelled.Data.GetProperty("Value").GetProperty("reason").GetString());
        Assert.DoesNotContain(fixture.Events.Items, item => item.Kind == "model_failed");
        StudioSpan model = Assert.Single(fixture.Events.Items.Where(item => item.Kind == "span")
            .Select(item => item.Data.Deserialize<StudioSpan>()!), span => span.Source == "MafPlayground.Studio.Model");
        Assert.NotEqual("Error", model.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unsolicited_operation_cancellation_remains_a_model_failure(bool streaming)
    {
        await using StudioExecutionFixture fixture = new();
        fixture.Client.Reply = (_, _) => throw new OperationCanceledException("Unsolicited provider cancellation.");
        _ = fixture.Provider.GetRequiredService<StudioActivityListener>();
        using var capture = new StudioCaptureScope(new("failure", "basic-agent", "cli-default", null, new("local")), fixture.Events,
            fixture.Provider.GetRequiredService<StudioPayloadStore>(), TimeProvider.System);
        using var client = fixture.Provider.GetRequiredService<IAIChatClientFactory>().Create("basic-agent");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InvokeAsync(client, streaming, CancellationToken.None));

        Assert.Single(fixture.Events.Items, item => item.Kind == "model_failed");
        Assert.DoesNotContain(fixture.Events.Items, item => item.Kind == "model_cancelled");
        StudioSpan model = Assert.Single(fixture.Events.Items.Where(item => item.Kind == "span")
            .Select(item => item.Data.Deserialize<StudioSpan>()!), span => span.Source == "MafPlayground.Studio.Model");
        Assert.Equal("Error", model.Status);
    }

    private static async Task InvokeAsync(IChatClient client, bool streaming, CancellationToken cancellationToken)
    {
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        if (streaming)
        {
            await foreach (var _ in client.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken)) { }
        }
        else
        {
            await client.GetResponseAsync(messages, cancellationToken: cancellationToken);
        }
    }
}

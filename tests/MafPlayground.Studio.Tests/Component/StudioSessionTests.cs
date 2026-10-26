using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI.Runtime.Sessions;
using MafPlayground.AI.Abstractions;
using MafPlayground.Studio.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Studio.Tests.Component;

public sealed class StudioSessionTests
{
    [Fact]
    public async Task SessionContinuation_RejectsDifferentProfileBeforeModelCall()
    {
        await using StudioExecutionFixture fixture = new(services => services.AddSingleton(new StudioExecutionProfile("other")));
        await fixture.RunAsync(new BasicAgentRequest("first"), session: "conversation");
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.RunAsync(new BasicAgentRequest("second"), profile: "other", session: "conversation"));
        Assert.Single(fixture.Client.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialStreamFailureOrCancellation_DoesNotCommitTheTurn(bool cancel)
    {
        await using StudioExecutionFixture fixture = new();
        await fixture.RunAsync(new BasicAgentRequest("first"), session: "conversation");
        using CancellationTokenSource cancellation = new();
        fixture.Client.AfterFirstDelta = token =>
        {
            if (cancel) { cancellation.Cancel(); return Task.FromCanceled(token); }
            throw new InvalidOperationException("controlled stream failure");
        };
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.RunAsync(new BasicAgentRequest("partial"), session: "conversation", token: cancellation.Token));
        fixture.Client.AfterFirstDelta = null;
        await fixture.RunAsync(new BasicAgentRequest("last"), session: "conversation");
        Assert.DoesNotContain(fixture.Client.Requests.Last(), message => message.Text == "partial");
        Assert.Contains(fixture.Client.Requests.Last(), message => message.Text == "first");
    }

    [Fact]
    public async Task Reset_ClearsCommittedConversation()
    {
        await using StudioExecutionFixture fixture = new();
        await fixture.RunAsync(new BasicAgentRequest("first"), session: "conversation");
        await fixture.Provider.GetRequiredService<IAgentSessionStore>().ResetAsync(new("basic-agent", null, "tester", "conversation", "cli-default"), default);
        await fixture.RunAsync(new BasicAgentRequest("second"), session: "conversation");
        Assert.DoesNotContain(fixture.Client.Requests.Last(), message => message.Text == "first");
    }

    [Fact]
    public async Task Overlap_IsRejectedWhileCancellationDoesNotCommitTurn()
    {
        await using StudioExecutionFixture fixture = new();
        await fixture.RunAsync(new BasicAgentRequest("first"), session: "conversation");
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.Reply = async (_, token) => { started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return "never"; };
        using CancellationTokenSource cancellation = new();
        Task pending = fixture.RunAsync(new BasicAgentRequest("cancelled"), session: "conversation", token: cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<AISessionConcurrencyException>(() => fixture.RunAsync(new BasicAgentRequest("overlap"), session: "conversation"));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        fixture.Client.Reply = (_, _) => Task.FromResult("done");
        await fixture.RunAsync(new BasicAgentRequest("last"), session: "conversation");
        Assert.DoesNotContain(fixture.Client.Requests.Last(), message => message.Text is "cancelled" or "overlap");
    }

    [Fact]
    public async Task FailedTurn_DoesNotCommitHistory()
    {
        await using StudioExecutionFixture fixture = new();
        await fixture.RunAsync(new BasicAgentRequest("first"), session: "conversation");
        fixture.Client.Reply = (_, _) => throw new InvalidOperationException("failed turn");
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.RunAsync(new BasicAgentRequest("failed"), session: "conversation"));
        fixture.Client.Reply = (_, _) => Task.FromResult("done");
        await fixture.RunAsync(new BasicAgentRequest("third"), session: "conversation");
        var history = fixture.Client.Requests.Last();
        Assert.Contains(history, message => message.Text == "first");
        Assert.DoesNotContain(history, message => message.Text == "failed");
    }
}

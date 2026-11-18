using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MafPlayground.CLI.Tui;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Tests;

public sealed class StudioClientTests
{
    [Fact]
    public void ProductionHandlerDisablesRedirectsAndCookies()
    {
        ServiceCollection services = new();
        StudioClient.AddHttpClient(services);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        HttpMessageHandler handler = factory.CreateHandler(nameof(StudioClient));
        while (handler is DelegatingHandler pipeline) handler = pipeline.InnerHandler!;
        var sockets = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(sockets.AllowAutoRedirect);
        Assert.False(sockets.UseCookies);
    }

    [Theory]
    [InlineData("http://studio.example")]
    [InlineData("http://localhost.example")]
    [InlineData("https://user:pass@studio.example")]
    [InlineData("https://studio.example?token=value")]
    [InlineData("https://studio.example#fragment")]
    [InlineData("file:///tmp/studio")]
    public void RejectsUnsafeHosts(string host)
    {
        Assert.Throws<ArgumentException>(() => new StudioClient(new Uri(host), null!));
    }

    [Theory]
    [InlineData("https://studio.example/tools/")]
    [InlineData("http://localhost:5053/")]
    [InlineData("http://127.0.0.1:5053/")]
    [InlineData("http://[::1]:5053/")]
    public async Task SendsBearerAndEscapedQueryWithinHostPrefix(string host)
    {
        using var client = new StudioClient(new Uri(host), new Handler((request, _) =>
        {
            Assert.Equal("Bearer test-token", request.Headers.Authorization?.ToString());
            Assert.Equal(host + "api/studio/v1/topology?entityId=a%26b&profileId=a%20b", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { nodes = new[] { "executor" } }) });
        }), "test-token");
        var topology = await client.TopologyAsync("a&b", "a b", default);
        Assert.Equal("executor", topology.GetProperty("nodes")[0].GetString());
    }

    [Fact]
    public async Task SubmitsStructuredInputAndReturnsReceipt()
    {
        using var client = new StudioClient(new Uri("http://localhost"), new Handler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/studio/v1/runs", request.RequestUri?.AbsolutePath);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal("request", body.GetProperty("requestId").GetString());
            Assert.Equal("agent", body.GetProperty("entityId").GetString());
            Assert.Equal("default", body.GetProperty("profileId").GetString());
            Assert.Equal("session", body.GetProperty("sessionId").GetString());
            Assert.Equal("hello", body.GetProperty("input").GetProperty("message").GetString());
            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = JsonContent.Create(new { runId = "r1" }) };
        }));
        var receipt = await client.StartRunAsync("request", "agent", "default", JsonSerializer.SerializeToElement(new { message = "hello" }), "session", default);
        Assert.Equal("r1", receipt.GetProperty("runId").GetString());
    }

    [Fact]
    public async Task StreamsFromCursorAndParsesFramedMultilineData()
    {
        using var client = new StudioClient(new Uri("http://localhost"), new Handler((request, _) =>
        {
            Assert.Equal("/api/studio/v1/runs/r1/events?after=7", request.RequestUri?.PathAndQuery);
            Assert.Contains(request.Headers.Accept, h => h.MediaType == "text/event-stream");
            string stream = ": heartbeat\r\n\r\nid: 8\r\nevent: captured\r\ndata: {\r\ndata: \"sequence\":8}\r\n\r\ndata: {\"sequence\":9}\n\n";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(stream, Encoding.UTF8, "text/event-stream") });
        }));
        var sequence = new List<int>();
        await foreach (var item in client.WatchAsync("r1", 7, default))
            sequence.Add(item.GetProperty("sequence").GetInt32());
        Assert.Equal([8, 9], sequence);
    }

    [Fact]
    public async Task CancelAcceptsEmptyResponseAndSurfacesConflict()
    {
        int attempts = 0;
        using var client = new StudioClient(new Uri("http://localhost"), new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/studio/v1/runs/r1/cancel", request.RequestUri?.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(attempts++ == 0 ? HttpStatusCode.Accepted : HttpStatusCode.Conflict));
        }));
        await client.CancelAsync("r1", default);
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => client.CancelAsync("r1", default));
        Assert.Equal(HttpStatusCode.Conflict, failure.StatusCode);
    }

    [Theory]
    [InlineData("authentication_required", true)]
    [InlineData("untrusted server response", false)]
    public async Task HttpErrorsExposeOnlyKnownProblemCodes(string title, bool expected)
    {
        using var client = new StudioClient(new Uri("http://localhost"), new Handler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { title, detail = "private body" }), Encoding.UTF8, "application/problem+json")
            })));
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => client.InstanceAsync(default));
        Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        Assert.Equal(expected, failure.Message.Contains(title, StringComparison.Ordinal));
        Assert.DoesNotContain("private body", failure.Message);
    }

    [Fact]
    public async Task CancellationInterruptsPendingRequest()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new StudioClient(new Uri("http://localhost"), new Handler(async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var stop = new CancellationTokenSource();
        Task request = client.RunsAsync(stop.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(this);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}

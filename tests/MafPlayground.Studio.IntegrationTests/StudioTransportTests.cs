using System.Net.Http.Json;
using System.Text.Json;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Http;
using MafPlayground.Studio.Tests.Fixtures;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MafPlayground.Studio.IntegrationTests;

public sealed class StudioTransportTests
{
    [StudioTransportFact]
    [Trait("Category", "StudioTransport")]
    public async Task Loopback_official_mcp_and_http_submit_and_read_the_same_run()
    {
        await using var host = await StudioTransportHost.Create(loopback: true);
        await using var mcp = await host.Connect();
        var request = new StudioRunRequest("loopback-request", "basic-agent", "cli-default", JsonSerializer.SerializeToElement(new { message = "hello" }), null);
        var accepted = await host.Http.PostAsJsonAsync(StudioEndpoints.Prefix + "/runs", request);
        var receipt = (await accepted.Content.ReadFromJsonAsync<StudioRunReceipt>())!;
        var duplicate = await mcp.CallToolAsync("studio_start_run", new Dictionary<string, object?> { ["request"] = request });
        Assert.False(duplicate.IsError == true);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var mcpReceipt = JsonSerializer.Deserialize<StudioRunReceipt>(Assert.IsType<TextContentBlock>(Assert.Single(duplicate.Content)).Text, json)!;
        Assert.Equal(receipt, mcpReceipt);
        var final = await host.Terminal(receipt.RunId);
        var read = await mcp.CallToolAsync("studio_get_run", new Dictionary<string, object?> { ["runId"] = receipt.RunId });
        Assert.False(read.IsError == true);
        var snapshot = JsonSerializer.Deserialize<StudioRunSnapshot>(Assert.IsType<TextContentBlock>(Assert.Single(read.Content)).Text, json)!;
        Assert.Equal(final.RunId, snapshot.RunId);
        Assert.Equal(final.Result!.Value.GetRawText(), snapshot.Result!.Value.GetRawText());
        Assert.Single(host.Model.Requests);
    }
    [StudioTransportFact]
    [Trait("Category", "StudioTransport")]
    public async Task Loopback_sse_viewer_reconnects_to_the_same_accepted_job()
    {
        await using var host = await StudioTransportHost.Create(loopback: true);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Model.AfterFirstDelta = async token => { reached.TrySetResult(); await release.Task.WaitAsync(token); };
        var request = new StudioRunRequest("sse-request", "basic-agent", "cli-default", JsonSerializer.SerializeToElement(new { message = "hello" }), null);
        var accepted = await host.Http.PostAsJsonAsync(StudioEndpoints.Prefix + "/runs", request);
        var receipt = (await accepted.Content.ReadFromJsonAsync<StudioRunReceipt>())!;
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        long cursor;
        using (var response = await host.Http.GetAsync(StudioEndpoints.Prefix + $"/runs/{receipt.RunId}/events", HttpCompletionOption.ResponseHeadersRead))
        {
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string first = (await reader.ReadLineAsync(timeout.Token))!;
            Assert.StartsWith("id: ", first);
            cursor = long.Parse(first[4..], System.Globalization.CultureInfo.InvariantCulture);
        }
        release.TrySetResult();
        var final = await host.Terminal(receipt.RunId);
        using var reconnect = new HttpRequestMessage(HttpMethod.Get, StudioEndpoints.Prefix + $"/runs/{receipt.RunId}/events");
        reconnect.Headers.Add("Last-Event-ID", cursor.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var resumed = await host.Http.SendAsync(reconnect, HttpCompletionOption.ResponseHeadersRead);
        using var resumedReader = new StreamReader(await resumed.Content.ReadAsStreamAsync());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string next = (await resumedReader.ReadLineAsync(deadline.Token))!;
        Assert.StartsWith("id: ", next);
        Assert.True(long.Parse(next[4..], System.Globalization.CultureInfo.InvariantCulture) > cursor);
        Assert.Equal(StudioRunStatus.Completed, final.Status);
        Assert.Single(host.Model.Requests);
    }
    private sealed class StudioTransportFactAttribute : FactAttribute
    {
        public StudioTransportFactAttribute()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("RUN_STUDIO_INTEGRATION_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
                Skip = "Set RUN_STUDIO_INTEGRATION_TESTS=true to enable real loopback transport checks.";
        }
    }
}

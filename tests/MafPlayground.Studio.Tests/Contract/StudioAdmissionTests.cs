using MafPlayground.AI;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using MafPlayground.Studio.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Studio.Tests.Contract;

public sealed class StudioAdmissionTests
{
    [Fact]
    public async Task Explicit_local_claim_policy_allows_both_transports_and_preserves_provenance()
    {
        await using var host = await StudioTransportHost.Create(configure: services =>
        {
            services.AddSingleton(new StudioSourceProvenance(new string('b', 40), true));
            services.AddAuthorization(options => options.AddPolicy("allowed", p => p.RequireAssertion(context => context.Resource is null).RequireClaim("ai_permission", "basic").RequireClaim(System.Security.Claims.ClaimTypes.NameIdentifier, "studio-local")));
            services.AddAIRuntime(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
                .AddAgent<ReviewEntity, ReviewRequest, ReviewResult>("protected", e => e.WithDescription("Protected review").RequireAuthorization("allowed"));
        });
        var request = new StudioRunRequest("allowed", "protected", "cli-default", JsonSerializer.SerializeToElement(new { review_items = Array.Empty<object>() }), null);
        var response = await host.Http.PostAsJsonAsync("/api/studio/v1/runs", request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = await host.Terminal((await response.Content.ReadFromJsonAsync<StudioRunReceipt>())!.RunId);
        Assert.Equal(StudioRunStatus.Completed, run.Status);
        Assert.Equal(new string('b', 40), run.Provenance!.Revision); Assert.True(run.Provenance.Dirty);
        await using var mcp = await host.Connect();
        var next = await StudioMcpContractTests.Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request with { RequestId = "allowed-mcp" } });
        Assert.Equal(StudioRunStatus.Completed, (await host.Terminal(next.RunId)).Status);
    }

    [Fact]
    public async Task Generic_pre_model_errors_are_sanitized_inspectable_and_compared_identically_by_transports()
    {
        await using var host = await StudioTransportHost.Create(configure: services =>
        {
            services.AddSingleton(new StudioOptions { Secrets = ["private-secret"] });
            services.AddAIRuntime(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
                .AddAgent<ThrowingEntity, string, string>("throwing", e => e.WithDescription("Throw before any model").AllowAnonymousAccess());
        });
        var runs = new List<StudioRunSnapshot>();
        foreach (string message in new[] { "failure A private-secret", "failure B private-secret" })
        {
            var response = await host.Http.PostAsJsonAsync("/api/studio/v1/runs", new StudioRunRequest(Guid.NewGuid().ToString(), "throwing", "cli-default", JsonSerializer.SerializeToElement(message), null));
            var run = await host.Terminal((await response.Content.ReadFromJsonAsync<StudioRunReceipt>())!.RunId); runs.Add(run);
            var page = (await host.Http.GetFromJsonAsync<StudioEventPage>($"/api/studio/v1/runs/{run.RunId}/event-page"))!;
            var error = Assert.Single(page.Events, x => x.Kind == "execution_error");
            Assert.Null(error.ExecutorId); Assert.Null(error.InvocationId);
            Assert.Contains("failure", error.Data.ToString()); Assert.DoesNotContain("private-secret", error.Data.ToString());
        }
        var comparison = new StudioComparisonRequest(runs[0].HostInstanceId, runs[0].RunId, runs[1].RunId);
        var http = await host.Http.PostAsJsonAsync("/api/studio/v1/comparisons", comparison);
        var expected = (await http.Content.ReadFromJsonAsync<StudioComparison>())!;
        await using var mcp = await host.Connect();
        var actual = await StudioMcpContractTests.Call<StudioComparison>(mcp, "studio_compare_runs", new() { ["request"] = comparison });
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        Assert.Contains(actual.Changes, x => x.Path.StartsWith("/errors", StringComparison.Ordinal));
        Assert.Contains("failure A", JsonSerializer.Serialize(actual));
        Assert.DoesNotContain("private-secret", JsonSerializer.Serialize(actual));
        Assert.False(actual.Baseline.ErrorCoverageComplete);
        Assert.Empty(host.Model.Requests);
    }
    public sealed class ThrowingEntity : IAIEntity<string, string>
    {
        public ValueTask<string> ExecuteAsync(string input, AIExecutionContext context, CancellationToken cancellationToken) => throw new InvalidOperationException(input);
    }

    [Theory]
    [InlineData("{\"items\":{\"key\":null},\"notes\":[null]}", false)]
    [InlineData("{\"items\":{\"key\":{\"title\":\"ok\",\"priority\":\"Normal\"}},\"notes\":[null]}", true)]
    public async Task Generic_dictionary_values_and_nullable_collection_elements_follow_contract(string json, bool accepted)
    {
        await using var host = await StudioTransportHost.Create(configure: services => services
            .AddAIRuntime(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
            .AddAgent<DictionaryEntity, DictionaryInput, int>("dictionary", e => e.WithDescription("Dictionary contract").AllowAnonymousAccess()));
        var request = new StudioRunRequest("dictionary", "dictionary", "cli-default", JsonDocument.Parse(json).RootElement, null);
        var response = await host.Http.PostAsJsonAsync("/api/studio/v1/runs", request);
        Assert.Equal(accepted ? HttpStatusCode.Accepted : HttpStatusCode.BadRequest, response.StatusCode);
        if (!accepted) Assert.Empty(await host.App.Services.GetRequiredService<IStudioRunService>().ListAsync(default));
    }
    public sealed record DictionaryInput(Dictionary<string, ReviewItem> Items, List<string?> Notes);
    public sealed class DictionaryEntity : IAIEntity<DictionaryInput, int>
    {
        public ValueTask<int> ExecuteAsync(DictionaryInput input, AIExecutionContext context, CancellationToken cancellationToken) => ValueTask.FromResult(input.Items.Count);
    }

    [Theory]
    [InlineData("deny")]
    [InlineData("unknown")]
    public async Task Registered_policy_denial_prevents_http_and_mcp_admission(string policy)
    {
        await using var host = await StudioTransportHost.Create(configure: services =>
        {
            services.AddAuthorization(options => options.AddPolicy("deny", p => p.RequireAssertion(_ => false)));
            services.AddAIRuntime(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
                .AddAgent<ReviewEntity, ReviewRequest, ReviewResult>("protected", e => e.WithDescription("Protected review").RequireAuthorization(policy));
        });
        var request = new StudioRunRequest("denied", "protected", "cli-default", JsonSerializer.SerializeToElement(new { review_items = Array.Empty<object>() }), null);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Http.PostAsJsonAsync("/api/studio/v1/runs", request)).StatusCode);
        await using var mcp = await host.Connect();
        var result = await mcp.CallToolAsync("studio_start_run", new Dictionary<string, object?> { ["request"] = request, ["newSession"] = true });
        Assert.True(result.IsError);
        Assert.Contains("entity_forbidden", JsonSerializer.Serialize(result));
        Assert.Empty(await host.App.Services.GetRequiredService<IStudioRunService>().ListAsync(default));
        Assert.Empty(await host.App.Services.GetRequiredService<StudioSessionService>().ListAsync(default));
        Assert.Empty(host.Model.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"review_items\":null}")]
    [InlineData("{\"review_items\":[{\"priority\":\"Urgent\"}]}")]
    [InlineData("{\"review_items\":[{\"title\":null,\"priority\":\"Urgent\"}]}")]
    [InlineData("{\"review_items\":[null]}")]
    public async Task Invalid_typed_contract_never_reserves_or_executes(string input)
    {
        await using var host = await StudioTransportHost.Create();
        var request = new StudioRunRequest("invalid", "novel-review", "cli-default", JsonDocument.Parse(input).RootElement, null);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Http.PostAsJsonAsync("/api/studio/v1/runs", request)).StatusCode);
        await using var mcp = await host.Connect();
        var result = await mcp.CallToolAsync("studio_start_run", new Dictionary<string, object?> { ["request"] = request });
        Assert.True(result.IsError);
        Assert.Contains("invalid_request", JsonSerializer.Serialize(result));
        Assert.Empty(await host.App.Services.GetRequiredService<IStudioRunService>().ListAsync(default));
        Assert.Empty(await host.App.Services.GetRequiredService<StudioSessionService>().ListAsync(default));
    }

    [Fact]
    public async Task Serializer_case_alias_enum_nullable_and_constructor_defaults_remain_compatible()
    {
        await using var host = await StudioTransportHost.Create();
        var request = new StudioRunRequest("valid", "novel-review", "cli-default", JsonDocument.Parse("""{"REVIEW_ITEMS":[{"TITLE":"Case","PRIORITY":"Urgent","Note":null}]}""").RootElement, null);
        var response = await host.Http.PostAsJsonAsync("/api/studio/v1/runs", request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<StudioRunReceipt>())!;
        Assert.Equal(StudioRunStatus.Completed, (await host.Terminal(receipt.RunId)).Status);
        await using var mcp = await host.Connect();
        var other = await StudioMcpContractTests.Call<StudioRunReceipt>(mcp, "studio_start_run", new() { ["request"] = request with { RequestId = "valid-mcp" } });
        Assert.Equal(StudioRunStatus.Completed, (await host.Terminal(other.RunId)).Status);
    }
}

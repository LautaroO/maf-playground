using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Http;
using MafPlayground.Studio.Tests.Fixtures;

namespace MafPlayground.Studio.Tests.Contract;

public sealed class StudioProfileDiscoveryTests
{
    [Theory]
    [InlineData(null, "fake:alternate", false, true)]
    [InlineData("fake:model", "missing:model", true, false)]
    public async Task Http_and_mcp_discovery_match_selected_profile_and_execution_preflight(string? defaultModel, string alternateModel, bool defaultAvailable, bool alternateAvailable)
    {
        await using var host = await StudioTransportHost.Create(defaultModel: defaultModel, alternateModel: alternateModel);
        await using var mcp = await host.Connect();
        foreach ((string profile, bool available) in new[] { ("cli-default", defaultAvailable), ("alternate", alternateAvailable), ("cli-default", defaultAvailable) })
        {
            var http = await host.Http.GetFromJsonAsync<StudioEntityDescriptor[]>(StudioEndpoints.Prefix + "/entities?profileId=" + profile);
            Assert.Equal(available, http!.Single(entity => entity.Id == "basic-agent").IsAvailable);
            var page = await StudioMcpContractTests.Call<StudioMcpPage<StudioEntityDescriptor>>(mcp, "studio_list_entities", new() { ["profileId"] = profile });
            Assert.Equal(available, page.Items.Single(entity => entity.Id == "basic-agent").IsAvailable);
            Assert.Equal(http!.Select(entity => (entity.Id, entity.IsAvailable)), page.Items.Select(entity => (entity.Id, entity.IsAvailable)));
            var request = new StudioRunRequest(Guid.NewGuid().ToString("N"), "basic-agent", profile, JsonSerializer.SerializeToElement(new { message = "Hello" }), null);
            using var response = await host.Http.PostAsJsonAsync(StudioEndpoints.Prefix + "/runs", request);
            Assert.Equal(available ? HttpStatusCode.Accepted : HttpStatusCode.Conflict, response.StatusCode);
            if (available)
            {
                var receipt = (await response.Content.ReadFromJsonAsync<StudioRunReceipt>())!;
                Assert.Equal(StudioRunStatus.Completed, (await host.Terminal(receipt.RunId)).Status);
            }
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Http.GetAsync(StudioEndpoints.Prefix + "/entities?profileId=unknown")).StatusCode);
        Assert.True((await mcp.CallToolAsync("studio_list_entities", new Dictionary<string, object?> { ["profileId"] = "unknown" })).IsError);
    }

    [Theory]
    [InlineData("unregistered-in-ui.stellar-survey-9", false)]
    [InlineData("unregistered-in-ui/stellar-survey-9", true)]
    [InlineData("literal%2Fsurvey%25name", true)]
    public async Task Http_topology_query_roundtrips_registered_and_transport_only_ids_without_double_decoding(string id, bool transportOnly)
    {
        await using var host = await StudioTransportHost.Create(additionalIds: transportOnly ? null : [id], opaqueTransportId: transportOnly ? id : null);
        await using var mcp = await host.Connect();
        if (!transportOnly)
        {
            var entities = await host.Http.GetFromJsonAsync<StudioEntityDescriptor[]>(StudioEndpoints.Prefix + "/entities?profileId=alternate");
            Assert.Contains(entities!, entity => entity.Id == id && entity.SupportsNativeWorkflow);
        }
        using var response = await host.Http.GetAsync(StudioEndpoints.Prefix + "/topology?entityId=" + Uri.EscapeDataString(id) + "&profileId=alternate");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var topology = (await response.Content.ReadFromJsonAsync<StudioTopology>())!;
        Assert.Equal(id, topology.EntityId);
        Assert.Equal(StudioTopologyStatus.Available, topology.Status);
        Assert.Contains(topology.Nodes, node => node.Id == "finish-alternate");
        var tool = await StudioMcpContractTests.Call<StudioTopology>(mcp, "studio_get_topology", new() { ["entityId"] = id, ["profileId"] = "alternate" });
        Assert.Equal(topology.EntityId, tool.EntityId);
        Assert.Equal(topology.Nodes.Select(node => node.Id), tool.Nodes.Select(node => node.Id));
        Assert.Empty(host.Model.Requests);
    }
}

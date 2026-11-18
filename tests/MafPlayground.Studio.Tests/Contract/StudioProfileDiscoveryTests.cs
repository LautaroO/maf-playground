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
    public async Task Http_discovery_matches_selected_profile_and_execution_preflight(string? defaultModel, string alternateModel, bool defaultAvailable, bool alternateAvailable)
    {
        await using var host = await StudioTransportHost.Create(defaultModel: defaultModel, alternateModel: alternateModel);
        foreach ((string profile, bool available) in new[] { ("cli-default", defaultAvailable), ("alternate", alternateAvailable), ("cli-default", defaultAvailable) })
        {
            var http = await host.Http.GetFromJsonAsync<StudioEntityDescriptor[]>(StudioEndpoints.Prefix + "/entities?profileId=" + profile);
            Assert.Equal(available, http!.Single(entity => entity.Id == "basic-agent").IsAvailable);
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
    }

    [Theory]
    [InlineData("novel.stellar-survey-9", false)]
    [InlineData("novel/stellar-survey-9", true)]
    [InlineData("literal%2Fsurvey%25name", true)]
    public async Task Http_topology_query_roundtrips_registered_and_transport_only_ids_without_double_decoding(string id, bool transportOnly)
    {
        await using var host = await StudioTransportHost.Create(additionalIds: transportOnly ? null : [id], opaqueTransportId: transportOnly ? id : null);
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
        Assert.Empty(host.Model.Requests);
    }
}

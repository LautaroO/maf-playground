using MafPlayground.AI;
using MafPlayground.AI.Runtime.Workflows;
using MafPlayground.AI.Workflows.Translation;
using MafPlayground.Studio.Catalog;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Tests.Fixtures;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Studio.Tests.Component;

public sealed class WorkflowTopologyMapperTests
{
    [Fact]
    public async Task TranslationTopology_PreservesConfiguredBranchesStartAggregationAndRetryCycles()
    {
        ServiceCollection services = new();
        services.AddAIRuntime(new ConfigurationBuilder().Build()).AddTranslationWorkflowEntity();
        services.Configure<TranslationWorkflowOptions>(options => options.SupportedTargetLanguages = ["de", "it"]);
        services.AddStudioCatalog();
        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        StudioTopology topology = await scope.ServiceProvider.GetRequiredService<IStudioCatalog>()
            .GetTopologyAsync("translation-workflow", "opaque-profile", CancellationToken.None);

        Assert.Equal(StudioTopologyStatus.Available, topology.Status);
        Assert.Equal("translation-input", topology.StartNodeId);
        Assert.Equal(["translate-de", "translate-it", "translation-aggregate", "translation-input", "validate-de", "validate-it"],
            topology.Nodes.Select(node => node.Id));
        Assert.Contains(topology.Edges, edge => edge.SourceIds.SequenceEqual(["translation-input"]) &&
            edge.TargetIds.Order().SequenceEqual(new[] { "translate-de", "translate-it" }) && edge.Kind == "fanOut");
        Assert.Contains(topology.Edges, edge => edge.SourceIds.SequenceEqual(["validate-de"]) && edge.TargetIds.SequenceEqual(["translate-de"]));
        Assert.Contains(topology.Edges, edge => edge.SourceIds.SequenceEqual(["validate-it"]) && edge.TargetIds.SequenceEqual(["translation-aggregate"]));
        Assert.NotEmpty(topology.LabelsUnavailableReason!);
        Assert.All(topology.Edges, edge => Assert.Null(edge.Label));
        Assert.Contains("retry with feedback", topology.NativeMermaid);
    }

    [Fact]
    public async Task NativeSource_ReceivesOpaqueProfileAndMapsChangedGraphWithoutExecutingHandlers()
    {
        ServiceCollection services = new();
        services.AddAIRuntime(new ConfigurationBuilder().Build())
            .AddWorkflow<UnconstructableEntity, string, string>("novel-native", entity => entity
                .WithDescription("Novel graph").AllowAnonymousAccess()
                .WithMafWorkflowSource((_, context) => new ProfileWorkflowSource(context.ProfileId)));
        services.AddStudioCatalog();
        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        IStudioCatalog catalog = scope.ServiceProvider.GetRequiredService<IStudioCatalog>();

        StudioTopology first = await catalog.GetTopologyAsync("novel-native", "profile-x", CancellationToken.None);
        StudioTopology second = await catalog.GetTopologyAsync("novel-native", "profile-y", CancellationToken.None);

        Assert.Equal(["finish-profile-x", "start"], first.Nodes.Select(node => node.Id));
        Assert.Equal(["finish-profile-y", "start"], second.Nodes.Select(node => node.Id));
        Assert.Equal("start", first.StartNodeId);
        Assert.Single(first.Edges);
        Assert.Null(first.Edges[0].Label);
    }

    private sealed class ProfileWorkflowSource(string profileId) : IMafWorkflowSource
    {
        public Workflow CreateWorkflow()
        {
            NeverRunExecutor start = new("start");
            NeverRunExecutor finish = new($"finish-{profileId}");
            return new WorkflowBuilder(start).AddEdge(start, finish).WithOutputFrom(finish).Build();
        }

        public Workflow CreateWorkflowForDevUI(string workflowName) => throw new NotSupportedException();
    }
}

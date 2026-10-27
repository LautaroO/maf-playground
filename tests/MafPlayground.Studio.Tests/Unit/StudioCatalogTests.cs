using System.Text.Json;
using MafPlayground.AI;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Registry;
using MafPlayground.Studio.Catalog;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Studio.Tests.Unit;

public sealed class StudioCatalogTests
{
    [Fact]
    public async Task ListAsync_DiscoversNovelContractsWithoutConstructingEntities()
    {
        ServiceCollection services = new();
        services.AddAIRuntime(new ConfigurationBuilder().Build())
            .AddWorkflow<UnconstructableEntity, string, string>("unknown-flow", entity => entity
                .WithDescription("Unknown workflow").AllowAnonymousAccess())
            .AddAgent<ReviewEntity, ReviewRequest, ReviewResult>("review", entity => entity
                .WithDescription("Review items").AllowAnonymousAccess());
        services.AddStudioCatalog();
        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        IReadOnlyList<StudioEntityDescriptor> entities = await scope.ServiceProvider
            .GetRequiredService<IStudioCatalog>().ListAsync(CancellationToken.None);

        Assert.Equal(["review", "unknown-flow"], entities.Select(entity => entity.Id));
        StudioEntityDescriptor review = Assert.Single(entities, entity => entity.Id == "review");
        Assert.Null(review.InputExample);
        Assert.False(review.SupportsNativeAgent);
        Assert.False(review.SupportsNativeWorkflow);
        Assert.True(review.IsAvailable);
        JsonElement properties = review.InputSchema.GetProperty("properties");
        Assert.False(properties.TryGetProperty("items", out _));
        JsonElement items = properties.GetProperty("review_items");
        Assert.Equal("array", items.GetProperty("type").GetString());
        JsonElement itemProperties = items.GetProperty("items").GetProperty("properties");
        Assert.Equal(["Normal", "Urgent"], itemProperties.GetProperty("priority")
            .GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("null", itemProperties.GetProperty("note").GetProperty("type")
            .EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("array", review.OutputSchema.GetProperty("properties")
            .GetProperty("titles").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ListAsync_ReportsOnlyAffectedEntitiesUnavailable()
    {
        ServiceCollection services = new();
        services.AddAIRuntime(new ConfigurationBuilder().Build())
            .AddAgent<UnconstructableEntity, string, string>("needs-retrieval", entity => entity
                .WithDescription("Needs retrieval").AllowAnonymousAccess()
                .WithAvailability(_ => AIEntityAvailability.Unavailable("Retrieval is not configured.")))
            .AddAgent<ReviewEntity, ReviewRequest, ReviewResult>("review", entity => entity
                .WithDescription("Review items").AllowAnonymousAccess());
        services.AddStudioCatalog();
        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        IReadOnlyList<StudioEntityDescriptor> entities = await scope.ServiceProvider
            .GetRequiredService<IStudioCatalog>().ListAsync(CancellationToken.None);

        StudioEntityDescriptor unavailable = Assert.Single(entities, entity => entity.Id == "needs-retrieval");
        Assert.False(unavailable.IsAvailable);
        Assert.Equal("Retrieval is not configured.", unavailable.UnavailableReason);
        Assert.True(Assert.Single(entities, entity => entity.Id == "review").IsAvailable);
    }

    [Fact]
    public async Task GetTopologyAsync_UsesExplicitUnavailableStateAndHonorsCancellation()
    {
        ServiceCollection services = new();
        services.AddAIRuntime(new ConfigurationBuilder().Build())
            .AddWorkflow<UnconstructableEntity, string, string>("unknown-flow", entity => entity
                .WithDescription("Unknown workflow").AllowAnonymousAccess());
        services.AddStudioCatalog();
        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        IStudioCatalog catalog = scope.ServiceProvider.GetRequiredService<IStudioCatalog>();

        StudioTopology topology = await catalog.GetTopologyAsync("unknown-flow", "opaque-profile", CancellationToken.None);

        Assert.Equal(StudioTopologyStatus.Unavailable, topology.Status);
        Assert.NotEmpty(topology.UnavailableReason!);
        Assert.Empty(topology.Nodes);
        await Assert.ThrowsAsync<AIEntityNotFoundException>(() =>
            catalog.GetTopologyAsync("missing", "opaque-profile", CancellationToken.None).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            catalog.ListAsync(new CancellationToken(canceled: true)).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            catalog.GetTopologyAsync("unknown-flow", "opaque-profile", new CancellationToken(canceled: true)).AsTask());
    }
}

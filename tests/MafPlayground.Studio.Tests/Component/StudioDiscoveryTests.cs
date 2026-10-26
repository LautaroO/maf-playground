using System.Text.Json;
using MafPlayground.AI;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Agents.HelpAgent;
using MafPlayground.AI.Runtime.Registry;
using MafPlayground.Studio.Catalog;
using MafPlayground.Studio.Tests.Fixtures;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Studio.Tests.Component;

public sealed class StudioDiscoveryTests
{
    [Fact]
    public async Task RegisteredSamples_AppearOnceWithExamplesWithoutProviderResolution()
    {
        ServiceCollection services = new();
        services.AddAIRuntime(new ConfigurationBuilder().Build()).AddMafPlaygroundSampleEntities();
        services.AddStudioCatalog();
        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        var entities = await scope.ServiceProvider.GetRequiredService<IStudioCatalog>().ListAsync(CancellationToken.None);

        Assert.Equal(["basic-agent", "basic-rag-agent", "repository-help-agent", "translation-workflow"], entities.Select(entity => entity.Id));
        Assert.All(entities, entity => Assert.NotNull(entity.InputExample));
        Assert.True(Assert.Single(entities, entity => entity.Id == "basic-agent").SupportsNativeAgent);
        Assert.True(Assert.Single(entities, entity => entity.Id == "translation-workflow").SupportsNativeWorkflow);
        Assert.DoesNotContain(typeof(StudioCatalog).Assembly.GetReferencedAssemblies(), assembly =>
            assembly.Name!.Contains("Samples", StringComparison.Ordinal) ||
            assembly.Name.Contains("Providers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AllFourEntities_BuildWithValidationAndReportMissingGroundedDependencies()
    {
        await using StudioExecutionFixture fixture = new();
        await using var scope = fixture.Provider.CreateAsyncScope();
        var entities = await scope.ServiceProvider.GetRequiredService<IStudioCatalog>().ListAsync(default);
        Assert.Equal(["basic-agent", "basic-rag-agent", "repository-help-agent", "translation-workflow"], entities.Select(entity => entity.Id));
        Assert.True(Assert.Single(entities, entity => entity.Id == "basic-agent").IsAvailable);
        Assert.True(Assert.Single(entities, entity => entity.Id == "translation-workflow").IsAvailable);
        foreach (string id in new[] { "basic-rag-agent", "repository-help-agent" })
        {
            var entity = Assert.Single(entities, entity => entity.Id == id);
            Assert.False(entity.IsAvailable);
            Assert.Equal("Knowledge search is not configured.", entity.UnavailableReason);
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.RunAsync(new { message = "hello" }, id));
            Assert.Equal(entity.UnavailableReason, error.Message);
        }
        Assert.Empty(fixture.Client.Models);
        Assert.Equal("hello world", (await fixture.RunAsync(new { message = "hello" })).Output.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitLegacySources_WithInvalidDependenciesStillFailProviderValidation(bool help)
    {
        AggregateException error = Assert.Throws<AggregateException>(() => new StudioExecutionFixture(services =>
        {
            if (help) services.AddHelpAgent(new("host-help", "Host help", "Explain this host."));
            else services.AddBasicRagAgent();
        }));
        Assert.Contains(typeof(IChatClient).FullName!, error.Message);
    }

    [Fact]
    public async Task NovelEntity_ExecutesAndSerializesUsingTheDiscoveredContracts()
    {
        ServiceCollection services = new();
        services.AddAIRuntime(new ConfigurationBuilder().Build())
            .AddAgent<ReviewEntity, ReviewRequest, ReviewResult>("novel-review", entity => entity
                .WithDescription("Review items").AllowAnonymousAccess()
                .WithInputExample(new ReviewRequest([new ReviewItem("One", ReviewPriority.Urgent)])));
        services.AddStudioCatalog();
        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        var descriptor = Assert.Single(await scope.ServiceProvider.GetRequiredService<IStudioCatalog>()
            .ListAsync(CancellationToken.None));

        AIEntityInvocationResult result = await scope.ServiceProvider.GetRequiredService<IAIEntityInvoker>()
            .InvokeAsync("novel-review", descriptor.InputExample!.Value,
                AIExecutionContext.Create(new AICallerContext("local")), CancellationToken.None);
        JsonElement output = JsonSerializer.SerializeToElement(result.Output, AIJsonSerializerOptions.Default);

        Assert.Equal("One", output.GetProperty("titles")[0].GetString());
        Assert.Equal(1, output.GetProperty("count").GetInt32());
        Assert.Equal("Urgent", descriptor.InputExample.Value.GetProperty("review_items")[0].GetProperty("priority").GetString());
    }

    [Fact]
    public void DuplicateIdsAndMismatchedExamples_AreRejectedByNormalRegistration()
    {
        ServiceCollection services = new();
        var builder = services.AddAIRuntime(new ConfigurationBuilder().Build());
        builder.AddAgent<ReviewEntity, ReviewRequest, ReviewResult>("review", entity => entity
            .WithDescription("Review items").AllowAnonymousAccess());

        Assert.Throws<InvalidOperationException>(() => builder
            .AddAgent<ReviewEntity, ReviewRequest, ReviewResult>("REVIEW", entity => entity
                .WithDescription("Duplicate").AllowAnonymousAccess()));
        Assert.Throws<InvalidOperationException>(() => builder
            .AddAgent<ReviewEntity, ReviewRequest, ReviewResult>("bad-example", entity => entity
                .WithDescription("Mismatched").AllowAnonymousAccess().WithInputExample("wrong contract")));
    }
}
